using MaisonCalliard.Domain.Enums;

namespace MaisonCalliard.Application.Products.Dtos;

public sealed class ProductDto
{
    public Guid Id { get; set; }
    public LocalizedTextDto Name { get; set; } = new();
    public LocalizedTextDto Description { get; set; } = new();
    public ProductCategory Category { get; set; }
    public CakeStyle? Style { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string ImagePosition { get; set; } = "50% 50%";
    public bool IsAvailable { get; set; }
    public bool IsVegan { get; set; }
    public bool IsSeason { get; set; }
    public bool? BakedOnSite { get; set; }
    public bool? BakedThisMorning { get; set; }
    public Dictionary<string, int>? Stock { get; set; }
    public LocalizedTextDto Ingredients { get; set; } = new();
    public List<string> Allergies { get; set; } = [];
    public List<PriceOptionDto> PriceOptions { get; set; } = [];
    public double? TaxRate { get; set; }
}

public sealed class LocalizedTextDto
{
    public string Se { get; set; } = string.Empty;
    public string En { get; set; } = string.Empty;
}

public sealed class PriceOptionDto
{
    public string Label { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
