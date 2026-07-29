using MaisonCalliard.Domain.Enums;

namespace MaisonCalliard.Application.Products.Dtos;

public sealed class CreateProductRequest
{
    public string NameSe { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? DescriptionSe { get; set; }
    public string? DescriptionEn { get; set; }
    public ProductCategory Category { get; set; }
    public CakeStyle? Style { get; set; }
    public bool IsAvailable { get; set; } = true;
    public bool IsVegan { get; set; }
    public bool IsSeason { get; set; }
    public bool? BakedOnSite { get; set; }
    public bool? BakedThisMorning { get; set; }
    public Dictionary<string, int>? Stock { get; set; }
    public string IngredientsSe { get; set; } = string.Empty;
    public string IngredientsEn { get; set; } = string.Empty;
    public List<string> Allergies { get; set; } = [];
    public List<PriceOptionDto> PriceOptions { get; set; } = [];
    public double? TaxRate { get; set; }
}
