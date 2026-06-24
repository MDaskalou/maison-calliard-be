using FluentAssertions;
using MaisonCalliard.Api.Controllers;
using MaisonCalliard.Application.Orders;
using MaisonCalliard.Application.Receipts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace MaisonCalliard.UnitTests;

public sealed class OrdersControllerReceiptResendTests
{
    private readonly Mock<IOrderService> _orderServiceMock = new();
    private readonly OrdersController _sut;

    public OrdersControllerReceiptResendTests()
    {
        _sut = new OrdersController(_orderServiceMock.Object, Mock.Of<ILogger<OrdersController>>());
    }

    [Fact]
    public void ResendReceipt_RouteMatchesDocumentedOrderIdTemplate()
    {
        var method = typeof(OrdersController).GetMethod(nameof(OrdersController.ResendReceipt));

        method.Should().NotBeNull();
        method!.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false)
            .Cast<HttpPostAttribute>()
            .Should()
            .ContainSingle(attribute => attribute.Template == "{orderId}/receipt/resend");
    }

    [Fact]
    public async Task ResendReceipt_WhenOrderIdIsInvalid_ReturnsNotFoundWithoutSending()
    {
        var result = await _sut.ResendReceipt("not-a-guid", CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        _orderServiceMock.Verify(
            service => service.ResendReceiptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResendReceipt_WhenOrderExists_ReturnsNoContent()
    {
        var orderId = Guid.NewGuid();

        var result = await _sut.ResendReceipt(orderId.ToString(), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        _orderServiceMock.Verify(
            service => service.ResendReceiptAsync(orderId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResendReceipt_WhenOrderDoesNotExist_ReturnsNotFound()
    {
        var orderId = Guid.NewGuid();
        _orderServiceMock
            .Setup(service => service.ResendReceiptAsync(orderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _sut.ResendReceipt(orderId.ToString(), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task ResendReceipt_WhenOrderHasNoEmail_ReturnsBadRequest()
    {
        var orderId = Guid.NewGuid();
        _orderServiceMock
            .Setup(service => service.ResendReceiptAsync(orderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ordern saknar e-postadress."));

        var result = await _sut.ResendReceipt(orderId.ToString(), CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { message = "Ordern saknar e-postadress." });
    }

    [Fact]
    public async Task ResendReceipt_WhenDeliveryFails_ReturnsBadGateway()
    {
        var orderId = Guid.NewGuid();
        _orderServiceMock
            .Setup(service => service.ResendReceiptAsync(orderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OrderReceiptDeliveryException("Provider failed."));

        var result = await _sut.ResendReceipt(orderId.ToString(), CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(502);
    }
}
