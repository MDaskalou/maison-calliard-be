using MaisonCalliard.Domain.Entities;

namespace MaisonCalliard.Domain.Repositories;

public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Order?> GetByStripeSessionIdAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<Order?> GetByStripePaymentIntentIdAsync(string paymentIntentId, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task ReplaceItemsAsync(Guid orderId, IReadOnlyList<CartItem> items, CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
    Task MarkAsPaidAsync(
        Order order,
        DateTime paidAt,
        string paymentMethod,
        string? stripePaymentIntentId,
        string? stripeSessionId,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(Order order, CancellationToken cancellationToken = default);
}
