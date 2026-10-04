using FluentAssertions;
using MaisonCalliard.Application.Orders;
using MaisonCalliard.Application.Payments;
using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Domain.Repositories;
using Moq;

namespace MaisonCalliard.UnitTests;

public sealed class OrderServiceArchiveTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();
    private readonly OrderService _sut;

    public OrderServiceArchiveTests()
    {
        _sut = new OrderService(
            _orderRepositoryMock.Object,
            Mock.Of<IProductRepository>(),
            Mock.Of<IOrderReceiptService>(),
            Mock.Of<IPaymentService>());
    }

    [Fact]
    public async Task ArchiveAsync_SetsArchivedAtAndLeavesStatusAndTotal()
    {
        var order = CreatePaidOrder();
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _orderRepositoryMock
            .Setup(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var before = DateTimeOffset.UtcNow;
        var result = await _sut.ArchiveAsync(order.Id);
        var after = DateTimeOffset.UtcNow;

        result.ArchivedAt.Should().NotBeNull();
        result.ArchivedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        result.Status.Should().Be(OrderStatus.Paid);
        result.Total.Should().Be(450m);
        order.Status.Should().Be(OrderStatus.Paid);
        order.Total.Should().Be(450m);
        _orderRepositoryMock.Verify(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ArchiveAsync_WhenAlreadyArchived_KeepsExistingTimestamp()
    {
        var archivedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var order = CreatePaidOrder();
        order.ArchivedAt = archivedAt;
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _orderRepositoryMock
            .Setup(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.ArchiveAsync(order.Id);

        result.ArchivedAt.Should().Be(archivedAt);
        result.Status.Should().Be(OrderStatus.Paid);
        result.Total.Should().Be(450m);
        order.ArchivedAt.Should().Be(archivedAt);
    }

    [Fact]
    public async Task UnarchiveAsync_ClearsArchivedAtAndLeavesStatusAndTotal()
    {
        var order = CreatePaidOrder();
        order.ArchivedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _orderRepositoryMock
            .Setup(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.UnarchiveAsync(order.Id);

        result.ArchivedAt.Should().BeNull();
        result.Status.Should().Be(OrderStatus.Paid);
        result.Total.Should().Be(450m);
        order.ArchivedAt.Should().BeNull();
        _orderRepositoryMock.Verify(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnarchiveAsync_WhenAlreadyActive_ReturnsOrderUnchanged()
    {
        var order = CreatePaidOrder();
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _orderRepositoryMock
            .Setup(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _sut.UnarchiveAsync(order.Id);

        result.ArchivedAt.Should().BeNull();
        result.Status.Should().Be(OrderStatus.Paid);
        result.Total.Should().Be(450m);
    }

    [Fact]
    public async Task ArchiveAsync_WhenOrderNotFound_ThrowsKeyNotFoundException()
    {
        var orderId = Guid.NewGuid();
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var act = () => _sut.ArchiveAsync(orderId);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UnarchiveAsync_WhenOrderNotFound_ThrowsKeyNotFoundException()
    {
        var orderId = Guid.NewGuid();
        _orderRepositoryMock
            .Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var act = () => _sut.UnarchiveAsync(orderId);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAllAsync_ForwardsIncludeArchived(bool includeArchived)
    {
        _orderRepositoryMock
            .Setup(r => r.GetAllAsync(includeArchived, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Order>());

        await _sut.GetAllAsync(includeArchived);

        _orderRepositoryMock.Verify(
            r => r.GetAllAsync(includeArchived, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Order CreatePaidOrder()
    {
        var orderId = Guid.NewGuid();
        return new Order
        {
            Id = orderId,
            Status = OrderStatus.Paid,
            PaidAt = new DateTime(2026, 5, 9, 10, 0, 0, DateTimeKind.Utc),
            PaymentMethod = "Stripe",
            PickupDateTime = new DateTime(2026, 5, 10, 14, 0, 0, DateTimeKind.Utc),
            Location = "Maison Caillard, Mölndal",
            CustomerName = "Anna Andersson",
            Email = "anna@example.com",
            Phone = "070-123 45 67",
            CreatedAt = new DateTime(2026, 5, 9, 9, 0, 0, DateTimeKind.Utc),
            Total = 450m,
            TaxAmount = 48.21m,
            Items = []
        };
    }
}
