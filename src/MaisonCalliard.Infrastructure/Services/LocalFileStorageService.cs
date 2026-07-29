using MaisonCalliard.Application.Files;

namespace MaisonCalliard.Infrastructure.Services;

// TODO: Replace with a real blob storage implementation (e.g. Azure Blob Storage).
internal sealed class LocalFileStorageService : IFileStorageService
{
    private readonly string _uploadPath;
    private readonly string _baseUrl;
    private readonly ImageProcessor _imageProcessor;

    public LocalFileStorageService(string uploadPath, string baseUrl, ImageProcessor imageProcessor)
    {
        _uploadPath = uploadPath;
        _baseUrl = baseUrl;
        _imageProcessor = imageProcessor;
    }

    public async Task<string> SaveAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var processed = await _imageProcessor.ProcessAsync(fileStream, cancellationToken);
        var uniqueName = $"{Guid.NewGuid():N}.webp";
        var filePath = Path.Combine(_uploadPath, uniqueName);
        var temporaryPath = Path.Combine(_uploadPath, $".{uniqueName}.{Guid.NewGuid():N}.tmp");

        Directory.CreateDirectory(_uploadPath);
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, processed.Bytes, cancellationToken);
            File.Move(temporaryPath, filePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return $"{_baseUrl.TrimEnd('/')}/uploads/{uniqueName}";
    }

    public Task DeleteAsync(string fileUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            return Task.CompletedTask;
        }

        var fileName = TryGetFileName(fileUrl);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Task.CompletedTask;
        }

        var filePath = Path.GetFullPath(Path.Combine(_uploadPath, fileName));
        var uploadRoot = Path.GetFullPath(_uploadPath).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(filePath), uploadRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    private static string? TryGetFileName(string fileUrl)
    {
        if (Uri.TryCreate(fileUrl, UriKind.Absolute, out var absoluteUri))
        {
            return Path.GetFileName(absoluteUri.LocalPath);
        }

        // Relative paths (e.g. /uploads/file.webp) or bare filenames from older data.
        var normalized = fileUrl.Replace('\\', '/');
        var slashIndex = normalized.LastIndexOf('/');
        return slashIndex >= 0 ? normalized[(slashIndex + 1)..] : normalized;
    }
}
