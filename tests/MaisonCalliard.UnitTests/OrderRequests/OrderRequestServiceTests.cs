using FluentAssertions;
using MaisonCalliard.Application.OrderRequests;
using MaisonCalliard.Application.OrderRequests.Dtos;
using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Infrastructure.Options;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace MaisonCalliard.UnitTests.OrderRequests;

public sealed class OrderRequestServiceTests
{
    private readonly Mock<IOrderReceiptSender> _senderMock = new();
    private readonly OrderRequestOptions _orderRequestOptions = new()
    {
        ToEmail = "cafe@maisoncaillard.com",
        FromEmail = "info@maisoncaillard.com"
    };
    private readonly ResendOptions _resendOptions = new()
    {
        Enabled = true,
        ApiKey = "re_test"
    };

    private OrderRequestService CreateSut() =>
        new(
            _senderMock.Object,
            Options.Create(_orderRequestOptions),
            Options.Create(_resendOptions),
            NullLogger<OrderRequestService>.Instance);

    [Fact]
    public async Task SendAsync_WhenValid_SendsCafeAndCustomerEmails()
    {
        var request = CreateValidRequest();
        _senderMock
            .Setup(s => s.SendAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ReturnsAsync(true);

        var result = await CreateSut().SendAsync(request);

        result.Ok.Should().BeTrue();
        result.ConfirmationSent.Should().BeTrue();
        result.ConfirmationError.Should().BeNull();
        _senderMock.Verify(
            s => s.SendAsync(
                "cafe@maisoncaillard.com",
                OrderRequestEmailRenderer.CafeSubject,
                It.Is<string>(html => html.Contains("Anna Andersson") && html.Contains("Produktnamn")),
                It.IsAny<CancellationToken>(),
                "info@maisoncaillard.com"),
            Times.Once);
        _senderMock.Verify(
            s => s.SendAsync(
                "anna@example.com",
                OrderRequestEmailRenderer.CustomerSubject,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                "info@maisoncaillard.com"),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_WhenCustomerConfirmationFails_ReturnsOkWithConfirmationSentFalse()
    {
        var request = CreateValidRequest();
        _senderMock
            .Setup(s => s.SendAsync(
                "cafe@maisoncaillard.com",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ReturnsAsync(true);
        _senderMock
            .Setup(s => s.SendAsync(
                "anna@example.com",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ReturnsAsync(false);

        var result = await CreateSut().SendAsync(request);

        result.Ok.Should().BeTrue();
        result.ConfirmationSent.Should().BeFalse();
        result.ConfirmationError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SendAsync_WhenCafeEmailFails_ThrowsDeliveryException()
    {
        var request = CreateValidRequest();
        _senderMock
            .Setup(s => s.SendAsync(
                "cafe@maisoncaillard.com",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ReturnsAsync(false);

        var act = () => CreateSut().SendAsync(request);

        await act.Should().ThrowAsync<OrderRequestDeliveryException>();
        _senderMock.Verify(
            s => s.SendAsync(
                "anna@example.com",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task SendAsync_WhenResendDisabled_ThrowsConfigurationException()
    {
        _resendOptions.Enabled = false;
        var act = () => CreateSut().SendAsync(CreateValidRequest());

        await act.Should().ThrowAsync<OrderRequestConfigurationException>();
        _senderMock.Verify(
            s => s.SendAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task SendAsync_WhenToEmailMissing_ThrowsConfigurationException()
    {
        _orderRequestOptions.ToEmail = "";
        var act = () => CreateSut().SendAsync(CreateValidRequest());

        await act.Should().ThrowAsync<OrderRequestConfigurationException>();
    }

    [Theory]
    [InlineData("", "anna@example.com", "0701234567", "2026-08-01", "Mölnedal")]
    [InlineData("A", "anna@example.com", "0701234567", "2026-08-01", "Mölnedal")]
    [InlineData("Anna Andersson", "not-an-email", "0701234567", "2026-08-01", "Mölnedal")]
    [InlineData("Anna Andersson", "anna@example.com", "123", "2026-08-01", "Mölnedal")]
    [InlineData("Anna Andersson", "anna@example.com", "0701234567", "", "Mölnedal")]
    [InlineData("Anna Andersson", "anna@example.com", "0701234567", "2026-08-01", "")]
    public async Task SendAsync_WhenInvalid_ThrowsArgumentException(
        string name,
        string email,
        string phone,
        string pickupDate,
        string pickupLocation)
    {
        var request = CreateValidRequest();
        request.CustomerName = name;
        request.CustomerEmail = email;
        request.CustomerPhone = phone;
        request.PickupDate = pickupDate;
        request.PickupLocation = pickupLocation;

        var act = () => CreateSut().SendAsync(request);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Missing order request details.");
    }

    [Fact]
    public async Task SendAsync_WhenItemsEmpty_ThrowsArgumentException()
    {
        var request = CreateValidRequest();
        request.Items = [];

        var act = () => CreateSut().SendAsync(request);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Missing order request details.");
    }

    private static CreateOrderRequestMailDto CreateValidRequest() =>
        new()
        {
            Items =
            [
                new OrderRequestItemDto { Wish = "Produktnamn", Size = "Storlek - 120 kr" }
            ],
            CustomerName = "Anna Andersson",
            CustomerEmail = "anna@example.com",
            CustomerPhone = "070-123 45 67",
            PickupDate = "2026-08-01",
            PickupLocation = "Mölnedal",
            Message = "Valfri text"
        };
}
