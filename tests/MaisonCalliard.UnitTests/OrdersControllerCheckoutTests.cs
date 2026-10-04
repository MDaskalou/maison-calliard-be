using FluentAssertions;
using MaisonCalliard.Api.Controllers;
using MaisonCalliard.Application.Orders;
using MaisonCalliard.Application.Orders.Dtos;
using MaisonCalliard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace MaisonCalliard.UnitTests;

public sealed class OrdersControllerCheckoutTests
{
    private readonly Mock<IOrderService> _orderService = new();
    private readonly OrdersController _sut;

    public OrdersControllerCheckoutTests()
    {
        _sut = new OrdersController(_orderService.Object, Mock.Of<ILogger<OrdersController>>());
    }

    [Fact]
    public async Task UpdateCheckout_WhenDraftIsUpdated_Returns200WithOrder()
    {
        var orderId = Guid.NewGuid();
        var order = new OrderDto
        {
            Id = orderId,
            Status = OrderStatus.AwaitingPayment,
            StripePaymentIntentId = "pi_live_123",
            CustomerName = "Eva Ek",
            Email = "eva@example.com"
        };
        _orderService
            .Setup(s => s.UpdateCheckoutAsync(orderId, It.IsAny<UpdateCheckoutRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var result = await _sut.UpdateCheckout(orderId, new UpdateCheckoutRequest
        {
            CustomerName = "Eva Ek",
            Email = "eva@example.com"
        }, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        ok.Value.Should().BeSameAs(order);
    }

    [Fact]
    public async Task UpdateCheckout_WhenOrderIsMissing_Returns404WithMessage()
    {
        var orderId = Guid.NewGuid();
        _orderService
            .Setup(s => s.UpdateCheckoutAsync(orderId, It.IsAny<UpdateCheckoutRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException(CheckoutUpdateMessages.NotFound));

        var result = await _sut.UpdateCheckout(orderId, new UpdateCheckoutRequest(), CancellationToken.None);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.StatusCode.Should().Be(404);
        notFound.Value.Should().BeEquivalentTo(new { message = "Order was not found." });
    }

    [Fact]
    public async Task UpdateCheckout_WhenCheckoutIsNotAnOpenDraft_Returns400WithMessage()
    {
        var orderId = Guid.NewGuid();
        _orderService
            .Setup(s => s.UpdateCheckoutAsync(orderId, It.IsAny<UpdateCheckoutRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(CheckoutUpdateMessages.NotUpdatable));

        var result = await _sut.UpdateCheckout(orderId, new UpdateCheckoutRequest(), CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
        badRequest.Value.Should().BeEquivalentTo(new { message = "Only an unpaid card checkout can be updated." });
    }

    [Fact]
    public async Task UpdateCheckout_WhenDraftCannotBeReused_Returns409WithMessage()
    {
        var orderId = Guid.NewGuid();
        _orderService
            .Setup(s => s.UpdateCheckoutAsync(orderId, It.IsAny<UpdateCheckoutRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CheckoutDraftConflictException(CheckoutUpdateMessages.NotReusable));

        var result = await _sut.UpdateCheckout(orderId, new UpdateCheckoutRequest(), CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.StatusCode.Should().Be(409);
        conflict.Value.Should().BeEquivalentTo(new { message = "This checkout draft can no longer be reused." });
    }
}
