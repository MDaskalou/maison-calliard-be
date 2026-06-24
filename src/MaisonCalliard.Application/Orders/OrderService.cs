using MaisonCalliard.Application.Orders.Dtos;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Domain.Repositories;

namespace MaisonCalliard.Application.Orders;

public interface IOrderService
{
    Task<IReadOnlyList<OrderDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<OrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrderDto> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;

    public OrderService(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<IReadOnlyList<OrderDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.GetAllAsync(cancellationToken);
        return orders.Select(MapToDto).ToList();
    }

    public async Task<OrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(id, cancellationToken);
        return order is null ? null : MapToDto(order);
    }

    public async Task<OrderDto> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        ValidateCreateOrderRequest(request);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Items = request.Items.Select(i => new CartItem
            {
                Id = Guid.NewGuid(),
                CartId = i.CartId,
                ProductId = i.ProductId,
                Name = i.Name,
                ImageUrl = i.ImageUrl,
                OptionLabel = i.OptionLabel,
                Price = GetUnitPrice(i),
                Quantity = i.Quantity,
                TaxRate = i.TaxRate
            }).ToList(),
            PickupDateTime = request.PickupDateTime,
            Location = request.Location,
            CustomerName = request.CustomerName,
            CustomerAddress = request.CustomerAddress,
            Email = request.Email,
            Phone = request.Phone,
            Message = request.Message,
            Status = OrderStatus.AwaitingPayment,
            CreatedAt = DateTime.UtcNow
        };

        order.Total = CalculateTotal(order.Items);
        order.TaxAmount = CalculateVatBreakdown(order.Items).Sum(v => v.TaxAmount);

        await _orderRepository.AddAsync(order, cancellationToken);
        return MapToDto(order);
    }

    public async Task<OrderDto> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Order {id} not found.");

        if (request.Status == OrderStatus.Pending && order.Status == OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException("Order cannot be activated until payment is confirmed.");
        }

        if (request.Status == OrderStatus.Paid)
        {
            await _orderRepository.MarkAsPaidAsync(
                order,
                DateTime.UtcNow,
                order.PaymentMethod ?? "Manuell betalning",
                order.StripePaymentIntentId,
                order.StripeSessionId,
                cancellationToken);

            return MapToDto(order);
        }

        order.Status = request.Status;
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return MapToDto(order);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Order {id} not found.");

        await _orderRepository.DeleteAsync(order, cancellationToken);
    }

    private static OrderDto MapToDto(Order order)
    {
        return new OrderDto
        {
            Id = order.Id,
            ReceiptNumber = order.ReceiptNumber,
            StripeSessionId = order.StripeSessionId,
            StripePaymentIntentId = order.StripePaymentIntentId,
            Items = order.Items.Select(i => new CartItemDto
            {
                CartId = i.CartId,
                ProductId = i.ProductId,
                Name = i.Name,
                ImageUrl = i.ImageUrl,
                OptionLabel = i.OptionLabel,
                Price = i.Price,
                UnitPrice = i.Price,
                LineTotal = Math.Round(i.Price * i.Quantity, 2, MidpointRounding.AwayFromZero),
                Quantity = i.Quantity,
                TaxRate = i.TaxRate
            }).ToList(),
            Total = order.Total,
            TaxAmount = order.TaxAmount,
            VatBreakdown = CalculateVatBreakdown(order.Items),
            PickupDateTime = order.PickupDateTime,
            Location = order.Location,
            CustomerName = order.CustomerName,
            CustomerAddress = order.CustomerAddress,
            Email = order.Email,
            Phone = order.Phone,
            Message = order.Message,
            Status = order.Status,
            PaymentMethod = order.PaymentMethod,
            PaidAt = order.PaidAt,
            IsPrinted = order.IsPrinted,
            CustomerEmailSentAt = order.CustomerEmailSentAt ?? order.ReceiptSentAt,
            InternalNotificationSentAt = order.InternalNotificationSentAt,
            Seller = new SellerDto(),
            CreatedAt = order.CreatedAt
        };
    }

    private static void ValidateCreateOrderRequest(CreateOrderRequest request)
    {
        if (request.Items.Count == 0)
        {
            throw new ArgumentException("At least one order item is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerAddress))
        {
            throw new ArgumentException("CustomerAddress is required.");
        }

        foreach (var item in request.Items)
        {
            if (GetUnitPrice(item) < 0)
            {
                throw new ArgumentException("Order item unit price cannot be negative.");
            }

            if (item.Quantity < 1)
            {
                throw new ArgumentException("Order item quantity must be greater than zero.");
            }

            if (item.TaxRate <= 0 || item.TaxRate > 100)
            {
                throw new ArgumentException("Order item TaxRate must be greater than zero and no more than 100.");
            }
        }
    }

    private static decimal GetUnitPrice(CartItemDto item)
    {
        return item.UnitPrice > 0 ? item.UnitPrice : item.Price;
    }

    private static decimal CalculateTotal(IEnumerable<CartItem> items)
    {
        return Math.Round(items.Sum(i => i.Price * i.Quantity), 2, MidpointRounding.AwayFromZero);
    }

    private static List<VatBreakdownDto> CalculateVatBreakdown(IEnumerable<CartItem> items)
    {
        return items
            .GroupBy(i => i.TaxRate)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var grossAmount = Math.Round(g.Sum(i => i.Price * i.Quantity), 2, MidpointRounding.AwayFromZero);
                var netAmount = Math.Round(grossAmount / (1 + (g.Key / 100m)), 2, MidpointRounding.AwayFromZero);

                return new VatBreakdownDto
                {
                    TaxRate = g.Key,
                    NetAmount = netAmount,
                    TaxAmount = grossAmount - netAmount,
                    GrossAmount = grossAmount
                };
            })
            .ToList();
    }
}
