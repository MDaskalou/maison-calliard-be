namespace MaisonCalliard.Application.Receipts;

public interface IOrderReceiptService
{
    /// <summary>
    /// Sends customer and internal paid-order emails once per order.
    /// </summary>
    Task TrySendReceiptAsync(Guid orderId, CancellationToken cancellationToken = default);
}
