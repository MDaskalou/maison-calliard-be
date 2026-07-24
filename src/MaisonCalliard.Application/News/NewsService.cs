using MaisonCalliard.Application.Files;
using MaisonCalliard.Application.News.Dtos;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Domain.ValueObjects;

namespace MaisonCalliard.Application.News;

public interface INewsService
{
    Task<IReadOnlyList<NewsItemDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<NewsItemDto> CreateAsync(CreateNewsItemRequest request, Stream? imageStream, string? imageFileName, string? imageContentType, CancellationToken cancellationToken = default);
    Task<NewsItemDto> UpdateAsync(Guid id, UpdateNewsItemRequest request, Stream? imageStream, string? imageFileName, string? imageContentType, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class NewsService : INewsService
{
    private readonly INewsRepository _newsRepository;
    private readonly IFileStorageService _fileStorage;

    public NewsService(INewsRepository newsRepository, IFileStorageService fileStorage)
    {
        _newsRepository = newsRepository;
        _fileStorage = fileStorage;
    }

    public async Task<IReadOnlyList<NewsItemDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _newsRepository.GetAllAsync(cancellationToken);
        return items.Select(MapToDto).ToList();
    }

    public async Task<NewsItemDto> CreateAsync(
        CreateNewsItemRequest request,
        Stream? imageStream,
        string? imageFileName,
        string? imageContentType,
        CancellationToken cancellationToken = default)
    {
        var imageUrl = string.Empty;
        if (imageStream is not null && imageFileName is not null && imageContentType is not null)
        {
            imageUrl = await _fileStorage.SaveAsync(imageStream, imageFileName, imageContentType, cancellationToken);
        }

        var item = new NewsItem
        {
            Id = Guid.NewGuid(),
            Title = new LocalizedText { Se = request.TitleSe, En = request.TitleEn },
            Subtitle = new LocalizedText { Se = request.SubtitleSe, En = request.SubtitleEn },
            ImageUrl = imageUrl,
            Link = request.Link
        };

        try
        {
            await _newsRepository.AddAsync(item, cancellationToken);
        }
        catch
        {
            if (!string.IsNullOrEmpty(imageUrl))
            {
                await _fileStorage.DeleteAsync(imageUrl, CancellationToken.None);
            }

            throw;
        }
        return MapToDto(item);
    }

    public async Task<NewsItemDto> UpdateAsync(
        Guid id,
        UpdateNewsItemRequest request,
        Stream? imageStream,
        string? imageFileName,
        string? imageContentType,
        CancellationToken cancellationToken = default)
    {
        var item = await _newsRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"NewsItem {id} not found.");

        if (imageStream is not null && imageFileName is not null && imageContentType is not null)
        {
            var oldImageUrl = item.ImageUrl;
            var newImageUrl = await _fileStorage.SaveAsync(imageStream, imageFileName, imageContentType, cancellationToken);
            item.ImageUrl = newImageUrl;

            try
            {
                item.Title = new LocalizedText { Se = request.TitleSe, En = request.TitleEn };
                item.Subtitle = new LocalizedText { Se = request.SubtitleSe, En = request.SubtitleEn };
                item.Link = request.Link;
                await _newsRepository.UpdateAsync(item, cancellationToken);
            }
            catch
            {
                item.ImageUrl = oldImageUrl;
                await _fileStorage.DeleteAsync(newImageUrl, CancellationToken.None);
                throw;
            }

            if (!string.IsNullOrEmpty(oldImageUrl))
            {
                await _fileStorage.DeleteAsync(oldImageUrl, cancellationToken);
            }

            return MapToDto(item);
        }

        item.Title = new LocalizedText { Se = request.TitleSe, En = request.TitleEn };
        item.Subtitle = new LocalizedText { Se = request.SubtitleSe, En = request.SubtitleEn };
        item.Link = request.Link;

        await _newsRepository.UpdateAsync(item, cancellationToken);
        return MapToDto(item);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _newsRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"NewsItem {id} not found.");

        await _newsRepository.DeleteAsync(item, cancellationToken);
        if (!string.IsNullOrEmpty(item.ImageUrl))
        {
            await _fileStorage.DeleteAsync(item.ImageUrl, cancellationToken);
        }
    }

    private static NewsItemDto MapToDto(NewsItem item)
    {
        return new NewsItemDto
        {
            Id = item.Id,
            Title = new LocalizedTextDto { Se = item.Title.Se, En = item.Title.En },
            Subtitle = new LocalizedTextDto { Se = item.Subtitle.Se, En = item.Subtitle.En },
            ImageUrl = item.ImageUrl,
            Link = item.Link
        };
    }
}
