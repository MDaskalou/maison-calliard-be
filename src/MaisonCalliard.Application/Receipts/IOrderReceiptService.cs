namespace MaisonCalliard.Application.Receipts;

public interface IOrderReceiptService
{
    /// <summary>
    /// Sends customer and internal paid-order emails once per order.
    /// </summary>
    Task TrySendReceiptAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the customer receipt again, even if it has already been sent.
    /// </summary>
    Task ResendReceiptAsync(Guid orderId, CancellationToken cancellationToken = default);
}

public interface IOrderReceiptSender
{
    Task<bool> SendAsync(string toEmail, string subject, string html, CancellationToken cancellationToken);
}

public sealed class OrderReceiptDeliveryException : Exception
{
    public OrderReceiptDeliveryException(string message)
        : base(message)
    {
    }

    public OrderReceiptDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
