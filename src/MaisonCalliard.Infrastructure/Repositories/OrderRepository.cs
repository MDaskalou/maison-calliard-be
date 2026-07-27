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

        if (order.Items.Count > 0)
        {
            _context.CartItems.RemoveRange(order.Items.ToList());
            order.Items.Clear();
        }

        foreach (var item in items)
        {
            item.OrderId = order.Id;
            order.Items.Add(item);
        }

        await _context.SaveChangesAsync(cancellationToken);
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
