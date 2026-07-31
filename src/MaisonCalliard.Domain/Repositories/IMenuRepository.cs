using MaisonCalliard.Domain.Entities;

namespace MaisonCalliard.Domain.Repositories;

public interface IMenuRepository
{
    Task<IReadOnlyList<MenuItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<MenuItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MenuItem>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<int?> GetMaxSortOrderAsync(CancellationToken cancellationToken = default);
    Task AddAsync(MenuItem menuItem, CancellationToken cancellationToken = default);
    Task UpdateAsync(MenuItem menuItem, CancellationToken cancellationToken = default);
    Task UpdateRangeAsync(IEnumerable<MenuItem> menuItems, CancellationToken cancellationToken = default);
    Task DeleteAsync(MenuItem menuItem, CancellationToken cancellationToken = default);
}
