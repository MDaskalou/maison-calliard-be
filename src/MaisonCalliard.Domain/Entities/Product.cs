using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Domain.ValueObjects;

namespace MaisonCalliard.Domain.Entities;

public sealed class Product
{
    public Guid Id { get; set; }
    public LocalizedText Name { get; set; } = new();
    public LocalizedText Description { get; set; } = new();
    public ProductCategory Category { get; set; }
    public CakeStyle? Style { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string ImagePosition { get; set; } = "50% 50%";
    public bool IsAvailable { get; set; } = true;
    public bool IsVegan { get; set; }
    public bool IsSeason { get; set; }
    public bool? BakedOnSite { get; set; }
    public bool? BakedThisMorning { get; set; }
    public Dictionary<string, int>? Stock { get; set; }
    public LocalizedText Ingredients { get; set; } = new();
    public List<string> Allergies { get; set; } = [];
    public List<PriceOption> PriceOptions { get; set; } = [];
    public double? TaxRate { get; set; }
}
