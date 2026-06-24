namespace MaisonCalliard.Domain.Entities;

public sealed class CartItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string CartId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string OptionLabel { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public decimal TaxRate { get; set; }
}
