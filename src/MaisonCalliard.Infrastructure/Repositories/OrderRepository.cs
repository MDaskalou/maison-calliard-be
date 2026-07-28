using MaisonCalliard.Application.Orders;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaisonCalliard.Infrastructure.Repositories;

internal sealed class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _context;

    public OrderRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Orders.Include(o => o.Items).ToListAsync(cancellationToken);
    }

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<Order?> GetByStripeSessionIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.StripeSessionId == sessionId, cancellationToken);
    }

    public async Task<Order?> GetByStripePaymentIntentIdAsync(string paymentIntentId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.StripePaymentIntentId == paymentIntentId, cancellationToken);
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveOrderUpdateAsync(Order order, IReadOnlyList<CartItem> items, CancellationToken cancellationToken = default)
    {
        if (_context.Entry(order).State == EntityState.Detached)
        {
            throw new InvalidOperationException("Order must be tracked before it can be updated.");
        }

        SyncOrderItems(order, items);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new OrderConcurrencyException(
                "Ordern har ändrats eller tagits bort av en annan begäran. Ladda om och försök igen.",
                ex);
        }
    }

    private void SyncOrderItems(Order order, IReadOnlyList<CartItem> items)
    {
        var existingByCartId = order.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.CartId))
            .GroupBy(i => i.CartId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var keptCartIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var incoming in items)
        {
            if (!string.IsNullOrWhiteSpace(incoming.CartId)
                && existingByCartId.TryGetValue(incoming.CartId, out var existing))
            {
                existing.ProductId = incoming.ProductId;
                existing.Name = incoming.Name;
                existing.ImageUrl = incoming.ImageUrl;
                existing.OptionLabel = incoming.OptionLabel;
                existing.Price = incoming.Price;
                existing.Quantity = incoming.Quantity;
                existing.TaxRate = incoming.TaxRate;
                existing.IsPaid = incoming.IsPaid;
                keptCartIds.Add(existing.CartId);
                continue;
            }

            // Always create a new tracked instance for inserts. Reusing detached
            // payload entities (or calling Update on them) can mark them Modified
            // and trigger DbUpdateConcurrencyException on SaveChanges.
            var newItem = new CartItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                CartId = string.IsNullOrWhiteSpace(incoming.CartId)
                    ? Guid.NewGuid().ToString("N")
                    : incoming.CartId.Trim(),
                ProductId = incoming.ProductId,
                Name = incoming.Name,
                ImageUrl = incoming.ImageUrl,
                OptionLabel = incoming.OptionLabel,
                Price = incoming.Price,
                Quantity = incoming.Quantity,
                TaxRate = incoming.TaxRate,
                IsPaid = incoming.IsPaid
            };

            // DbSet.Add guarantees Insert; navigation Add alone can leave client-keyed
            // entities as Modified when defaults/store config confuse the tracker.
            _context.CartItems.Add(newItem);
            if (!order.Items.Contains(newItem))
            {
                order.Items.Add(newItem);
            }

            keptCartIds.Add(newItem.CartId);
        }

        var toRemove = order.Items
            .Where(i => !keptCartIds.Contains(i.CartId))
            .ToList();

        foreach (var item in toRemove)
        {
            order.Items.Remove(item);
            _context.CartItems.Remove(item);
        }
    }

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        if (_context.Entry(order).State == EntityState.Detached)
        {
            _context.Orders.Update(order);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAsPaidAsync(
        Order order,
        DateTime paidAt,
        string paymentMethod,
        string? stripePaymentIntentId,
        string? stripeSessionId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

        order.Status = OrderStatus.Paid;
        order.PaidAt ??= paidAt;
        order.PaymentMethod = string.IsNullOrWhiteSpace(order.PaymentMethod) ? paymentMethod : order.PaymentMethod;

        if (!string.IsNullOrWhiteSpace(stripePaymentIntentId))
        {
            order.StripePaymentIntentId = stripePaymentIntentId;
        }

        if (!string.IsNullOrWhiteSpace(stripeSessionId))
        {
            order.StripeSessionId = stripeSessionId;
        }

        if (string.IsNullOrWhiteSpace(order.ReceiptNumber))
        {
            var year = order.PaidAt.Value.Year;
            var sequence = await _context.ReceiptSequences.FirstOrDefaultAsync(s => s.Year == year, cancellationToken);
            if (sequence is null)
            {
                sequence = new ReceiptSequence { Year = year };
                _context.ReceiptSequences.Add(sequence);
            }

            sequence.LastNumber += 1;
            order.ReceiptNumber = $"MC-{year}-{sequence.LastNumber:000000}";
        }

        _context.Orders.Update(order);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Order order, CancellationToken cancellationToken = default)
    {
        _context.Orders.Remove(order);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
