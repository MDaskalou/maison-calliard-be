namespace MaisonCalliard.Application.Menu.Dtos;

public sealed class ReorderMenuItemsRequest
{
    public List<Guid> OrderedIds { get; set; } = [];
}
