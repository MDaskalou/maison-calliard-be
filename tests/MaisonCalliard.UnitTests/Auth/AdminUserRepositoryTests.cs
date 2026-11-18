using FluentAssertions;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Infrastructure.Data;
using MaisonCalliard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class AdminUserRepositoryTests
{
    [Fact]
    public async Task GetByNormalizedEmailAsync_FindsTheAdminRegardlessOfAddressCasing()
    {
        await using var context = CreateContext();
        var repository = new AdminUserRepository(context);
        var user = new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = "Admin@Example.com",
            NormalizedEmail = AdminUser.NormalizeEmail("Admin@Example.com"),
            PasswordHash = "hash",
            Role = AdminUser.AdminRole,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await repository.AddAsync(user);
        context.ChangeTracker.Clear();

        var found = await repository.GetByNormalizedEmailAsync(AdminUser.NormalizeEmail("admin@example.com"));

        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
        found.Email.Should().Be("Admin@Example.com");
    }

    [Fact]
    public async Task RefreshTokenRepository_StoresTheHashRatherThanTheRawToken()
    {
        await using var context = CreateContext();
        var users = new AdminUserRepository(context);
        var tokens = new RefreshTokenRepository(context);
        var user = new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = "admin@example.com",
            NormalizedEmail = AdminUser.NormalizeEmail("admin@example.com"),
            Role = AdminUser.AdminRole,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await users.AddAsync(user);

        var rawToken = "plain-refresh-token";
        var tokenHash = RefreshTokenProtector.Hash(rawToken);
        await tokens.AddAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            AdminUserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            CreatedAt = DateTimeOffset.UtcNow
        });
        context.ChangeTracker.Clear();

        var stored = await tokens.GetByHashAsync(tokenHash);

        stored.Should().NotBeNull();
        stored!.TokenHash.Should().Be(tokenHash);
        stored.TokenHash.Should().NotBe(rawToken);
        (await tokens.GetByHashAsync(rawToken)).Should().BeNull();
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
