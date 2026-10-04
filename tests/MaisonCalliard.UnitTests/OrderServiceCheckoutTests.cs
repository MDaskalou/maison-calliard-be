using FluentAssertions;
using MaisonCalliard.Application.Orders;
using MaisonCalliard.Application.Orders.Dtos;
using MaisonCalliard.Application.Payments;
using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Domain.Repositories;
using Moq;

namespace MaisonCalliard.UnitTests;

public sealed class OrderServiceCheckoutTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();
    private readonly Mock<IPaymentService> _paymentServiceMock = new();
    private readonly OrderService _sut;

    public OrderServiceCheckoutTests()
    {
        _sut = new OrderService(
            _orderRepositoryMock.Object,
            Mock.Of<IProductRepository>(),
            Mock.Of<IOrderReceiptService>(),
            _paymentServiceMock.Object);
    }

    [Fact]
    public async Task UpdateCheckoutAsync_UpdatesCustomerFieldsAndLeavesCartUntouched()
    {
        var order = CreateDraft();
        var originalItems = order.Items.ToList();
        var originalTotal = order.Total;
        var originalTax = order.TaxAmount;
        var originalLocation = order.Location;
        var originalIntentId = order.StripePaymentIntentId;
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        Order? savedOrder = null;
        _orderRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Callback<Order, CancellationToken>((updated, _) => savedOrder = updated)
            .Returns(Task.CompletedTask);

        var request = new UpdateCheckoutRequest
        {
            PickupDateTime = new DateTime(2026, 5, 11, 16, 0, 0, DateTimeKind.Utc),
            CustomerName = "Eva Ek",
            CustomerAddress = "Nya gatan 2, 411 00 Göteborg",
            Email = "eva@example.com",
            Phone = "070-999 88 77",
            Message = "Extra gafflar"
        };

        var result = await _sut.UpdateCheckoutAsync(order.Id, request);

        savedOrder.Should().NotBeNull();
        savedOrder!.PickupDateTime.Should().Be(request.PickupDateTime);
        savedOrder.CustomerName.Should().Be(request.CustomerName);
        savedOrder.CustomerAddress.Should().Be(request.CustomerAddress);
        savedOrder.Email.Should().Be(request.Email);
        savedOrder.Phone.Should().Be(request.Phone);
        savedOrder.Message.Should().Be(request.Message);
        savedOrder.Items.Should().BeEquivalentTo(originalItems);
        savedOrder.Location.Should().Be(originalLocation);
        savedOrder.Total.Should().Be(originalTotal);
        savedOrder.TaxAmount.Should().Be(originalTax);
        savedOrder.StripePaymentIntentId.Should().Be(originalIntentId);
        savedOrder.Status.Should().Be(OrderStatus.AwaitingPayment);
        result.Email.Should().Be(request.Email);
        result.StripePaymentIntentId.Should().Be(originalIntentId);

        _paymentServiceMock.Verify(p => p.EnsureUnpaidCheckoutReusableAsync(
            originalIntentId,
            order.StripeSessionId,
            originalLocation,
            request.Email,
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCheckoutAsync_WhenEmailIsUnchanged_DoesNotAskStripeToUpdateReceiptEmail()
    {
        var order = CreateDraft();
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _orderRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateCheckoutRequest
        {
            PickupDateTime = order.PickupDateTime.AddHours(1),
            CustomerName = "Nytt namn",
            CustomerAddress = order.CustomerAddress,
            Email = order.Email,
            Phone = order.Phone,
            Message = order.Message
        };

        await _sut.UpdateCheckoutAsync(order.Id, request);

        _paymentServiceMock.Verify(p => p.EnsureUnpaidCheckoutReusableAsync(
            order.StripePaymentIntentId,
            order.StripeSessionId,
            order.Location,
            order.Email,
            false,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCheckoutAsync_WhenOrderIsMissing_ThrowsKeyNotFoundException()
    {
        var orderId = Guid.NewGuid();
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var act = () => _sut.UpdateCheckoutAsync(orderId, new UpdateCheckoutRequest());

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage(CheckoutUpdateMessages.NotFound);
    }

    [Fact]
    public async Task UpdateCheckoutAsync_WhenOrderIsPaid_ThrowsAndDoesNotSave()
    {
        var order = CreateDraft();
        order.Status = OrderStatus.Paid;
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var act = () => _sut.UpdateCheckoutAsync(order.Id, new UpdateCheckoutRequest { Email = "new@example.com" });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(CheckoutUpdateMessages.NotUpdatable);
        _orderRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
        _paymentServiceMock.Verify(p => p.EnsureUnpaidCheckoutReusableAsync(
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateCheckoutAsync_WhenDraftHasNoCardPayment_Throws()
    {
        var order = CreateDraft();
        order.StripePaymentIntentId = null;
        order.StripeSessionId = null;
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var act = () => _sut.UpdateCheckoutAsync(order.Id, new UpdateCheckoutRequest());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(CheckoutUpdateMessages.NotUpdatable);
    }

    [Fact]
    public async Task UpdateCheckoutAsync_WhenPaymentIntentCannotBeReused_DoesNotSave()
    {
        var order = CreateDraft();
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _paymentServiceMock
            .Setup(p => p.EnsureUnpaidCheckoutReusableAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CheckoutDraftConflictException(CheckoutUpdateMessages.NotReusable));

        var act = () => _sut.UpdateCheckoutAsync(order.Id, new UpdateCheckoutRequest
        {
            CustomerName = "Ska inte sparas",
            Email = "ny@example.com"
        });

        await act.Should().ThrowAsync<CheckoutDraftConflictException>()
            .WithMessage(CheckoutUpdateMessages.NotReusable);
        order.CustomerName.Should().Be("Anna Andersson");
        _orderRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Order CreateDraft()
    {
        var orderId = Guid.NewGuid();
        return new Order
        {
            Id = orderId,
            Status = OrderStatus.AwaitingPayment,
            StripePaymentIntentId = "pi_live_123",
            StripeSessionId = null,
            PickupDateTime = new DateTime(2026, 5, 10, 14, 0, 0, DateTimeKind.Utc),
            Location = "Maison Caillard, Mölndal",
            CustomerName = "Anna Andersson",
            CustomerAddress = "Storgatan 1, 431 00 Mölndal",
            Email = "anna@example.com",
            Phone = "070-123 45 67",
            Message = "Utan nötter",
            Total = 450m,
            TaxAmount = 48.21m,
            Items =
            [
                new CartItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    CartId = "cart-1",
                    ProductId = Guid.NewGuid().ToString(),
                    Name = "Jordgubbstårta",
                    OptionLabel = "8 bitar",
                    Price = 450m,
                    Quantity = 1,
                    TaxRate = 12m
                }
            ]
        };
    }
}
