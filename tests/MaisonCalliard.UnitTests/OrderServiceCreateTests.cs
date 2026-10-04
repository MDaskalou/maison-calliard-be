using FluentAssertions;
using MaisonCalliard.Application.Orders;
using MaisonCalliard.Application.Orders.Dtos;
using MaisonCalliard.Application.Payments;
using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Domain.ValueObjects;
using Moq;

namespace MaisonCalliard.UnitTests;

public sealed class OrderServiceCreateTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();
    private readonly Mock<IProductRepository> _productRepositoryMock = new();
    private readonly OrderService _sut;

    public OrderServiceCreateTests()
    {
        _sut = new OrderService(
            _orderRepositoryMock.Object,
            _productRepositoryMock.Object,
            Mock.Of<IOrderReceiptService>(),
            Mock.Of<IPaymentService>());
    }

    [Fact]
    public async Task CreateAsync_WithoutCustomerAddress_PersistsNullAndReturnsOtherFields()
    {
        var productId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        _productRepositoryMock
            .Setup(r => r.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product
            {
                Id = productId,
                Name = new LocalizedText { Se = "Jordgubbstårta", En = "Strawberry cake" },
                IsAvailable = true,
                TaxRate = 12,
                PriceOptions =
                [
                    new PriceOption { Label = "8 bitar", Price = 450m }
                ]
            });

        Order? savedOrder = null;
        _orderRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Callback<Order, CancellationToken>((order, _) => savedOrder = order)
            .Returns(Task.CompletedTask);

        var pickup = new DateTime(2026, 5, 11, 16, 0, 0, DateTimeKind.Utc);
        var request = new CreateOrderRequest
        {
            PickupDateTime = pickup,
            Location = "Maison Caillard, Mölndal",
            CustomerName = "Eva Ek",
            Email = "eva@example.com",
            Phone = "070-999 88 77",
            Items =
            [
                new CartItemDto
                {
                    ProductId = productId.ToString(),
                    OptionLabel = "8 bitar",
                    Quantity = 1
                }
            ]
        };

        var result = await _sut.CreateAsync(request);

        savedOrder.Should().NotBeNull();
        savedOrder!.CustomerAddress.Should().BeNull();
        savedOrder.PickupDateTime.Should().Be(pickup);
        savedOrder.Location.Should().Be(request.Location);
        savedOrder.CustomerName.Should().Be(request.CustomerName);
        savedOrder.Email.Should().Be(request.Email);
        savedOrder.Phone.Should().Be(request.Phone);
        savedOrder.Items.Should().ContainSingle();

        result.CustomerAddress.Should().BeNull();
        result.PickupDateTime.Should().Be(pickup);
        result.Location.Should().Be(request.Location);
        result.CustomerName.Should().Be(request.CustomerName);
        result.Email.Should().Be(request.Email);
        result.Phone.Should().Be(request.Phone);
        result.Items.Should().ContainSingle();
    }
}
