using MaisonCalliard.Domain.Enums;

namespace MaisonCalliard.Application.Orders.Dtos;

public sealed class UpdateOrderRequest
{
    public List<CartItemDto> Items { get; set; } = [];
    public DateTime PickupDateTime { get; set; }
    public string Location { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Message { get; set; }
    public OrderStatus Status { get; set; }
}
