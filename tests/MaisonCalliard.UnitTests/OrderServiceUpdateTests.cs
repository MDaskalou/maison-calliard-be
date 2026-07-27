using FluentAssertions;
using MaisonCalliard.Application.Orders;
using MaisonCalliard.Application.Orders.Dtos;
using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Domain.ValueObjects;
using Moq;

namespace MaisonCalliard.UnitTests;

public sealed class OrderServiceUpdateTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();
    private readonly Mock<IProductRepository> _productRepositoryMock = new();
    private readonly Mock<IOrderReceiptService> _orderReceiptServiceMock = new();
    private readonly OrderService _sut;

    private readonly Guid _cakeProductId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _croissantProductId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public OrderServiceUpdateTests()
    {
        _sut = new OrderService(
            _orderRepositoryMock.Object,
            _productRepositoryMock.Object,
            _orderReceiptServiceMock.Object);

        SetupProduct(
            _cakeProductId,
            "Jordgubbstårta",
            "8 bitar",
            450m,
            12);
        SetupProduct(
            _croissantProductId,
            "Croissant",
            "1 styck",
            35m,
            12);
    }

    [Fact]
    public async Task UpdateAsync_WhenAddonHasIsPaidFalse_PersistsUnpaidFlag()
    {
        var order = CreatePaidOrder();
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        Order? savedOrder = null;
        _orderRepositoryMock
            .Setup(r => r.ReplaceItemsAsync(order.Id, It.IsAny<IReadOnlyList<CartItem>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orderRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Callback<Order, CancellationToken>((updatedOrder, _) => savedOrder = updatedOrder)
            .Returns(Task.CompletedTask);

        var request = new UpdateOrderRequest
        {
            Status = OrderStatus.Paid,
            PickupDateTime = order.PickupDateTime,
            Location = order.Location,
            CustomerName = order.CustomerName,
            Email = order.Email,
            Phone = order.Phone,
            Message = order.Message,
            Items =
            [
                new CartItemDto
                {
                    CartId = "existing-cart-id-1",
                    ProductId = _cakeProductId.ToString(),
                    OptionLabel = "8 bitar",
                    Quantity = 1
                },
                new CartItemDto
                {
                    CartId = "new-addon-1",
                    ProductId = _croissantProductId.ToString(),
                    OptionLabel = "1 styck",
                    Quantity = 1,
                    IsPaid = false
                }
            ]
        };

        var result = await _sut.UpdateAsync(order.Id, request);

        savedOrder.Should().NotBeNull();
        savedOrder!.Items.Should().HaveCount(2);
        savedOrder.Items.Single(i => i.CartId == "existing-cart-id-1").IsPaid.Should().BeTrue();
        savedOrder.Items.Single(i => i.CartId == "new-addon-1").IsPaid.Should().BeFalse();
        result.Items.Single(i => i.CartId == "new-addon-1").IsPaid.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_WhenItemOmitsIsPaid_TreatsAsPaid()
    {
        var order = CreatePaidOrder();
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _orderRepositoryMock
            .Setup(r => r.ReplaceItemsAsync(order.Id, It.IsAny<IReadOnlyList<CartItem>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orderRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateOrderRequest
        {
            Status = OrderStatus.Paid,
            PickupDateTime = order.PickupDateTime,
            Location = order.Location,
            CustomerName = order.CustomerName,
            Email = order.Email,
            Phone = order.Phone,
            Items =
            [
                new CartItemDto
                {
                    CartId = "existing-cart-id-1",
                    ProductId = _cakeProductId.ToString(),
                    OptionLabel = "8 bitar",
                    Quantity = 1
                }
            ]
        };

        var result = await _sut.UpdateAsync(order.Id, request);

        result.Items.Should().ContainSingle();
        result.Items[0].IsPaid.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_PreservesPaymentMetadataAndRecalculatesTotals()
    {
        var paidAt = new DateTime(2026, 5, 9, 10, 0, 0, DateTimeKind.Utc);
        var order = CreatePaidOrder(paidAt);
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        Order? savedOrder = null;
        _orderRepositoryMock
            .Setup(r => r.ReplaceItemsAsync(order.Id, It.IsAny<IReadOnlyList<CartItem>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orderRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Callback<Order, CancellationToken>((updatedOrder, _) => savedOrder = updatedOrder)
            .Returns(Task.CompletedTask);

        var request = new UpdateOrderRequest
        {
            Status = OrderStatus.Paid,
            PickupDateTime = order.PickupDateTime,
            Location = order.Location,
            CustomerName = order.CustomerName,
            Email = order.Email,
            Phone = order.Phone,
            Items =
            [
                new CartItemDto
                {
                    CartId = "existing-cart-id-1",
                    ProductId = _cakeProductId.ToString(),
                    OptionLabel = "8 bitar",
                    Quantity = 1
                },
                new CartItemDto
                {
                    CartId = "new-addon-1",
                    ProductId = _croissantProductId.ToString(),
                    OptionLabel = "1 styck",
                    Quantity = 1,
                    IsPaid = false
                }
            ]
        };

        var result = await _sut.UpdateAsync(order.Id, request);

        savedOrder.Should().NotBeNull();
        savedOrder!.PaidAt.Should().Be(paidAt);
        savedOrder.StripeSessionId.Should().Be(order.StripeSessionId);
        savedOrder.StripePaymentIntentId.Should().Be(order.StripePaymentIntentId);
        savedOrder.ReceiptNumber.Should().Be(order.ReceiptNumber);
        savedOrder.PaymentMethod.Should().Be(order.PaymentMethod);
        savedOrder.IsPrinted.Should().BeTrue();
        savedOrder.CustomerAddress.Should().Be(order.CustomerAddress);
        savedOrder.Status.Should().Be(OrderStatus.Paid);
        savedOrder.Total.Should().Be(485m);
        result.Total.Should().Be(485m);
        result.TaxAmount.Should().BeApproximately(51.96m, 0.01m);
    }

    [Fact]
    public async Task UpdateAsync_WhenOrderNotFound_ThrowsKeyNotFoundException()
    {
        var orderId = Guid.NewGuid();
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var act = () => _sut.UpdateAsync(orderId, new UpdateOrderRequest());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private void SetupProduct(Guid id, string name, string optionLabel, decimal price, decimal taxRate)
    {
        _productRepositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product
            {
                Id = id,
                Name = new LocalizedText { Se = name, En = name },
                IsAvailable = true,
                TaxRate = (double)taxRate,
                PriceOptions =
                [
                    new PriceOption { Label = optionLabel, Price = price }
                ]
            });
    }

    private Order CreatePaidOrder(DateTime? paidAt = null)
    {
        var orderId = Guid.NewGuid();
        return new Order
        {
            Id = orderId,
            Status = OrderStatus.Paid,
            PaidAt = paidAt ?? new DateTime(2026, 5, 9, 10, 0, 0, DateTimeKind.Utc),
            PaymentMethod = "Stripe",
            StripeSessionId = "cs_test_123",
            StripePaymentIntentId = "pi_test_123",
            ReceiptNumber = "MC-2026-000001",
            IsPrinted = true,
            CustomerAddress = "Storgatan 1, 431 00 Mölndal",
            CustomerEmailSentAt = new DateTime(2026, 5, 9, 10, 5, 0, DateTimeKind.Utc),
            PickupDateTime = new DateTime(2026, 5, 10, 14, 0, 0, DateTimeKind.Utc),
            Location = "Maison Caillard, Mölndal",
            CustomerName = "Anna Andersson",
            Email = "anna@example.com",
            Phone = "070-123 45 67",
            Message = "Utan nötter",
            CreatedAt = new DateTime(2026, 5, 9, 9, 0, 0, DateTimeKind.Utc),
            Items =
            [
                new CartItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    CartId = "existing-cart-id-1",
                    ProductId = _cakeProductId.ToString(),
                    Name = "Jordgubbstårta",
                    OptionLabel = "8 bitar",
                    Price = 450m,
                    Quantity = 1,
                    TaxRate = 12m,
                    IsPaid = true
                }
            ],
            Total = 450m,
            TaxAmount = 48.21m
        };
    }
}
