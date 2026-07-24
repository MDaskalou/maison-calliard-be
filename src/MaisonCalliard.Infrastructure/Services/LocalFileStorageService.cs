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
        var fileName = Path.GetFileName(new Uri(fileUrl).LocalPath);
        var filePath = Path.Combine(_uploadPath, fileName);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }
}
