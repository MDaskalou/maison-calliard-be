using FluentAssertions;
using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Infrastructure.Options;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace MaisonCalliard.UnitTests.Receipts;

public sealed class OrderReceiptServiceResendTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();
    private readonly Mock<IOrderReceiptSender> _senderMock = new();
    private readonly OrderReceiptService _sut;

    public OrderReceiptServiceResendTests()
    {
        _sut = new OrderReceiptService(
            _orderRepositoryMock.Object,
            _senderMock.Object,
            Options.Create(new ReceiptOptions { CompanyName = "Maison Caillard" }),
            new ConfigurationBuilder().Build(),
            NullLogger<OrderReceiptService>.Instance);
    }

    [Fact]
    public async Task ResendReceiptAsync_WhenOrderExists_SendsReceiptAndDoesNotChangeStatus()
    {
        var order = CreateOrder();
        var originalStatus = order.Status;
        var shortOrderId = order.Id.ToString("N")[..8].ToUpperInvariant();

        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _senderMock
            .Setup(s => s.SendAsync(order.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _sut.ResendReceiptAsync(order.Id);

        order.Status.Should().Be(originalStatus);
        order.CustomerEmailSentAt.Should().NotBeNull();
        order.ReceiptSentAt.Should().Be(order.CustomerEmailSentAt);
        _senderMock.Verify(
            s => s.SendAsync(
                order.Email,
                It.Is<string>(subject => subject.Contains(shortOrderId)),
                It.Is<string>(html => html.Contains(order.ReceiptNumber!) && html.Contains("Chokladtarta")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _orderRepositoryMock.Verify(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResendReceiptAsync_WhenOrderDoesNotExist_ThrowsKeyNotFoundException()
    {
        var orderId = Guid.NewGuid();
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var act = async () => await _sut.ResendReceiptAsync(orderId);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _senderMock.Verify(
            s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResendReceiptAsync_WhenOrderHasNoEmail_ThrowsInvalidOperationException()
    {
        var order = CreateOrder();
        order.Email = " ";

        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var act = async () => await _sut.ResendReceiptAsync(order.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Ordern saknar e-postadress.");
        _senderMock.Verify(
            s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResendReceiptAsync_WhenEmailProviderFails_ThrowsDeliveryException()
    {
        var order = CreateOrder();

        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _senderMock
            .Setup(s => s.SendAsync(order.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = async () => await _sut.ResendReceiptAsync(order.Id);

        await act.Should().ThrowAsync<OrderReceiptDeliveryException>();
        _orderRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Order CreateOrder()
    {
        var orderId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        return new Order
        {
            Id = orderId,
            ReceiptNumber = "MC-2026-000123",
            Email = "anna@example.com",
            CustomerName = "Anna Test",
            CustomerAddress = "Testgatan 1",
            Location = "Maison Caillard, Molndal",
            PickupDateTime = new DateTime(2026, 5, 25, 11, 0, 0, DateTimeKind.Utc),
            PaymentMethod = "Stripe kortbetalning",
            Status = OrderStatus.Paid,
            Total = 450m,
            TaxAmount = 54m,
            Items =
            [
                new CartItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    Name = "Chokladtarta",
                    OptionLabel = "8 bitar",
                    Price = 450m,
                    Quantity = 1,
                    TaxRate = 12m
                }
            ]
        };
    }
}
