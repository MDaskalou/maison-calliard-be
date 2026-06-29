using MaisonCalliard.Application.Orders.Dtos;
using MaisonCalliard.Application.Receipts;
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
    Task ResendReceiptAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductRepository _productRepository;
    private readonly IOrderReceiptService _orderReceiptService;

    public OrderService(
        IOrderRepository orderRepository,
        IProductRepository productRepository,
        IOrderReceiptService orderReceiptService)
    {
        _orderRepository = orderRepository;
        _productRepository = productRepository;
        _orderReceiptService = orderReceiptService;
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

        var orderItems = new List<CartItem>();
        foreach (var item in request.Items)
        {
            orderItems.Add(await CreateVerifiedOrderItemAsync(item, cancellationToken));
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            Items = orderItems,
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

            await _orderReceiptService.TrySendReceiptAsync(order.Id, cancellationToken);

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

    public async Task ResendReceiptAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _orderReceiptService.ResendReceiptAsync(id, cancellationToken);
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
            if (string.IsNullOrWhiteSpace(item.ProductId) || !Guid.TryParse(item.ProductId, out _))
            {
                throw new ArgumentException("Order item ProductId must be a valid product id.");
            }

            if (item.Quantity < 1)
            {
                throw new ArgumentException("Order item quantity must be greater than zero.");
            }
        }
    }

    private async Task<CartItem> CreateVerifiedOrderItemAsync(CartItemDto item, CancellationToken cancellationToken)
    {
        var productId = Guid.Parse(item.ProductId);
        var product = await _productRepository.GetByIdAsync(productId, cancellationToken)
            ?? throw new ArgumentException($"Product {item.ProductId} was not found.");

        if (!product.IsAvailable)
        {
            throw new ArgumentException($"Product {item.ProductId} is not available.");
        }

        var option = product.PriceOptions.FirstOrDefault(priceOption =>
            string.Equals(priceOption.Label, item.OptionLabel, StringComparison.Ordinal));

        if (option is null)
        {
            throw new ArgumentException($"Price option '{item.OptionLabel}' is not available for product {item.ProductId}.");
        }

        var taxRate = product.TaxRate.HasValue
            ? Convert.ToDecimal(product.TaxRate.Value)
            : 12m;

        if (taxRate <= 0 || taxRate > 100)
        {
            throw new ArgumentException($"Product {item.ProductId} has an invalid tax rate.");
        }

        return new CartItem
        {
            Id = Guid.NewGuid(),
            CartId = item.CartId,
            ProductId = product.Id.ToString(),
            Name = string.IsNullOrWhiteSpace(product.Name.Se) ? product.Name.En : product.Name.Se,
            ImageUrl = product.ImageUrl,
            OptionLabel = option.Label,
            Price = option.Price,
            Quantity = item.Quantity,
            TaxRate = taxRate
        };
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
