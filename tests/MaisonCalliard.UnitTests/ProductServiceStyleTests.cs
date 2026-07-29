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

public sealed class ProductServiceStyleTests
{
    private readonly Mock<IProductRepository> _productRepositoryMock = new();
    private readonly Mock<IFileStorageService> _fileStorageMock = new();
    private readonly ProductService _sut;

    public ProductServiceStyleTests()
    {
        _sut = new ProductService(_productRepositoryMock.Object, _fileStorageMock.Object);
    }

    [Fact]
    public async Task GetAllAsync_WithoutStyle_ReturnsAllProductsIncludingStyle()
    {
        var products = new List<Product>
        {
            CreateProduct(CakeStyle.Entremet),
            CreateProduct(CakeStyle.Tarte),
            CreateProduct(style: null)
        };
        _productRepositoryMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync(products);

        var result = await _sut.GetAllAsync();

        result.Should().HaveCount(3);
        result.Select(p => p.Style).Should().BeEquivalentTo(new CakeStyle?[] { CakeStyle.Entremet, CakeStyle.Tarte, null });
    }

    [Fact]
    public async Task GetAllAsync_WithStyle_ReturnsOnlyMatchingProducts()
    {
        var products = new List<Product>
        {
            CreateProduct(CakeStyle.Entremet),
            CreateProduct(CakeStyle.Tarte),
            CreateProduct(style: null)
        };
        _productRepositoryMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync(products);

        var result = await _sut.GetAllAsync(CakeStyle.Entremet);

        result.Should().HaveCount(1);
        result[0].Style.Should().Be(CakeStyle.Entremet);
    }

    [Fact]
    public async Task CreateAsync_WithStyle_PersistsAndReturnsStyle()
    {
        var request = CreateRequest(CakeStyle.Tarte);

        var result = await _sut.CreateAsync(request, null, null, null);

        _productRepositoryMock.Verify(
            r => r.AddAsync(It.Is<Product>(p => p.Style == CakeStyle.Tarte), default),
            Times.Once);
        result.Style.Should().Be(CakeStyle.Tarte);
        result.Category.Should().Be(ProductCategory.Cake);
    }

    [Fact]
    public async Task CreateAsync_WithoutStyle_PersistsNullStyle()
    {
        var request = CreateRequest(style: null);

        var result = await _sut.CreateAsync(request, null, null, null);

        _productRepositoryMock.Verify(
            r => r.AddAsync(It.Is<Product>(p => p.Style == null), default),
            Times.Once);
        result.Style.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_WithStyle_UpdatesStyle()
    {
        var product = CreateProduct(style: null);
        _productRepositoryMock.Setup(r => r.GetByIdAsync(product.Id, default)).ReturnsAsync(product);

        var request = CreateUpdateRequest(CakeStyle.Entremet);
        var result = await _sut.UpdateAsync(product.Id, request, null, null, null);

        result.Style.Should().Be(CakeStyle.Entremet);
        _productRepositoryMock.Verify(r => r.UpdateAsync(It.Is<Product>(p => p.Style == CakeStyle.Entremet), default), Times.Once);
    }

    private static Product CreateProduct(CakeStyle? style) => new()
    {
        Id = Guid.NewGuid(),
        Name = new LocalizedText { Se = "Tårta", En = "Cake" },
        Description = new LocalizedText { Se = "Beskrivning", En = "Description" },
        Category = ProductCategory.Cake,
        Style = style,
        Ingredients = new LocalizedText { Se = "Ingredienser", En = "Ingredients" },
        PriceOptions = [new PriceOption { Label = "4-6 bitar", Price = 420 }]
    };

    private static CreateProductRequest CreateRequest(CakeStyle? style) => new()
    {
        NameSe = "Tårta",
        NameEn = "Cake",
        Category = ProductCategory.Cake,
        Style = style,
        IngredientsSe = "Ingredienser",
        IngredientsEn = "Ingredients",
        PriceOptions = [new PriceOptionDto { Label = "4-6 bitar", Price = 420 }]
    };

    private static UpdateProductRequest CreateUpdateRequest(CakeStyle? style) => new()
    {
        NameSe = "Tårta",
        NameEn = "Cake",
        Category = ProductCategory.Cake,
        Style = style,
        IngredientsSe = "Ingredienser",
        IngredientsEn = "Ingredients",
        PriceOptions = [new PriceOptionDto { Label = "4-6 bitar", Price = 420 }]
    };
}
