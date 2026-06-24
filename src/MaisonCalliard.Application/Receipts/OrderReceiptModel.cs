namespace MaisonCalliard.Application.Receipts;

public sealed class OrderReceiptModel
{
    public Guid OrderId { get; init; }
    public string ShortOrderId { get; init; } = string.Empty;
    public string? ReceiptNumber { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerAddress { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Message { get; init; }
    public string Location { get; init; } = string.Empty;
    public string PickupDate { get; init; } = string.Empty;
    public string PickupTime { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public decimal TaxAmount { get; init; }
    public string? PaymentMethod { get; init; }
    public IReadOnlyList<OrderReceiptLineModel> Lines { get; init; } = [];
}

public sealed class OrderReceiptLineModel
{
    public string Name { get; init; } = string.Empty;
    public string OptionLabel { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
}
