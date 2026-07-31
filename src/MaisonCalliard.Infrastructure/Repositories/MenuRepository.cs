using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaisonCalliard.Infrastructure.Repositories;

internal sealed class MenuRepository : IMenuRepository
{
    private readonly AppDbContext _context;

    public MenuRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<MenuItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.MenuItems
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<MenuItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.MenuItems.FindAsync([id], cancellationToken);
    }

    public async Task<IReadOnlyList<MenuItem>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        return await _context.MenuItems
            .Where(m => idList.Contains(m.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<int?> GetMaxSortOrderAsync(CancellationToken cancellationToken = default)
    {
        if (!await _context.MenuItems.AnyAsync(cancellationToken))
        {
            return null;
        }

        return await _context.MenuItems.MaxAsync(m => m.SortOrder, cancellationToken);
    }

    public async Task AddAsync(MenuItem menuItem, CancellationToken cancellationToken = default)
    {
        _context.MenuItems.Add(menuItem);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(MenuItem menuItem, CancellationToken cancellationToken = default)
    {
        _context.MenuItems.Update(menuItem);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateRangeAsync(IEnumerable<MenuItem> menuItems, CancellationToken cancellationToken = default)
    {
        _context.MenuItems.UpdateRange(menuItems);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(MenuItem menuItem, CancellationToken cancellationToken = default)
    {
        _context.MenuItems.Remove(menuItem);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
