using FluentAssertions;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Infrastructure.Data;
using MaisonCalliard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MaisonCalliard.UnitTests;

public sealed class OrderRepositorySaveOrderUpdateTests
{
    [Fact]
    public async Task SaveOrderUpdateAsync_WhenAddingUnpaidItemWithoutCartId_InsertsAndReturnsGeneratedCartId()
    {
        await using var context = CreateContext();
        var repository = new OrderRepository(context);

        var orderId = Guid.NewGuid();
        var existingItemId = Guid.NewGuid();
        const string existingCartId = "existing-cart-id-1";

        context.Orders.Add(new Order
        {
            Id = orderId,
            Status = OrderStatus.Paid,
            PaidAt = new DateTime(2026, 5, 9, 10, 0, 0, DateTimeKind.Utc),
            PaymentMethod = "Stripe",
            ReceiptNumber = "MC-2026-000042",
            PickupDateTime = new DateTime(2026, 5, 10, 14, 0, 0, DateTimeKind.Utc),
            Location = "Café Caillard, Järntorget",
            CustomerName = "Anna Andersson",
            CustomerAddress = "Storgatan 1",
            Email = "anna@example.com",
            Phone = "070-123 45 67",
            Total = 450m,
            TaxAmount = 48.21m,
            Items =
            [
                new CartItem
                {
                    Id = existingItemId,
                    OrderId = orderId,
                    CartId = existingCartId,
                    ProductId = "11111111-1111-1111-1111-111111111111",
                    Name = "Jordgubbstårta",
                    OptionLabel = "8 bitar",
                    Price = 450m,
                    Quantity = 1,
                    TaxRate = 12m,
                    IsPaid = true
                }
            ]
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var trackedOrder = await repository.GetByIdAsync(orderId);
        trackedOrder.Should().NotBeNull();

        var incomingItems = new List<CartItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                CartId = existingCartId,
                ProductId = "11111111-1111-1111-1111-111111111111",
                Name = "Jordgubbstårta",
                OptionLabel = "8 bitar",
                Price = 450m,
                Quantity = 1,
                TaxRate = 12m,
                IsPaid = true
            },
            new()
            {
                Id = Guid.NewGuid(),
                CartId = string.Empty,
                ProductId = "22222222-2222-2222-2222-222222222222",
                Name = "Croissant",
                OptionLabel = "1 styck",
                Price = 35m,
                Quantity = 1,
                TaxRate = 12m,
                IsPaid = false
            }
        };

        trackedOrder!.Total = 485m;
        trackedOrder.TaxAmount = 51.96m;

        await repository.SaveOrderUpdateAsync(trackedOrder, incomingItems);

        context.ChangeTracker.Clear();
        var reloaded = await repository.GetByIdAsync(orderId);

        reloaded.Should().NotBeNull();
        reloaded!.Items.Should().HaveCount(2);
        reloaded.Total.Should().Be(485m);

        var kept = reloaded.Items.Single(i => i.CartId == existingCartId);
        kept.Id.Should().Be(existingItemId);
        kept.IsPaid.Should().BeTrue();
        kept.Quantity.Should().Be(1);

        var addon = reloaded.Items.Single(i => i.CartId != existingCartId);
        addon.CartId.Should().NotBeNullOrWhiteSpace();
        addon.IsPaid.Should().BeFalse();
        addon.ProductId.Should().Be("22222222-2222-2222-2222-222222222222");
        addon.Name.Should().Be("Croissant");
    }

    [Fact]
    public async Task SaveOrderUpdateAsync_WhenUpdatingExistingItemOnly_KeepsIdentity()
    {
        await using var context = CreateContext();
        var repository = new OrderRepository(context);

        var orderId = Guid.NewGuid();
        var existingItemId = Guid.NewGuid();
        const string existingCartId = "existing-cart-id-1";

        context.Orders.Add(new Order
        {
            Id = orderId,
            Status = OrderStatus.Paid,
            PickupDateTime = DateTime.UtcNow.AddDays(1),
            Location = "Café Caillard, Järntorget",
            CustomerName = "Anna Andersson",
            CustomerAddress = "Storgatan 1",
            Email = "anna@example.com",
            Phone = "070-123 45 67",
            Message = "Original",
            Total = 450m,
            TaxAmount = 48.21m,
            Items =
            [
                new CartItem
                {
                    Id = existingItemId,
                    OrderId = orderId,
                    CartId = existingCartId,
                    ProductId = "11111111-1111-1111-1111-111111111111",
                    Name = "Jordgubbstårta",
                    OptionLabel = "8 bitar",
                    Price = 450m,
                    Quantity = 1,
                    TaxRate = 12m,
                    IsPaid = true
                }
            ]
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var trackedOrder = (await repository.GetByIdAsync(orderId))!;
        trackedOrder.Message = "Updated note";
        trackedOrder.Location = "Maison Caillard, Mölndal";

        await repository.SaveOrderUpdateAsync(
            trackedOrder,
            [
                new CartItem
                {
                    Id = Guid.NewGuid(),
                    CartId = existingCartId,
                    ProductId = "11111111-1111-1111-1111-111111111111",
                    Name = "Jordgubbstårta",
                    OptionLabel = "8 bitar",
                    Price = 450m,
                    Quantity = 2,
                    TaxRate = 12m,
                    IsPaid = true
                }
            ]);

        context.ChangeTracker.Clear();
        var reloaded = (await repository.GetByIdAsync(orderId))!;

        reloaded.Items.Should().ContainSingle();
        reloaded.Items[0].Id.Should().Be(existingItemId);
        reloaded.Items[0].Quantity.Should().Be(2);
        reloaded.Message.Should().Be("Updated note");
        reloaded.Location.Should().Be("Maison Caillard, Mölndal");
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
