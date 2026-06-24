using MaisonCalliard.Domain.Enums;

namespace MaisonCalliard.Application.Orders.Dtos;

public sealed class OrderDto
{
    public Guid Id { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? StripeSessionId { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public List<CartItemDto> Items { get; set; } = [];
    public decimal Total { get; set; }
    public decimal TaxAmount { get; set; }
    public List<VatBreakdownDto> VatBreakdown { get; set; } = [];
    public DateTime PickupDateTime { get; set; }
    public string Location { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Message { get; set; }
    public OrderStatus Status { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTime? PaidAt { get; set; }
    public bool IsPrinted { get; set; }
    public SellerDto Seller { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public sealed class CartItemDto
{
    public string CartId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string OptionLabel { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public int Quantity { get; set; }
    public decimal TaxRate { get; set; }
}

public sealed class VatBreakdownDto
{
    public decimal TaxRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrossAmount { get; set; }
}

public sealed class SellerDto
{
    public string CompanyName { get; set; } = "Maison Caillard AB";
    public string OrganizationNumber { get; set; } = "559999-9999";
    public string VatNumber { get; set; } = "SE559999999901";
    public string Address { get; set; } = "Exempelgatan 1, 431 00 Molndal";
    public string Email { get; set; } = "info@maisoncaillard.se";
}
