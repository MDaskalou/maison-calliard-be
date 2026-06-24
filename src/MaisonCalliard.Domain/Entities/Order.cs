using MaisonCalliard.Domain.Enums;

namespace MaisonCalliard.Domain.Entities;

public sealed class Order
{
    public Guid Id { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? StripeSessionId { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public List<CartItem> Items { get; set; } = [];
    public decimal Total { get; set; }
    public decimal TaxAmount { get; set; }
    public DateTime PickupDateTime { get; set; }
    public string Location { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Message { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.AwaitingPayment;
    public string? PaymentMethod { get; set; }
    public DateTime? PaidAt { get; set; }
    public bool IsPrinted { get; set; }
    public DateTime? ReceiptSentAt { get; set; }
    public DateTime? CustomerEmailSentAt { get; set; }
    public DateTime? InternalNotificationSentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
