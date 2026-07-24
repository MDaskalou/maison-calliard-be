using MaisonCalliard.Application.Files;
using MaisonCalliard.Application.Menu.Dtos;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Domain.ValueObjects;

namespace MaisonCalliard.Application.Menu;

public interface IMenuService
{
    Task<IReadOnlyList<MenuItemDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<MenuItemDto> CreateAsync(CreateMenuItemRequest request, Stream? imageStream, string? imageFileName, string? imageContentType, CancellationToken cancellationToken = default);
    Task<MenuItemDto> UpdateAsync(Guid id, UpdateMenuItemRequest request, Stream? imageStream, string? imageFileName, string? imageContentType, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class MenuService : IMenuService
{
    private readonly IMenuRepository _menuRepository;
    private readonly IFileStorageService _fileStorage;

    public MenuService(IMenuRepository menuRepository, IFileStorageService fileStorage)
    {
        _menuRepository = menuRepository;
        _fileStorage = fileStorage;
    }

    public async Task<IReadOnlyList<MenuItemDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _menuRepository.GetAllAsync(cancellationToken);
        return items.Select(MapToDto).ToList();
    }

    public async Task<MenuItemDto> CreateAsync(
        CreateMenuItemRequest request,
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

        var item = new MenuItem
        {
            Id = Guid.NewGuid(),
            Name = new LocalizedText { Se = request.NameSe, En = request.NameEn },
            Description = new LocalizedText { Se = "", En = "" },
            Category = request.Category,
            Price = request.Price,
            ImageUrl = imageUrl,
            Ingredients = new LocalizedText { Se = "", En = "" },
            Allergies = [],
            IsAvailable = request.IsAvailable,
            BakedOnSite = request.BakedOnSite,
            BakedThisMorning = request.BakedThisMorning,
            TaxRate = request.TaxRate
        };

        try
        {
            await _menuRepository.AddAsync(item, cancellationToken);
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

    public async Task<MenuItemDto> UpdateAsync(
        Guid id,
        UpdateMenuItemRequest request,
        Stream? imageStream,
        string? imageFileName,
        string? imageContentType,
        CancellationToken cancellationToken = default)
    {
        var item = await _menuRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"MenuItem {id} not found.");

        if (imageStream is not null && imageFileName is not null && imageContentType is not null)
        {
            var oldImageUrl = item.ImageUrl;
            var newImageUrl = await _fileStorage.SaveAsync(imageStream, imageFileName, imageContentType, cancellationToken);
            item.ImageUrl = newImageUrl;

            try
            {
                ApplyUpdate(item, request);
                await _menuRepository.UpdateAsync(item, cancellationToken);
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

        ApplyUpdate(item, request);

        await _menuRepository.UpdateAsync(item, cancellationToken);
        return MapToDto(item);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _menuRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"MenuItem {id} not found.");

        await _menuRepository.DeleteAsync(item, cancellationToken);
        if (!string.IsNullOrEmpty(item.ImageUrl))
        {
            await _fileStorage.DeleteAsync(item.ImageUrl, cancellationToken);
        }
    }

    private static MenuItemDto MapToDto(MenuItem item)
    {
        return new MenuItemDto
        {
            Id = item.Id,
            Name = new LocalizedTextDto { Se = item.Name.Se, En = item.Name.En },
            Description = new LocalizedTextDto { Se = item.Description.Se, En = item.Description.En },
            Category = item.Category,
            Price = item.Price,
            ImageUrl = item.ImageUrl,
            Ingredients = new LocalizedTextDto { Se = item.Ingredients.Se, En = item.Ingredients.En },
            Allergies = item.Allergies,
            IsAvailable = item.IsAvailable,
            BakedOnSite = item.BakedOnSite,
            BakedThisMorning = item.BakedThisMorning,
            TaxRate = item.TaxRate
        };
    }

    private static void ApplyUpdate(MenuItem item, UpdateMenuItemRequest request)
    {
        item.Name = new LocalizedText { Se = request.NameSe, En = request.NameEn };
        item.Category = request.Category;
        item.Price = request.Price;
        item.IsAvailable = request.IsAvailable;
        item.BakedOnSite = request.BakedOnSite;
        item.BakedThisMorning = request.BakedThisMorning;
        item.TaxRate = request.TaxRate;
    }
}
