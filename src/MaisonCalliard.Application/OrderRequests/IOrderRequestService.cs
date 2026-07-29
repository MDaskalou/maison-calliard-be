using MaisonCalliard.Application.OrderRequests.Dtos;

namespace MaisonCalliard.Application.OrderRequests;

public interface IOrderRequestService
{
    Task<OrderRequestMailResultDto> SendAsync(
        CreateOrderRequestMailDto request,
        CancellationToken cancellationToken = default);
}

public sealed class OrderRequestConfigurationException : Exception
{
    public OrderRequestConfigurationException(string message)
        : base(message)
    {
    }
}

public sealed class OrderRequestDeliveryException : Exception
{
    public OrderRequestDeliveryException(string message)
        : base(message)
    {
    }

    public OrderRequestDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
