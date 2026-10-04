using FluentAssertions;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Enums;
using MaisonCalliard.Infrastructure.Data;
using MaisonCalliard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MaisonCalliard.UnitTests;

public sealed class OrderRepositoryArchiveTests
{
    [Fact]
    public async Task GetAllAsync_HidesArchivedOrdersUnlessIncludeArchived()
    {
        await using var context = CreateContext();
        var repository = new OrderRepository(context);

        var active = CreateOrder(450m);
        var archived = CreateOrder(120m);
        archived.ArchivedAt = new DateTimeOffset(2026, 5, 2, 8, 0, 0, TimeSpan.Zero);
        context.Orders.AddRange(active, archived);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var visible = await repository.GetAllAsync();
        var includingArchived = await repository.GetAllAsync(includeArchived: true);

        visible.Should().ContainSingle();
        visible[0].Id.Should().Be(active.Id);
        visible[0].Total.Should().Be(450m);

        includingArchived.Should().HaveCount(2);
        var archivedResult = includingArchived.Single(o => o.Id == archived.Id);
        archivedResult.ArchivedAt.Should().Be(archived.ArchivedAt);
        archivedResult.Total.Should().Be(120m);
        archivedResult.Status.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsArchivedOrder()
    {
        await using var context = CreateContext();
        var repository = new OrderRepository(context);

        var archived = CreateOrder(120m);
        archived.ArchivedAt = new DateTimeOffset(2026, 5, 2, 8, 0, 0, TimeSpan.Zero);
        context.Orders.Add(archived);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await repository.GetByIdAsync(archived.Id);

        result.Should().NotBeNull();
        result!.ArchivedAt.Should().Be(archived.ArchivedAt);
        result.Total.Should().Be(120m);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOrderAndItems()
    {
        await using var context = CreateContext();
        var repository = new OrderRepository(context);

        var order = CreateOrder(450m);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var tracked = (await repository.GetByIdAsync(order.Id))!;
        await repository.DeleteAsync(tracked);

        (await repository.GetByIdAsync(order.Id)).Should().BeNull();
        context.CartItems.Should().BeEmpty();
        context.Orders.Should().BeEmpty();
    }

    private static Order CreateOrder(decimal total)
    {
        var orderId = Guid.NewGuid();
        return new Order
        {
            Id = orderId,
            Status = OrderStatus.Paid,
            PaidAt = new DateTime(2026, 5, 9, 10, 0, 0, DateTimeKind.Utc),
            PaymentMethod = "Stripe",
            PickupDateTime = new DateTime(2026, 5, 10, 14, 0, 0, DateTimeKind.Utc),
            Location = "Café Caillard, Järntorget",
            CustomerName = "Anna Andersson",
            Email = "anna@example.com",
            Phone = "070-123 45 67",
            CreatedAt = new DateTime(2026, 5, 9, 9, 0, 0, DateTimeKind.Utc),
            Total = total,
            TaxAmount = 10m,
            Items =
            [
                new CartItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    CartId = Guid.NewGuid().ToString("N"),
                    ProductId = "11111111-1111-1111-1111-111111111111",
                    Name = "Jordgubbstårta",
                    OptionLabel = "8 bitar",
                    Price = total,
                    Quantity = 1,
                    TaxRate = 12m,
                    IsPaid = true
                }
            ]
        };
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
