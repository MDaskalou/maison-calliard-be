using MaisonCalliard.Domain.Enums;

namespace MaisonCalliard.Application.Menu.Dtos;

public sealed class UpdateMenuItemRequest
{
    public string NameSe { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public MenuCategory Category { get; set; }
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; } = true;
    public bool? BakedOnSite { get; set; }
    public bool? BakedThisMorning { get; set; }
    public double? TaxRate { get; set; }
    public int? SortOrder { get; set; }
}
