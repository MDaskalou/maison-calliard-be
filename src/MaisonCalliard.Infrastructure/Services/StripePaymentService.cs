using MaisonCalliard.Application.Payments;
using MaisonCalliard.Application.Payments.Dtos;
using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Checkout;
using DomainOrder = MaisonCalliard.Domain.Entities.Order;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed class StripePaymentService : IPaymentService
{
    private readonly string _stripeSecretKey;
    private readonly string _webhookSecret;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderReceiptService _orderReceiptService;

    public StripePaymentService(
        IConfiguration configuration,
        IOrderRepository orderRepository,
        IOrderReceiptService orderReceiptService)
    {
        _stripeSecretKey = configuration["Stripe:SecretKey"] ?? string.Empty;
        _webhookSecret = configuration["Stripe:WebhookSecret"] ?? string.Empty;
        _orderRepository = orderRepository;
        _orderReceiptService = orderReceiptService;
    }

    public async Task<CreatePaymentSessionResponse> CreateSessionAsync(
        CreatePaymentSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCreateSessionRequest(request);
        EnsureStripeSecretKeyConfigured();

        var lineItems = request.LineItems.Select(item => new SessionLineItemOptions
        {
            PriceData = new SessionLineItemPriceDataOptions
            {
                Currency = "sek",
                UnitAmount = item.UnitAmountOre,
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
            Metadata = CreateCheckoutMetadata(request)
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options, cancellationToken: cancellationToken);

        if (request.OrderId.HasValue)
        {
            var order = await _orderRepository.GetByIdAsync(request.OrderId.Value, cancellationToken);
            if (order is not null)
            {
                order.StripeSessionId = session.Id;
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }
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

        var amountOre = (long)Math.Round(order.Total * 100m, MidpointRounding.AwayFromZero);
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
        var intent = await service.CreateAsync(options, cancellationToken: cancellationToken);

        order.StripeSessionId = intent.Id;
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return new CreatePaymentIntentResponse
        {
            ClientSecret = intent.ClientSecret,
            PaymentIntentId = intent.Id
        };
    }

    public async Task HandleWebhookAsync(string payload, string stripeSignature, CancellationToken cancellationToken = default)
    {
        var stripeEvent = EventUtility.ConstructEvent(payload, stripeSignature, _webhookSecret);

        if (stripeEvent.Type == EventTypes.CheckoutSessionCompleted)
        {
            var session = (Session)stripeEvent.Data.Object;
            var order = await ResolveOrderForCheckoutSessionAsync(session, cancellationToken);
            if (order is not null)
            {
                await ActivateOrderAfterPaymentAsync(order, cancellationToken);
            }

            return;
        }

        if (stripeEvent.Type == EventTypes.PaymentIntentSucceeded)
        {
            var paymentIntent = (PaymentIntent)stripeEvent.Data.Object;
            var order = await ResolveOrderForPaymentIntentAsync(paymentIntent, cancellationToken);
            if (order is not null)
            {
                await ActivateOrderAfterPaymentAsync(order, cancellationToken);
            }
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
            order = await _orderRepository.GetByStripeSessionIdAsync(request.PaymentIntentId, cancellationToken);
        }

        if (order is null)
        {
            throw new InvalidOperationException("Order was not found for payment confirmation.");
        }

        if (order.Status is OrderStatus.Pending or OrderStatus.Completed or OrderStatus.Paid)
        {
            await _orderReceiptService.TrySendReceiptAsync(order.Id, cancellationToken);
            return order.Id;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {order.Id} cannot be confirmed (status: {order.Status}).");
        }

        var paymentIntentId = !string.IsNullOrWhiteSpace(request.PaymentIntentId)
            ? request.PaymentIntentId
            : order.StripeSessionId;

        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            throw new InvalidOperationException($"Order {order.Id} has no payment intent.");
        }

        var intentService = new PaymentIntentService();
        var intent = await intentService.GetAsync(paymentIntentId, cancellationToken: cancellationToken);

        if (!string.Equals(intent.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Payment has not succeeded yet.");
        }

        await ActivateOrderAfterPaymentAsync(order, cancellationToken);
        return order.Id;
    }

    private async Task ActivateOrderAfterPaymentAsync(DomainOrder order, CancellationToken cancellationToken)
    {
        if (order.Status == OrderStatus.AwaitingPayment)
        {
            order.Status = OrderStatus.Pending;
            await _orderRepository.UpdateAsync(order, cancellationToken);
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

        return await _orderRepository.GetByStripeSessionIdAsync(paymentIntent.Id, cancellationToken);
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

        if (request.LineItems.Count == 0)
        {
            throw new ArgumentException("At least one line item is required.");
        }

        foreach (var item in request.LineItems)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                throw new ArgumentException("Line item name is required.");
            }

            if (item.UnitAmountOre < 1)
            {
                throw new ArgumentException("Line item unit amount must be greater than zero.");
            }

            if (item.Quantity < 1)
            {
                throw new ArgumentException("Line item quantity must be greater than zero.");
            }
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
        if (string.IsNullOrWhiteSpace(_stripeSecretKey))
        {
            throw new InvalidOperationException("Stripe:SecretKey is not configured.");
        }
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
}
