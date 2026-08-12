using FluentAssertions;
using MaisonCalliard.Application.Files;
using MaisonCalliard.Application.Products;
using MaisonCalliard.Application.Products.Dtos;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Domain.ValueObjects;
using Moq;

namespace MaisonCalliard.UnitTests;

public sealed class ProductServiceImagePositionTests
{
    private readonly Mock<IProductRepository> _productRepositoryMock = new();
    private readonly Mock<IFileStorageService> _fileStorageMock = new();
    private readonly ProductService _sut;

    public ProductServiceImagePositionTests()
    {
        _sut = new ProductService(_productRepositoryMock.Object, _fileStorageMock.Object);
    }

    [Fact]
    public async Task CreateAsync_WithoutImagePosition_DefaultsToCenter()
    {
        var request = CreateRequest();

        var result = await _sut.CreateAsync(request, null, null, null);

        _productRepositoryMock.Verify(
            r => r.AddAsync(It.Is<Product>(p => p.ImagePosition == "50% 50%"), default),
            Times.Once);
        result.ImagePosition.Should().Be("50% 50%");
    }

    [Fact]
    public async Task CreateAsync_WithValidImagePosition_PersistsAndReturnsValue()
    {
        var request = CreateRequest();
        request.ImagePosition = "50% 75%";

        var result = await _sut.CreateAsync(request, null, null, null);

        _productRepositoryMock.Verify(
            r => r.AddAsync(It.Is<Product>(p => p.ImagePosition == "50% 75%"), default),
            Times.Once);
        result.ImagePosition.Should().Be("50% 75%");
    }

    [Fact]
    public async Task CreateAsync_WithInvalidImagePosition_ThrowsArgumentException()
    {
        var request = CreateRequest();
        request.ImagePosition = "center";

        var act = async () => await _sut.CreateAsync(request, null, null, null);

        await act.Should().ThrowAsync<ArgumentException>();
        _productRepositoryMock.Verify(r => r.AddAsync(It.IsAny<Product>(), default), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WithoutImagePosition_KeepsExistingValue()
    {
        var product = CreateProduct("30% 40%");
        _productRepositoryMock.Setup(r => r.GetByIdAsync(product.Id, default)).ReturnsAsync(product);

        var request = CreateUpdateRequest();
        var result = await _sut.UpdateAsync(product.Id, request, null, null, null);

        result.ImagePosition.Should().Be("30% 40%");
        _productRepositoryMock.Verify(
            r => r.UpdateAsync(It.Is<Product>(p => p.ImagePosition == "30% 40%"), default),
            Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithValidImagePosition_UpdatesValue()
    {
        var product = CreateProduct("50% 50%");
        _productRepositoryMock.Setup(r => r.GetByIdAsync(product.Id, default)).ReturnsAsync(product);

        var request = CreateUpdateRequest();
        request.ImagePosition = "20% 80%";
        var result = await _sut.UpdateAsync(product.Id, request, null, null, null);

        result.ImagePosition.Should().Be("20% 80%");
        _productRepositoryMock.Verify(
            r => r.UpdateAsync(It.Is<Product>(p => p.ImagePosition == "20% 80%"), default),
            Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithOutOfRangeImagePosition_ThrowsArgumentException()
    {
        var product = CreateProduct("50% 50%");
        _productRepositoryMock.Setup(r => r.GetByIdAsync(product.Id, default)).ReturnsAsync(product);

        var request = CreateUpdateRequest();
        request.ImagePosition = "101% 50%";

        var act = async () => await _sut.UpdateAsync(product.Id, request, null, null, null);

        await act.Should().ThrowAsync<ArgumentException>();
        _productRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Product>(), default), Times.Never);
    }

    private static Product CreateProduct(string imagePosition) => new()
    {
        Id = Guid.NewGuid(),
        Name = new LocalizedText { Se = "Tårta", En = "Cake" },
        Description = new LocalizedText { Se = "Beskrivning", En = "Description" },
        Category = ProductCategory.Cake,
        ImagePosition = imagePosition,
        Ingredients = new LocalizedText { Se = "Ingredienser", En = "Ingredients" },
        PriceOptions = [new PriceOption { Label = "4-6 bitar", Price = 420 }]
    };

    private static CreateProductRequest CreateRequest() => new()
    {
        NameSe = "Tårta",
        NameEn = "Cake",
        Category = ProductCategory.Cake,
        IngredientsSe = "Ingredienser",
        IngredientsEn = "Ingredients",
        PriceOptions = [new PriceOptionDto { Label = "4-6 bitar", Price = 420 }]
    };

    private static UpdateProductRequest CreateUpdateRequest() => new()
    {
        NameSe = "Tårta",
        NameEn = "Cake",
        Category = ProductCategory.Cake,
        IngredientsSe = "Ingredienser",
        IngredientsEn = "Ingredients",
        PriceOptions = [new PriceOptionDto { Label = "4-6 bitar", Price = 420 }]
    };
}
