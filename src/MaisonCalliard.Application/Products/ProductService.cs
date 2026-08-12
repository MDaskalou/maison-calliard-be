using System.Text.RegularExpressions;
using MaisonCalliard.Application.Files;
using MaisonCalliard.Application.Products.Dtos;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Domain.ValueObjects;

namespace MaisonCalliard.Application.Products;

public interface IProductService
{
    Task<IReadOnlyList<ProductDto>> GetAllAsync(CakeStyle? style = null, CancellationToken cancellationToken = default);
    Task<ProductDto> CreateAsync(CreateProductRequest request, Stream? imageStream, string? imageFileName, string? imageContentType, CancellationToken cancellationToken = default);
    Task<ProductDto> UpdateAsync(Guid id, UpdateProductRequest request, Stream? imageStream, string? imageFileName, string? imageContentType, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductDto> ToggleAvailabilityAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class ProductService : IProductService
{
    private static readonly Regex ImagePositionRegex = new(@"^\d{1,3}%\s+\d{1,3}%$", RegexOptions.Compiled);
    private const string DefaultImagePosition = "50% 50%";

    private readonly IProductRepository _productRepository;
    private readonly IFileStorageService _fileStorage;

    public ProductService(IProductRepository productRepository, IFileStorageService fileStorage)
    {
        _productRepository = productRepository;
        _fileStorage = fileStorage;
    }

    public async Task<IReadOnlyList<ProductDto>> GetAllAsync(CakeStyle? style = null, CancellationToken cancellationToken = default)
    {
        var products = await _productRepository.GetAllAsync(cancellationToken);
        if (style is not null)
        {
            products = products.Where(p => p.Style == style).ToList();
        }

        return products.Select(MapToDto).ToList();
    }

    public async Task<ProductDto> CreateAsync(
        CreateProductRequest request,
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

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = new LocalizedText { Se = request.NameSe, En = request.NameEn },
            Description = new LocalizedText { Se = request.DescriptionSe ?? string.Empty, En = request.DescriptionEn ?? string.Empty },
            Category = request.Category,
            Style = request.Style,
            ImageUrl = imageUrl,
            ImagePosition = ResolveImagePositionForCreate(request.ImagePosition),
            IsAvailable = request.IsAvailable,
            IsVegan = request.IsVegan,
            IsSeason = request.IsSeason,
            BakedOnSite = request.BakedOnSite,
            BakedThisMorning = request.BakedThisMorning,
            Stock = request.Stock,
            Ingredients = new LocalizedText { Se = request.IngredientsSe, En = request.IngredientsEn },
            Allergies = request.Allergies,
            PriceOptions = request.PriceOptions.Select(p => new PriceOption { Label = p.Label, Price = p.Price }).ToList(),
            TaxRate = request.TaxRate
        };

        try
        {
            await _productRepository.AddAsync(product, cancellationToken);
        }
        catch
        {
            if (!string.IsNullOrEmpty(imageUrl))
            {
                await _fileStorage.DeleteAsync(imageUrl, CancellationToken.None);
            }

            throw;
        }
        return MapToDto(product);
    }

    public async Task<ProductDto> UpdateAsync(
        Guid id,
        UpdateProductRequest request,
        Stream? imageStream,
        string? imageFileName,
        string? imageContentType,
        CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Product {id} not found.");

        if (imageStream is not null && imageFileName is not null && imageContentType is not null)
        {
            var oldImageUrl = product.ImageUrl;
            var newImageUrl = await _fileStorage.SaveAsync(imageStream, imageFileName, imageContentType, cancellationToken);
            product.ImageUrl = newImageUrl;

            try
            {
                ApplyUpdate(product, request);
                await _productRepository.UpdateAsync(product, cancellationToken);
            }
            catch
            {
                product.ImageUrl = oldImageUrl;
                await _fileStorage.DeleteAsync(newImageUrl, CancellationToken.None);
                throw;
            }

            if (!string.IsNullOrEmpty(oldImageUrl))
            {
                await _fileStorage.DeleteAsync(oldImageUrl, cancellationToken);
            }

            return MapToDto(product);
        }

        ApplyUpdate(product, request);

        await _productRepository.UpdateAsync(product, cancellationToken);
        return MapToDto(product);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Product {id} not found.");

        var imageUrl = product.ImageUrl;
        await _productRepository.DeleteAsync(product, cancellationToken);

        // Best-effort cleanup — product is already removed from the database.
        if (!string.IsNullOrEmpty(imageUrl))
        {
            try
            {
                await _fileStorage.DeleteAsync(imageUrl, cancellationToken);
            }
            catch
            {
                // Ignore storage cleanup failures so delete still succeeds for the client.
            }
        }
    }

    public async Task<ProductDto> ToggleAvailabilityAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Product {id} not found.");

        product.IsAvailable = !product.IsAvailable;
        await _productRepository.UpdateAsync(product, cancellationToken);
        return MapToDto(product);
    }

