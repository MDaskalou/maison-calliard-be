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
    private readonly string _storageBaseUrl;
    private readonly string _publicBaseUrl;

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

        var projectUrl = _options.Url.TrimEnd('/');
        _storageBaseUrl = $"{projectUrl}/storage/v1/object";
        _publicBaseUrl = $"{projectUrl}/storage/v1/object/public/{_options.StorageBucket}";
    }

    public async Task<string> SaveAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var processed = await _imageProcessor.ProcessAsync(fileStream, cancellationToken);
        var objectName = $"{Guid.NewGuid():N}.webp";
        var uploadUrl = $"{_storageBaseUrl}/{_options.StorageBucket}/{objectName}";

        using var content = new ByteArrayContent(processed.Bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/webp");

        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl)
        {
            Content = content
        };
        request.Headers.Add("x-upsert", "false");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Supabase upload failed for {ObjectName}: {StatusCode} {Body}",
                objectName,
                (int)response.StatusCode,
                body);
            response.EnsureSuccessStatusCode();
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

        var deleteUrl = $"{_storageBaseUrl}/{_options.StorageBucket}/{objectName}";
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
