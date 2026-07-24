using MaisonCalliard.Application.Files;
using MaisonCalliard.Infrastructure.Data;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;

namespace MaisonCalliard.Infrastructure.Services;

public interface IExistingImageOptimizationService
{
    Task<ExistingImageOptimizationResult> OptimizeAsync(CancellationToken cancellationToken = default);
}

public sealed record ExistingImageOptimizationResult(int OptimizedFiles, int UpdatedRecords, int SkippedFiles, int MissingFiles);

internal sealed class ExistingImageOptimizationService : IExistingImageOptimizationService
{
    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorage;
    private readonly ImageUploadOptions _options;
    private readonly string _uploadPath;
    private readonly ILogger<ExistingImageOptimizationService> _logger;

    public ExistingImageOptimizationService(
        AppDbContext dbContext,
        IFileStorageService fileStorage,
        ImageUploadOptions options,
        string uploadPath,
        ILogger<ExistingImageOptimizationService> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _options = options;
        _uploadPath = Path.GetFullPath(uploadPath);
        _logger = logger;
    }

    public async Task<ExistingImageOptimizationResult> OptimizeAsync(CancellationToken cancellationToken = default)
    {
        var news = await _dbContext.NewsItems.ToListAsync(cancellationToken);
        var menu = await _dbContext.MenuItems.ToListAsync(cancellationToken);
        var products = await _dbContext.Products.ToListAsync(cancellationToken);

        var references = news.Select(item => new ImageReference(() => item.ImageUrl, value => item.ImageUrl = value))
            .Concat(menu.Select(item => new ImageReference(() => item.ImageUrl, value => item.ImageUrl = value)))
            .Concat(products.Select(item => new ImageReference(() => item.ImageUrl, value => item.ImageUrl = value)))
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Url))
            .GroupBy(reference => reference.Url, StringComparer.OrdinalIgnoreCase);

        var optimized = 0;
        var updated = 0;
        var skipped = 0;
        var missing = 0;

        foreach (var group in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var oldUrl = group.Key;
            var filePath = ResolveLocalPath(oldUrl);
            if (filePath is null || !File.Exists(filePath))
            {
                missing++;
                _logger.LogWarning("Referenced upload is missing or not local: {ImageUrl}", oldUrl);
                continue;
            }

            if (await IsAlreadyOptimizedAsync(filePath, cancellationToken))
            {
                skipped++;
                continue;
            }

            string newUrl;
            await using (var source = File.OpenRead(filePath))
            {
                newUrl = await _fileStorage.SaveAsync(source, Path.GetFileName(filePath), "application/octet-stream", cancellationToken);
            }

            var affected = group.ToList();
            foreach (var reference in affected)
            {
                reference.SetUrl(newUrl);
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                foreach (var reference in affected)
                {
                    reference.SetUrl(oldUrl);
                }

                await _fileStorage.DeleteAsync(newUrl, CancellationToken.None);
                throw;
            }

            await _fileStorage.DeleteAsync(oldUrl, cancellationToken);
            optimized++;
            updated += affected.Count;
        }

        return new ExistingImageOptimizationResult(optimized, updated, skipped, missing);
    }

    private async Task<bool> IsAlreadyOptimizedAsync(string path, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(path), ".webp", StringComparison.OrdinalIgnoreCase) ||
            new FileInfo(path).Length > _options.TargetBytes)
        {
            return false;
        }

        await using var stream = File.OpenRead(path);
        var info = await Image.IdentifyAsync(stream, cancellationToken);
        return info is not null && Math.Max(info.Width, info.Height) <= _options.MaxOutputLongSide;
    }

    private string? ResolveLocalPath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine(_uploadPath, Path.GetFileName(uri.LocalPath)));
        return string.Equals(Path.GetDirectoryName(candidate), _uploadPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }

    private sealed record ImageReference(Func<string> GetUrl, Action<string> SetUrl)
    {
        public string Url => GetUrl();
    }
}
