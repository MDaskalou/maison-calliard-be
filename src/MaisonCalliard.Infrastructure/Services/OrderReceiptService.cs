using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed class OrderReceiptService : IOrderReceiptService
{
    private readonly IOrderRepository _orderRepository;
    private readonly ResendOrderReceiptSender _sender;
    private readonly ReceiptOptions _receiptOptions;
    private readonly ILogger<OrderReceiptService> _logger;

    public OrderReceiptService(
        IOrderRepository orderRepository,
        ResendOrderReceiptSender sender,
        IOptions<ReceiptOptions> receiptOptions,
        ILogger<OrderReceiptService> logger)
    {
        _orderRepository = orderRepository;
        _sender = sender;
        _receiptOptions = receiptOptions.Value;
        _logger = logger;
    }

    public async Task TrySendReceiptAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} not found for receipt.", orderId);
            return;
        }

        var customerEmailSentAt = order.CustomerEmailSentAt ?? order.ReceiptSentAt;
        if (order.CustomerEmailSentAt is null && order.ReceiptSentAt is not null)
        {
            order.CustomerEmailSentAt = order.ReceiptSentAt;
        }

        if (order.Status is not (OrderStatus.Pending or OrderStatus.Completed or OrderStatus.Paid))
        {
            _logger.LogDebug(
                "Order {OrderId} status {Status} not eligible for receipt.",
                orderId,
                order.Status);
            return;
        }

        if (customerEmailSentAt is null && string.IsNullOrWhiteSpace(order.Email))
        {
            _logger.LogWarning("Order {OrderId} has no email; receipt skipped.", orderId);
        }
        else if (customerEmailSentAt is null)
        {
            var model = MapToModel(order);
            var subject = OrderReceiptEmailRenderer.RenderSubject(model);
            var html = OrderReceiptEmailRenderer.RenderHtml(model, _receiptOptions);

            var sent = await _sender.SendAsync(order.Email, subject, html, cancellationToken);
            if (sent)
            {
                var sentAt = DateTime.UtcNow;
                order.ReceiptSentAt = sentAt;
                order.CustomerEmailSentAt = sentAt;
                _logger.LogInformation("Order receipt sent for {OrderId} to {Email}.", orderId, order.Email);
            }
        }

        if (order.InternalNotificationSentAt is null)
        {
            await TrySendInternalNotificationAsync(order, cancellationToken);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
    }

    private async Task TrySendInternalNotificationAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.PaidAt is null || string.IsNullOrWhiteSpace(order.PaymentMethod))
        {
            _logger.LogDebug(
                "Order {OrderId} has no completed payment data; internal order notification skipped.",
                order.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(_receiptOptions.OrderNotificationEmail))
        {
            _logger.LogWarning(
                "ORDER_NOTIFICATION_EMAIL or Receipt:OrderNotificationEmail is not configured; cafe order notification skipped for {OrderId}.",
                order.Id);
            return;
        }

        var model = MapToModel(order);
        var subject = InternalOrderNotificationEmailRenderer.RenderSubject(model);
        var html = InternalOrderNotificationEmailRenderer.RenderHtml(model);

        var sent = await _sender.SendAsync(_receiptOptions.OrderNotificationEmail, subject, html, cancellationToken);
        if (!sent)
        {
            _logger.LogWarning(
                "Cafe order notification was not sent for {OrderId} to {Email}.",
                order.Id,
                _receiptOptions.OrderNotificationEmail);
            return;
        }

        order.InternalNotificationSentAt = DateTime.UtcNow;
        _logger.LogInformation(
            "Cafe order notification sent for {OrderId} to {Email}.",
            order.Id,
            _receiptOptions.OrderNotificationEmail);
    }

    private static OrderReceiptModel MapToModel(Order order)
    {
        var pickupLocal = order.PickupDateTime.ToLocalTime();
        return new OrderReceiptModel
        {
            OrderId = order.Id,
            ShortOrderId = order.Id.ToString("N")[..8].ToUpperInvariant(),
            ReceiptNumber = order.ReceiptNumber,
            CustomerName = order.CustomerName,
            CustomerAddress = order.CustomerAddress,
            CustomerEmail = order.Email,
            Phone = order.Phone,
            Message = order.Message,
            Location = order.Location,
            PickupDate = pickupLocal.ToString("yyyy-MM-dd"),
            PickupTime = pickupLocal.ToString("HH:mm"),
            Total = order.Total,
            TaxAmount = order.TaxAmount,
            PaymentMethod = order.PaymentMethod,
            Lines = order.Items.Select(i => new OrderReceiptLineModel
            {
                Name = i.Name,
                OptionLabel = i.OptionLabel,
                Price = i.Price,
                Quantity = i.Quantity
            }).ToList()
        };
    }
}
