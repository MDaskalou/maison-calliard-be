using MaisonCalliard.Domain.Enums;

namespace MaisonCalliard.Application.Menu.Dtos;

public sealed class MenuItemDto
{
    public Guid Id { get; set; }
    public LocalizedTextDto Name { get; set; } = new();
    public LocalizedTextDto Description { get; set; } = new();
    public MenuCategory Category { get; set; }
    public decimal Price { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public LocalizedTextDto Ingredients { get; set; } = new();
    public List<string> Allergies { get; set; } = [];
    public bool IsAvailable { get; set; }
    public bool? BakedOnSite { get; set; }
    public bool? BakedThisMorning { get; set; }
    public double? TaxRate { get; set; }
    public int SortOrder { get; set; }
}

public sealed class LocalizedTextDto
{
    public string Se { get; set; } = string.Empty;
    public string En { get; set; } = string.Empty;
}
