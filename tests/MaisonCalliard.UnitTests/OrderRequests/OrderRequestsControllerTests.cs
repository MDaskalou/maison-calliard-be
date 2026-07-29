using System.Text.Json;
using FluentAssertions;
using MaisonCalliard.Api.Controllers;
using MaisonCalliard.Application.OrderRequests;
using MaisonCalliard.Application.OrderRequests.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace MaisonCalliard.UnitTests.OrderRequests;

public sealed class OrderRequestsControllerTests
{
    private readonly Mock<IOrderRequestService> _serviceMock = new();
    private readonly OrderRequestsController _sut;

    public OrderRequestsControllerTests()
    {
        _sut = new OrderRequestsController(_serviceMock.Object, Mock.Of<ILogger<OrderRequestsController>>());
    }

    [Fact]
    public async Task Create_WhenRequestIsNull_ReturnsBadRequest()
    {
        var result = await _sut.Create(null, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { message = "Missing order request details." });
        _serviceMock.Verify(
            s => s.SendAsync(It.IsAny<CreateOrderRequestMailDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_WhenValidationFails_ReturnsBadRequest()
    {
        _serviceMock
            .Setup(s => s.SendAsync(It.IsAny<CreateOrderRequestMailDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Missing order request details."));

        var result = await _sut.Create(new CreateOrderRequestMailDto(), CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { message = "Missing order request details." });
    }

    [Fact]
    public async Task Create_WhenBothEmailsSucceed_ReturnsOkWithoutConfirmationError()
    {
        _serviceMock
            .Setup(s => s.SendAsync(It.IsAny<CreateOrderRequestMailDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderRequestMailResultDto { Ok = true, ConfirmationSent = true });

        var result = await _sut.Create(new CreateOrderRequestMailDto(), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"confirmationSent\":true");
        json.Should().NotContain("confirmationError");
    }

    [Fact]
    public async Task Create_WhenCustomerConfirmationFails_ReturnsOkWithConfirmationError()
    {
        _serviceMock
            .Setup(s => s.SendAsync(It.IsAny<CreateOrderRequestMailDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderRequestMailResultDto
            {
                Ok = true,
                ConfirmationSent = false,
                ConfirmationError = "provider failed"
            });

        var result = await _sut.Create(new CreateOrderRequestMailDto(), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("\"confirmationSent\":false");
        json.Should().Contain("provider failed");
    }

    [Fact]
    public async Task Create_WhenConfigurationMissing_Returns500()
    {
        _serviceMock
            .Setup(s => s.SendAsync(It.IsAny<CreateOrderRequestMailDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OrderRequestConfigurationException("not configured"));

        var result = await _sut.Create(new CreateOrderRequestMailDto(), CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task Create_WhenCafeDeliveryFails_Returns502()
    {
        _serviceMock
            .Setup(s => s.SendAsync(It.IsAny<CreateOrderRequestMailDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OrderRequestDeliveryException("cafe failed"));

        var result = await _sut.Create(new CreateOrderRequestMailDto(), CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public void MethodNotAllowed_Returns405()
    {
        var result = _sut.MethodNotAllowed();

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status405MethodNotAllowed);
        objectResult.Value.Should().BeEquivalentTo(new { message = "Method not allowed" });
    }
}
