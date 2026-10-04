namespace MaisonCalliard.Application.Orders.Dtos;

public sealed class UpdateCheckoutRequest
{
    public DateTime PickupDateTime { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Message { get; set; }
}
