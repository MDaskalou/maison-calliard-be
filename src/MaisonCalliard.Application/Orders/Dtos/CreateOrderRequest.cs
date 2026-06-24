namespace MaisonCalliard.Application.Orders.Dtos;

public sealed class CreateOrderRequest
{
    public List<CartItemDto> Items { get; set; } = [];
    public decimal Total { get; set; }
    public decimal TaxAmount { get; set; }
    public DateTime PickupDateTime { get; set; }
    public string Location { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Message { get; set; }
}
