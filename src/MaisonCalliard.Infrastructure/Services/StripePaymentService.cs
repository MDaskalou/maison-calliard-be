using MaisonCalliard.Application.Payments;
using MaisonCalliard.Application.Payments.Dtos;
using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;
using DomainOrder = MaisonCalliard.Domain.Entities.Order;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed class StripePaymentService : IPaymentService
{
    private readonly string _stripeSecretKey;
    private readonly string _webhookSecret;
    private readonly IReadOnlyDictionary<string, StripeLocationSettings> _locationSettings;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderReceiptService _orderReceiptService;
    private readonly ILogger<StripePaymentService> _logger;

    public StripePaymentService(
        IConfiguration configuration,
        IOrderRepository orderRepository,
        IOrderReceiptService orderReceiptService,
        ILogger<StripePaymentService> logger)
    {
        _stripeSecretKey = configuration["Stripe:SecretKey"] ?? string.Empty;
        _webhookSecret = configuration["Stripe:WebhookSecret"] ?? string.Empty;
        _locationSettings = LoadLocationSettings(configuration);
        _orderRepository = orderRepository;
        _orderReceiptService = orderReceiptService;
        _logger = logger;
    }

    public async Task<CreatePaymentSessionResponse> CreateSessionAsync(
        CreatePaymentSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCreateSessionRequest(request);
        EnsureStripeSecretKeyConfigured();

        var order = await _orderRepository.GetByIdAsync(request.OrderId!.Value, cancellationToken)
            ?? throw new ArgumentException($"Order {request.OrderId.Value} was not found.");

        if (order.Status == OrderStatus.Paid)
        {
            throw new InvalidOperationException($"Order {order.Id} is already paid.");
        }

        if (!string.IsNullOrWhiteSpace(order.StripeSessionId))
        {
            var existingSession = await TryGetOpenCheckoutSessionAsync(order, cancellationToken);
            if (existingSession is not null)
            {
                return new CreatePaymentSessionResponse
                {
                    SessionId = existingSession.Id,
                    Url = existingSession.Url
                };
            }
        }

        var lineItems = order.Items.Select(item => new SessionLineItemOptions
        {
            PriceData = new SessionLineItemPriceDataOptions
            {
                Currency = "sek",
                UnitAmount = ToOre(item.Price),
                ProductData = new SessionLineItemPriceDataProductDataOptions
                {
                    Name = item.Name
                }
            },
            Quantity = item.Quantity
        }).ToList();

        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = ["card"],
            LineItems = lineItems,
            Mode = "payment",
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            CustomerEmail = request.CustomerEmail,
            ClientReferenceId = order.Id.ToString(),
            Metadata = CreateCheckoutMetadata(request)
        };

        var service = new SessionService();
        var session = await service.CreateAsync(
            options,
            requestOptions: CreateStripeRequestOptions(order),
            cancellationToken: cancellationToken);

        try
        {
            order.StripeSessionId = session.Id;
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Stripe Checkout session {SessionId} was created for order {OrderId}, but StripeSessionId could not be saved. Webhook metadata will still be used as fallback.",
                session.Id,
                order.Id);
        }

        return new CreatePaymentSessionResponse
        {
            SessionId = session.Id,
            Url = session.Url
        };
    }

    public async Task<CreatePaymentIntentResponse> CreatePaymentIntentAsync(
        CreatePaymentIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {request.OrderId} was not found.");

        if (order.Status == OrderStatus.Paid)
        {
            throw new InvalidOperationException($"Order {order.Id} is already paid.");
        }

        if (!string.IsNullOrWhiteSpace(order.StripePaymentIntentId))
        {
            var existingIntent = await TryGetReusablePaymentIntentAsync(order, cancellationToken);
            if (existingIntent is not null)
            {
                return new CreatePaymentIntentResponse
                {
                    ClientSecret = existingIntent.ClientSecret,
                    PaymentIntentId = existingIntent.Id
                };
            }
        }

        var amountOre = GetExpectedAmountOre(order);
        if (amountOre < 1)
        {
            throw new InvalidOperationException("Order total must be greater than zero.");
        }

        var options = new PaymentIntentCreateOptions
        {
            Amount = amountOre,
            Currency = "sek",
            ReceiptEmail = request.CustomerEmail,
            Metadata = new Dictionary<string, string> { ["orderId"] = order.Id.ToString() },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true }
        };

        var service = new PaymentIntentService();
        var intent = await service.CreateAsync(
            options,
            requestOptions: CreateStripeRequestOptions(order),
            cancellationToken: cancellationToken);

        order.StripePaymentIntentId = intent.Id;
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return new CreatePaymentIntentResponse
        {
            ClientSecret = intent.ClientSecret,
            PaymentIntentId = intent.Id
        };
    }

    public async Task HandleWebhookAsync(string payload, string stripeSignature, CancellationToken cancellationToken = default)
    {
        var stripeEvent = ConstructStripeEvent(payload, stripeSignature);

        if (stripeEvent.Type == EventTypes.CheckoutSessionCompleted)
        {
            var session = (Session)stripeEvent.Data.Object;
            var order = await ResolveOrderForCheckoutSessionAsync(session, cancellationToken);
            if (order is not null)
            {
                ValidateCheckoutSessionMatchesOrder(session, order);

                await ActivateOrderAfterPaymentAsync(
                    order,
                    DateTime.UtcNow,
                    "Stripe kortbetalning",
                    session.PaymentIntentId,
                    session.Id,
                    cancellationToken);
            }

            return;
        }

        if (stripeEvent.Type == EventTypes.PaymentIntentSucceeded)
        {
            var paymentIntent = (PaymentIntent)stripeEvent.Data.Object;
            var order = await ResolveOrderForPaymentIntentAsync(paymentIntent, cancellationToken);
            if (order is not null)
            {
                ValidatePaymentIntentMatchesOrder(paymentIntent, order);

                await ActivateOrderAfterPaymentAsync(
                    order,
                    DateTime.UtcNow,
                    "Stripe kortbetalning",
                    paymentIntent.Id,
                    null,
                    cancellationToken);
            }

            return;
        }

        if (stripeEvent.Type is EventTypes.PaymentIntentPaymentFailed or EventTypes.CheckoutSessionExpired)
        {
            _logger.LogInformation("Stripe event {EventType} received; no paid state change performed.", stripeEvent.Type);
        }
    }

    public async Task<Guid> ConfirmPaymentIntentAsync(
        ConfirmPaymentIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        DomainOrder? order = null;

        if (request.OrderId is Guid orderId && orderId != Guid.Empty)
        {
            order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.PaymentIntentId))
        {
            order = await _orderRepository.GetByStripePaymentIntentIdAsync(request.PaymentIntentId, cancellationToken)
                ?? await _orderRepository.GetByStripeSessionIdAsync(request.PaymentIntentId, cancellationToken);
        }

        if (order is null)
        {
            throw new InvalidOperationException("Order was not found for payment confirmation.");
        }

        if ((order.Status is OrderStatus.Pending or OrderStatus.Completed or OrderStatus.Paid)
            && !string.IsNullOrWhiteSpace(order.ReceiptNumber)
            && order.PaidAt is not null
            && !string.IsNullOrWhiteSpace(order.PaymentMethod))
        {
            await _orderReceiptService.TrySendReceiptAsync(order.Id, cancellationToken);
            return order.Id;
        }

        if (order.Status is not (OrderStatus.AwaitingPayment or OrderStatus.Pending or OrderStatus.Paid))
        {
            throw new InvalidOperationException($"Order {order.Id} cannot be confirmed (status: {order.Status}).");
        }

        if (!string.IsNullOrWhiteSpace(request.PaymentIntentId)
            && !request.PaymentIntentId.StartsWith("cs_", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(order.StripePaymentIntentId)
            && !string.Equals(request.PaymentIntentId, order.StripePaymentIntentId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Payment intent does not match the order.");
        }

        if (!string.IsNullOrWhiteSpace(request.PaymentIntentId)
            && !string.IsNullOrWhiteSpace(order.StripeSessionId)
            && request.PaymentIntentId.StartsWith("cs_", StringComparison.Ordinal)
            && !string.Equals(request.PaymentIntentId, order.StripeSessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Checkout session does not match the order.");
        }

        if (!string.IsNullOrWhiteSpace(request.PaymentIntentId)
            && request.PaymentIntentId.StartsWith("cs_", StringComparison.Ordinal))
        {
            var sessionService = new SessionService();
            var session = await sessionService.GetAsync(
                request.PaymentIntentId,
                requestOptions: CreateStripeRequestOptions(order),
                cancellationToken: cancellationToken);
            ValidateCheckoutSessionMatchesOrder(session, order);

            await ActivateOrderAfterPaymentAsync(
                order,
                DateTime.UtcNow,
                "Stripe kortbetalning",
                session.PaymentIntentId,
                session.Id,
                cancellationToken);

            return order.Id;
        }

        var paymentIntentId = !string.IsNullOrWhiteSpace(request.PaymentIntentId)
            ? request.PaymentIntentId
            : order.StripePaymentIntentId;

        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            throw new InvalidOperationException($"Order {order.Id} has no payment intent.");
        }

        var intentService = new PaymentIntentService();
        var intent = await intentService.GetAsync(
            paymentIntentId,
            requestOptions: CreateStripeRequestOptions(order),
            cancellationToken: cancellationToken);

        if (!string.Equals(intent.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Payment has not succeeded yet.");
        }

        ValidatePaymentIntentMatchesOrder(intent, order);

        await ActivateOrderAfterPaymentAsync(
            order,
            DateTime.UtcNow,
            "Stripe kortbetalning",
            intent.Id,
            order.StripeSessionId,
            cancellationToken);
        return order.Id;
    }

    private async Task ActivateOrderAfterPaymentAsync(
        DomainOrder order,
        DateTime paidAt,
        string paymentMethod,
        string? stripePaymentIntentId,
        string? stripeSessionId,
        CancellationToken cancellationToken)
    {
        if (order.Status is OrderStatus.AwaitingPayment or OrderStatus.Pending or OrderStatus.Paid)
        {
            await _orderRepository.MarkAsPaidAsync(
                order,
                paidAt,
                paymentMethod,
                stripePaymentIntentId,
                stripeSessionId,
                cancellationToken);
        }

        await _orderReceiptService.TrySendReceiptAsync(order.Id, cancellationToken);
    }

    private async Task<DomainOrder?> ResolveOrderForPaymentIntentAsync(
        PaymentIntent paymentIntent,
        CancellationToken cancellationToken)
    {
        if (paymentIntent.Metadata.TryGetValue("orderId", out var orderIdStr)
            && Guid.TryParse(orderIdStr, out var orderId))
        {
            var byId = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
            if (byId is not null)
            {
                return byId;
            }
        }

        return await _orderRepository.GetByStripePaymentIntentIdAsync(paymentIntent.Id, cancellationToken)
            ?? await _orderRepository.GetByStripeSessionIdAsync(paymentIntent.Id, cancellationToken);
    }

    private static void ValidateCreateSessionRequest(CreatePaymentSessionRequest request)
    {
        if (!request.OrderId.HasValue || request.OrderId.Value == Guid.Empty)
        {
            throw new ArgumentException("OrderId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SuccessUrl))
        {
            throw new ArgumentException("SuccessUrl is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CancelUrl))
        {
            throw new ArgumentException("CancelUrl is required.");
        }

    }

    private static Dictionary<string, string> CreateCheckoutMetadata(CreatePaymentSessionRequest request)
    {
        var metadata = new Dictionary<string, string>
        {
            ["orderId"] = request.OrderId!.Value.ToString()
        };

        if (!string.IsNullOrWhiteSpace(request.CustomerName))
        {
            metadata["customerName"] = request.CustomerName;
        }

        return metadata;
    }

    private void EnsureStripeSecretKeyConfigured()
    {
        if (string.IsNullOrWhiteSpace(_stripeSecretKey)
            && !_locationSettings.Values.Any(settings => !string.IsNullOrWhiteSpace(settings.SecretKey)))
        {
            throw new InvalidOperationException("Stripe secret key is not configured.");
        }
    }

    private Event ConstructStripeEvent(string payload, string stripeSignature)
    {
        var secrets = _locationSettings.Values
            .Select(settings => settings.WebhookSecret)
            .Append(_webhookSecret)
            .Where(secret => !string.IsNullOrWhiteSpace(secret))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (secrets.Count == 0)
        {
            throw new InvalidOperationException("Stripe webhook secret is not configured.");
        }

        StripeException? lastStripeException = null;

        foreach (var secret in secrets)
        {
            try
            {
                return EventUtility.ConstructEvent(payload, stripeSignature, secret);
            }
            catch (StripeException ex)
            {
                lastStripeException = ex;
            }
        }

        if (lastStripeException is not null)
        {
            throw lastStripeException;
        }

        throw new InvalidOperationException("Stripe webhook signature could not be verified.");
    }

    private async Task<DomainOrder?> ResolveOrderForCheckoutSessionAsync(
        Session session,
        CancellationToken cancellationToken)
    {
        var bySessionId = await _orderRepository.GetByStripeSessionIdAsync(session.Id, cancellationToken);
        if (bySessionId is not null)
        {
            return bySessionId;
        }

        if (session.Metadata.TryGetValue("orderId", out var orderIdStr)
            && Guid.TryParse(orderIdStr, out var orderId))
        {
            var byOrderId = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
            if (byOrderId is not null)
            {
                byOrderId.StripeSessionId = session.Id;
                await _orderRepository.UpdateAsync(byOrderId, cancellationToken);
                return byOrderId;
            }
        }

        return null;
    }

    private async Task<Session?> TryGetOpenCheckoutSessionAsync(DomainOrder order, CancellationToken cancellationToken)
    {
        try
        {
            var service = new SessionService();
            var session = await service.GetAsync(
                order.StripeSessionId,
                requestOptions: CreateStripeRequestOptions(order),
                cancellationToken: cancellationToken);
            if (string.Equals(session.Status, "open", StringComparison.OrdinalIgnoreCase)
                && string.Equals(session.PaymentStatus, "unpaid", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(session.Url))
            {
                ValidateCheckoutSessionAmountAndCurrency(session, order, requirePaid: false);
                return session;
            }
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Existing Stripe Checkout session {SessionId} could not be reused for order {OrderId}.", order.StripeSessionId, order.Id);
        }

        return null;
    }

    private async Task<PaymentIntent?> TryGetReusablePaymentIntentAsync(DomainOrder order, CancellationToken cancellationToken)
    {
        try
        {
            var service = new PaymentIntentService();
            var intent = await service.GetAsync(
                order.StripePaymentIntentId,
                requestOptions: CreateStripeRequestOptions(order),
                cancellationToken: cancellationToken);
            ValidatePaymentIntentMatchesOrder(intent, order, requireSucceeded: false);

            if (intent.Status is "requires_payment_method" or "requires_confirmation" or "requires_action" or "processing")
            {
                return intent;
            }
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Existing Stripe PaymentIntent {PaymentIntentId} could not be reused for order {OrderId}.", order.StripePaymentIntentId, order.Id);
        }

        return null;
    }

    private static long GetExpectedAmountOre(DomainOrder order)
    {
        return ToOre(order.Total);
    }

    private static long ToOre(decimal amount)
    {
        return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    }

    private static void ValidateCheckoutSessionMatchesOrder(Session session, DomainOrder order)
    {
        ValidateCheckoutSessionAmountAndCurrency(session, order, requirePaid: true);

        if (!string.IsNullOrWhiteSpace(order.StripeSessionId)
            && string.Equals(session.Id, order.StripeSessionId, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(session.ClientReferenceId, order.Id.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (session.Metadata.TryGetValue("orderId", out var orderId)
            && string.Equals(orderId, order.Id.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException("Checkout session is not linked to the order.");
    }

    private static void ValidateCheckoutSessionAmountAndCurrency(Session session, DomainOrder order, bool requirePaid)
    {
        if (requirePaid && !string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Checkout session is not paid.");
        }

        if (!string.Equals(session.Currency, "sek", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Checkout session currency does not match the order.");
        }

        if (session.AmountTotal != GetExpectedAmountOre(order))
        {
            throw new InvalidOperationException("Checkout session amount does not match the order.");
        }
    }

    private static void ValidatePaymentIntentMatchesOrder(PaymentIntent intent, DomainOrder order, bool requireSucceeded = true)
    {
        if (requireSucceeded && !string.Equals(intent.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Payment has not succeeded yet.");
        }

        if (!string.Equals(intent.Currency, "sek", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Payment currency does not match the order.");
        }

        if (intent.Amount != GetExpectedAmountOre(order))
        {
            throw new InvalidOperationException("Payment amount does not match the order.");
        }

        if (!string.IsNullOrWhiteSpace(order.StripePaymentIntentId)
            && string.Equals(intent.Id, order.StripePaymentIntentId, StringComparison.Ordinal))
        {
            return;
        }

        if (intent.Metadata.TryGetValue("orderId", out var orderId)
            && string.Equals(orderId, order.Id.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException("Payment intent is not linked to the order.");
    }

    private RequestOptions CreateStripeRequestOptions(DomainOrder order)
    {
        var secretKey = ResolveStripeSecretKey(order.Location);
        return new RequestOptions { ApiKey = secretKey };
    }

    private string ResolveStripeSecretKey(string location)
    {
        var locationKey = GetLocationKey(location);
        if (_locationSettings.TryGetValue(locationKey, out var settings)
            && !string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            return settings.SecretKey;
        }

        if (!string.IsNullOrWhiteSpace(_stripeSecretKey))
        {
            return _stripeSecretKey;
        }

        throw new InvalidOperationException($"Stripe secret key is not configured for {locationKey}.");
    }

    private static IReadOnlyDictionary<string, StripeLocationSettings> LoadLocationSettings(IConfiguration configuration)
    {
        return configuration.GetSection("Stripe:Locations")
            .GetChildren()
            .ToDictionary(
                section => section.Key,
                section => new StripeLocationSettings(
                    section["SecretKey"] ?? string.Empty,
                    section["WebhookSecret"] ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string GetLocationKey(string location)
    {
        var normalized = location
            .Replace("ä", "a", StringComparison.OrdinalIgnoreCase)
            .Replace("ö", "o", StringComparison.OrdinalIgnoreCase);

        return normalized.Contains("jarntorget", StringComparison.OrdinalIgnoreCase)
            ? "Jarntorget"
            : "Molndal";
    }

    private sealed record StripeLocationSettings(string SecretKey, string WebhookSecret);
}
