using System.Net.Mail;
using System.Text.RegularExpressions;
using MaisonCalliard.Application.OrderRequests;
using MaisonCalliard.Application.OrderRequests.Dtos;
using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed class OrderRequestService : IOrderRequestService
{
    private static readonly Regex NonDigitRegex = new(@"\D", RegexOptions.Compiled);

    private readonly IOrderReceiptSender _sender;
    private readonly OrderRequestOptions _options;
    private readonly ResendOptions _resendOptions;
    private readonly ILogger<OrderRequestService> _logger;

    public OrderRequestService(
        IOrderReceiptSender sender,
        IOptions<OrderRequestOptions> options,
        IOptions<ResendOptions> resendOptions,
        ILogger<OrderRequestService> logger)
    {
        _sender = sender;
        _options = options.Value;
        _resendOptions = resendOptions.Value;
        _logger = logger;
    }

    public async Task<OrderRequestMailResultDto> SendAsync(
        CreateOrderRequestMailDto request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);

        if (!_resendOptions.Enabled
            || string.IsNullOrWhiteSpace(_resendOptions.ApiKey)
            || string.IsNullOrWhiteSpace(_options.ToEmail)
            || string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            throw new OrderRequestConfigurationException(
                "Order request email is not configured (Resend and ORDER_REQUEST_TO/FROM_EMAIL).");
        }

        var fromEmail = _options.FromEmail.Trim();
        var cafeHtml = OrderRequestEmailRenderer.RenderCafeHtml(request);

        bool cafeSent;
        try
        {
            cafeSent = await _sender.SendAsync(
                _options.ToEmail.Trim(),
                OrderRequestEmailRenderer.CafeSubject,
                cafeHtml,
                cancellationToken,
                fromEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cafe order-request email failed for {Email}.", request.CustomerEmail);
            throw new OrderRequestDeliveryException("Cafe order-request email provider failed.", ex);
        }

        if (!cafeSent)
        {
            throw new OrderRequestDeliveryException("Cafe order-request email provider failed.");
        }

        var customerHtml = OrderRequestEmailRenderer.RenderCustomerHtml(request);
        try
        {
            var confirmationSent = await _sender.SendAsync(
                request.CustomerEmail.Trim(),
                OrderRequestEmailRenderer.CustomerSubject,
                customerHtml,
                cancellationToken,
                fromEmail);

            if (confirmationSent)
            {
                return new OrderRequestMailResultDto
                {
                    Ok = true,
                    ConfirmationSent = true
                };
            }

            _logger.LogWarning(
                "Customer confirmation email was not accepted for order-request to {Email}.",
                request.CustomerEmail);

            return new OrderRequestMailResultDto
            {
                Ok = true,
                ConfirmationSent = false,
                ConfirmationError = "Confirmation email was not accepted by the provider."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Customer confirmation email failed for {Email}.", request.CustomerEmail);
            return new OrderRequestMailResultDto
            {
                Ok = true,
                ConfirmationSent = false,
                ConfirmationError = ex.Message
            };
        }
    }

    private static void Validate(CreateOrderRequestMailDto request)
    {
        if (request.Items is null || request.Items.Count == 0
            || request.Items.Any(item =>
                string.IsNullOrWhiteSpace(item.Wish) || string.IsNullOrWhiteSpace(item.Size)))
        {
            throw new ArgumentException("Missing order request details.");
        }

        var customerName = request.CustomerName?.Trim() ?? string.Empty;
        if (customerName.Length < 2)
        {
            throw new ArgumentException("Missing order request details.");
        }

        request.CustomerName = customerName;

        var customerEmail = request.CustomerEmail?.Trim() ?? string.Empty;
        if (!IsValidEmail(customerEmail))
        {
            throw new ArgumentException("Missing order request details.");
        }

        request.CustomerEmail = customerEmail;

        var digits = NonDigitRegex.Replace(request.CustomerPhone ?? string.Empty, string.Empty);
        if (digits.Length < 8)
        {
            throw new ArgumentException("Missing order request details.");
        }

        if (string.IsNullOrWhiteSpace(request.PickupDate) || string.IsNullOrWhiteSpace(request.PickupLocation))
        {
            throw new ArgumentException("Missing order request details.");
        }

        request.PickupDate = request.PickupDate.Trim();
        request.PickupLocation = request.PickupLocation.Trim();
        request.Message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim();
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var address = new MailAddress(email);
            return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
