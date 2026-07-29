using System.Net.Http.Headers;
using MaisonCalliard.Application.Files;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed class SupabaseStorageService : IFileStorageService
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseStorageOptions _options;
    private readonly ImageProcessor _imageProcessor;
    private readonly ILogger<SupabaseStorageService> _logger;
    private readonly string _projectUrl;
    private readonly string _bucket;
    private readonly string _storageBaseUrl;
    private readonly string _publicBaseUrl;
    private readonly SemaphoreSlim _bucketLock = new(1, 1);
    private bool _bucketReady;

    public SupabaseStorageService(
        HttpClient httpClient,
        IOptions<SupabaseStorageOptions> options,
        ImageProcessor imageProcessor,
        ILogger<SupabaseStorageService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _imageProcessor = imageProcessor;
        _logger = logger;

        _projectUrl = NormalizeProjectUrl(_options.Url);
        _bucket = _options.StorageBucket.Trim();
        _storageBaseUrl = $"{_projectUrl}/storage/v1/object";
        _publicBaseUrl = $"{_projectUrl}/storage/v1/object/public/{_bucket}";
    }

    public async Task<string> SaveAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(cancellationToken);

        var processed = await _imageProcessor.ProcessAsync(fileStream, cancellationToken);
        var objectName = $"{Guid.NewGuid():N}.webp";
        var uploadUrl = $"{_storageBaseUrl}/{_bucket}/{objectName}";

        using var content = new ByteArrayContent(processed.Bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/webp");

        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl)
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("x-upsert", "true");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Supabase upload failed for {ObjectName} to {UploadUrl}: {StatusCode} {Body}",
                objectName,
                uploadUrl,
                (int)response.StatusCode,
                body);
            throw new InvalidOperationException(
                $"Supabase Storage upload failed ({(int)response.StatusCode}): {body}");
        }

        return $"{_publicBaseUrl}/{objectName}";
    }

    public async Task DeleteAsync(string fileUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            return;
        }

        var objectName = TryGetObjectName(fileUrl);
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return;
        }

        var deleteUrl = $"{_storageBaseUrl}/{_bucket}/{objectName}";
        using var response = await _httpClient.DeleteAsync(deleteUrl, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
            "Supabase delete failed for {ObjectName}: {StatusCode} {Body}",
            objectName,
            (int)response.StatusCode,
            body);
    }

    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        if (_bucketReady)
        {
            return;
        }

        await _bucketLock.WaitAsync(cancellationToken);
        try
        {
            if (_bucketReady)
            {
                return;
            }

            var getUrl = $"{_projectUrl}/storage/v1/bucket/{_bucket}";
            using (var getResponse = await _httpClient.GetAsync(getUrl, cancellationToken))
            {
                if (getResponse.IsSuccessStatusCode)
                {
                    _bucketReady = true;
                    return;
                }
            }

            using var createContent = new StringContent(
                $"{{\"id\":\"{_bucket}\",\"name\":\"{_bucket}\",\"public\":true}}",
                System.Text.Encoding.UTF8,
                "application/json");
            using var createResponse = await _httpClient.PostAsync(
                $"{_projectUrl}/storage/v1/bucket",
                createContent,
                cancellationToken);

            if (createResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation("Created Supabase Storage bucket {Bucket}", _bucket);
                _bucketReady = true;
                return;
            }

            var body = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            // Bucket may already exist (race) or name conflict — treat as ready if message says so.
            if ((int)createResponse.StatusCode is 409 or 400 &&
                body.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                _bucketReady = true;
                return;
            }

            _logger.LogError(
                "Failed to create Supabase bucket {Bucket}: {StatusCode} {Body}",
                _bucket,
                (int)createResponse.StatusCode,
                body);
            throw new InvalidOperationException(
                $"Supabase Storage bucket '{_bucket}' is missing and could not be created ({(int)createResponse.StatusCode}): {body}");
        }
        finally
        {
            _bucketLock.Release();
        }
    }

    internal static string NormalizeProjectUrl(string url)
    {
        var normalized = url.Trim().TrimEnd('/');
        if (normalized.EndsWith("/rest/v1", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^"/rest/v1".Length].TrimEnd('/');
        }
        else if (normalized.EndsWith("/storage/v1", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^"/storage/v1".Length].TrimEnd('/');
        }

        return normalized;
    }

    internal static string? TryGetObjectName(string fileUrl)
    {
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        const string publicMarker = "/storage/v1/object/public/";
        var path = uri.AbsolutePath;
        var markerIndex = path.IndexOf(publicMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            var objectPath = path[(markerIndex + publicMarker.Length)..];
            var slashIndex = objectPath.IndexOf('/');
            return slashIndex >= 0 ? objectPath[(slashIndex + 1)..] : null;
        }

        return null;
    }
}
