namespace MaisonCalliard.Application.OrderRequests.Dtos;

public sealed class CreateOrderRequestMailDto
{
    public List<OrderRequestItemDto> Items { get; set; } = [];
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string PickupDate { get; set; } = string.Empty;
    public string PickupLocation { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public sealed class OrderRequestItemDto
{
    public string Wish { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
}

public sealed class OrderRequestMailResultDto
{
    public bool Ok { get; set; }
    public bool ConfirmationSent { get; set; }
    public string? ConfirmationError { get; set; }
}
