using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Domain.ValueObjects;

namespace MaisonCalliard.Domain.Entities;

public sealed class MenuItem
{
    public Guid Id { get; set; }
    public LocalizedText Name { get; set; } = new();
    public LocalizedText Description { get; set; } = new();
    public MenuCategory Category { get; set; }
    public decimal Price { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public LocalizedText Ingredients { get; set; } = new();
    public List<string> Allergies { get; set; } = [];
    public bool IsAvailable { get; set; } = true;
    public bool? BakedOnSite { get; set; }
    public bool? BakedThisMorning { get; set; }
    public double? TaxRate { get; set; }
    public int SortOrder { get; set; }
}
