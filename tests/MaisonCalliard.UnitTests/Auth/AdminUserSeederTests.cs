using FluentAssertions;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Infrastructure.Data;
using MaisonCalliard.Infrastructure.Options;
using MaisonCalliard.Infrastructure.Repositories;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class AdminUserSeederTests
{
    [Fact]
    public async Task StartAsync_CreatesAMissingAdminAndLeavesAnExistingAdminUntouched()
    {
        var databaseName = Guid.NewGuid().ToString();
        var hasher = new IdentityPasswordHasher();
        await using var setup = CreateContext(databaseName);
        var existing = new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = "existing@example.com",
            NormalizedEmail = AdminUser.NormalizeEmail("existing@example.com"),
            PasswordHash = hasher.Hash("original-password"),
            Role = AdminUser.AdminRole,
            IsActive = true,
            MustChangePassword = false,
            TokenVersion = 4,
            CreatedAt = DateTimeOffset.UtcNow
        };
        setup.AdminUsers.Add(existing);
        await setup.SaveChangesAsync();

        var options = new AuthOptions
        {
            SeedPassword = "shared-password-must-not-be-used",
            SeedEmails = ["existing@example.com", "new@example.com"],
            SeedAdmins =
            [
                new SeedAdminCredential { Email = "existing@example.com", Password = "individual-temp-password" },
                new SeedAdminCredential { Email = "new@example.com", Password = "another-individual-password" },
                new SeedAdminCredential { Email = "nopass@example.com" }
            ]
        };

        await CreateSeeder(databaseName, options).StartAsync(CancellationToken.None);

        await using var verification = CreateContext(databaseName);
        var admins = await verification.AdminUsers.ToListAsync();
        admins.Should().HaveCount(2);
        var unchanged = admins.Single(admin => admin.Email == "existing@example.com");
        hasher.Verify("original-password", unchanged.PasswordHash!).Should().BeTrue();
        hasher.Verify("individual-temp-password", unchanged.PasswordHash!).Should().BeFalse();
        unchanged.MustChangePassword.Should().BeFalse();
        unchanged.TokenVersion.Should().Be(4);

        var created = admins.Single(admin => admin.Email == "new@example.com");
        created.MustChangePassword.Should().BeTrue();
        hasher.Verify("another-individual-password", created.PasswordHash!).Should().BeTrue();
        hasher.Verify("shared-password-must-not-be-used", created.PasswordHash!).Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_WhenOnlyTheSharedPasswordIsConfigured_CreatesNobody()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new AuthOptions
        {
            SeedPassword = "shared-password-must-not-be-used",
            SeedEmails = ["admin@example.com"]
        };

        await CreateSeeder(databaseName, options).StartAsync(CancellationToken.None);

        await using var verification = CreateContext(databaseName);
        (await verification.AdminUsers.CountAsync()).Should().Be(0);
    }

    private static AdminUserSeeder CreateSeeder(string databaseName, AuthOptions options)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(builder => builder.UseInMemoryDatabase(databaseName));
        services.AddScoped<IAdminUserRepository, AdminUserRepository>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        return new AdminUserSeeder(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<AdminUserSeeder>.Instance);
    }

    private static AppDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options;
        return new AppDbContext(options);
    }
}