    private static ProductDto MapToDto(Product product)
    {
        return new ProductDto
        {
            Id = product.Id,
            Name = new LocalizedTextDto { Se = product.Name.Se, En = product.Name.En },
            Description = new LocalizedTextDto { Se = product.Description.Se, En = product.Description.En },
            Category = product.Category,
            Style = product.Style,
            ImageUrl = product.ImageUrl,
            ImagePosition = product.ImagePosition,
            IsAvailable = product.IsAvailable,
            IsVegan = product.IsVegan,
            IsSeason = product.IsSeason,
            BakedOnSite = product.BakedOnSite,
            BakedThisMorning = product.BakedThisMorning,
            Stock = product.Stock,
            Ingredients = new LocalizedTextDto { Se = product.Ingredients.Se, En = product.Ingredients.En },
            Allergies = product.Allergies,
            PriceOptions = product.PriceOptions.Select(p => new PriceOptionDto { Label = p.Label, Price = p.Price }).ToList(),
            TaxRate = product.TaxRate
        };
    }

    private static void ApplyUpdate(Product product, UpdateProductRequest request)
    {
        product.Name = new LocalizedText { Se = request.NameSe, En = request.NameEn };
        product.Description = new LocalizedText { Se = request.DescriptionSe ?? string.Empty, En = request.DescriptionEn ?? string.Empty };
        product.Category = request.Category;
        product.Style = request.Style;
        product.IsAvailable = request.IsAvailable;
        product.IsVegan = request.IsVegan;
        product.IsSeason = request.IsSeason;
        product.BakedOnSite = request.BakedOnSite;
        product.BakedThisMorning = request.BakedThisMorning;
        product.Stock = request.Stock;
        product.Ingredients = new LocalizedText { Se = request.IngredientsSe, En = request.IngredientsEn };
        product.Allergies = request.Allergies;
        product.PriceOptions = request.PriceOptions.Select(p => new PriceOption { Label = p.Label, Price = p.Price }).ToList();
        product.TaxRate = request.TaxRate;

        if (!string.IsNullOrWhiteSpace(request.ImagePosition))
        {
            product.ImagePosition = ValidateImagePosition(request.ImagePosition);
        }
    }

    private static string ResolveImagePositionForCreate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultImagePosition;
        }

        return ValidateImagePosition(value);
    }

    private static string ValidateImagePosition(string value)
    {
        var trimmed = value.Trim();
        if (!ImagePositionRegex.IsMatch(trimmed))
        {
            throw new ArgumentException("ImagePosition must be a CSS object-position percentage pair, e.g. \"50% 50%\".");
        }

        var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !TryParsePercent(parts[0], out var x)
            || !TryParsePercent(parts[1], out var y)
            || x is < 0 or > 100
            || y is < 0 or > 100)
        {
            throw new ArgumentException("ImagePosition percentages must be between 0 and 100.");
        }

        return $"{x}% {y}%";
    }

    private static bool TryParsePercent(string token, out int percent)
    {
        percent = 0;
        if (!token.EndsWith('%'))
        {
            return false;
        }

        return int.TryParse(token.AsSpan(0, token.Length - 1), out percent);
    }
}
