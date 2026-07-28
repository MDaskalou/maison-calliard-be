using FluentAssertions;
using MaisonCalliard.Api.Controllers;
using MaisonCalliard.Application.Orders;
using MaisonCalliard.Application.Orders.Dtos;
using MaisonCalliard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace MaisonCalliard.UnitTests;

public sealed class OrdersControllerUpdateTests
{
    [Fact]
    public async Task Update_WhenConcurrencyConflict_Returns409()
    {
        var orderId = Guid.NewGuid();
        var orderService = new Mock<IOrderService>();
        orderService
            .Setup(s => s.UpdateAsync(orderId, It.IsAny<UpdateOrderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OrderConcurrencyException("Ordern har ändrats eller tagits bort av en annan begäran. Ladda om och försök igen."));

        var sut = new OrdersController(orderService.Object, Mock.Of<ILogger<OrdersController>>());

        var result = await sut.Update(orderId, new UpdateOrderRequest
        {
            Status = OrderStatus.Paid,
            PickupDateTime = DateTime.UtcNow,
            Location = "Café Caillard, Järntorget",
            CustomerName = "Anna",
            Email = "anna@example.com",
            Phone = "070",
            Items =
            [
                new CartItemDto
                {
                    CartId = "cart-1",
                    ProductId = Guid.NewGuid().ToString(),
                    OptionLabel = "1 styck",
                    Quantity = 1
                }
            ]
        }, CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.StatusCode.Should().Be(409);
    }
}
