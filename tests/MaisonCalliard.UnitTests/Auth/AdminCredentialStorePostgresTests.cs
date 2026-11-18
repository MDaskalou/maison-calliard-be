using FluentAssertions;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Infrastructure.Data;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class PostgresAdminDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
    }

    public string ConnectionString => _postgres.GetConnectionString();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

public sealed class AdminCredentialStorePostgresTests : IClassFixture<PostgresAdminDatabase>
{
    private readonly PostgresAdminDatabase _database;

    public AdminCredentialStorePostgresTests(PostgresAdminDatabase database)
    {
        _database = database;
    }

    [Fact]
    public async Task ChangePasswordAsync_UpdatesTheHashVersionAndRevokesEveryActiveRefreshToken()
    {
        var hasher = new IdentityPasswordHasher();
        var userId = await SeedAdminAsync(hasher, "current-password", mustChangePassword: true);
        var firstToken = await SeedRefreshTokenAsync(userId);
        await SeedRefreshTokenAsync(userId);
        var store = CreateStore(hasher);

        var result = await store.ChangePasswordAsync(userId, "current-password", "a-new-passphrase");

        result.Status.Should().Be(PasswordChangeStatus.Success);
        await using var verification = CreateDatabase();
        var user = await verification.AdminUsers.SingleAsync(admin => admin.Id == userId);
        hasher.Verify("a-new-passphrase", user.PasswordHash!).Should().BeTrue();
        hasher.Verify("current-password", user.PasswordHash!).Should().BeFalse();
        user.MustChangePassword.Should().BeFalse();
        user.TokenVersion.Should().Be(1);
        var tokens = await verification.RefreshTokens.Where(token => token.AdminUserId == userId).ToListAsync();
        tokens.Should().OnlyContain(token => token.RevokedAt != null);

        var rotation = await CreateStore(hasher).RotateAsync(firstToken);
        rotation.Should().BeOfType<SessionRotationRejected>();
    }

    [Fact]
    public async Task RotateAsync_WhilePasswordChangeIsRequired_DoesNotCreateASession()
    {
        var hasher = new IdentityPasswordHasher();
        var userId = await SeedAdminAsync(hasher, "current-password", mustChangePassword: true);
        var rawToken = await SeedRefreshTokenAsync(userId);

        var rotation = await CreateStore(hasher).RotateAsync(rawToken);

        rotation.Should().BeOfType<SessionRotationRejected>();
        await using var verification = CreateDatabase();
        var token = await verification.RefreshTokens.SingleAsync(item => item.AdminUserId == userId);
        token.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenTheNewPasswordMatchesTheCurrentOne_DoesNotWrite()
    {
        var hasher = new IdentityPasswordHasher();
        var userId = await SeedAdminAsync(hasher, "current-password", mustChangePassword: true);

        var result = await CreateStore(hasher).ChangePasswordAsync(userId, "current-password", "current-password");

        result.Status.Should().Be(PasswordChangeStatus.Reused);
        await using var verification = CreateDatabase();
        var user = await verification.AdminUsers.SingleAsync(admin => admin.Id == userId);
        user.MustChangePassword.Should().BeTrue();
        user.TokenVersion.Should().Be(0);
        hasher.Verify("current-password", user.PasswordHash!).Should().BeTrue();
    }

    [Fact]
    public async Task RotateAsync_WhenPasswordChangeIsNotRequired_ReplacesTheRefreshToken()
    {
        var hasher = new IdentityPasswordHasher();
        var userId = await SeedAdminAsync(hasher, "current-password", mustChangePassword: false);
        var rawToken = await SeedRefreshTokenAsync(userId);

        var rotation = await CreateStore(hasher).RotateAsync(rawToken);

        var rotated = rotation.Should().BeOfType<SessionRotated>().Subject;
        rotated.User.TokenVersion.Should().Be(0);
        rotated.RefreshToken.Should().NotBe(rawToken);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenTwoChangesRunTogether_KeepsOneConsistentResult()
    {
        var hasher = new IdentityPasswordHasher();
        var userId = await SeedAdminAsync(hasher, "current-password", mustChangePassword: true);
        await SeedRefreshTokenAsync(userId);
        var first = CreateStore(hasher);
        var second = CreateStore(hasher);

        var results = await Task.WhenAll(
            first.ChangePasswordAsync(userId, "current-password", "first-new-passphrase"),
            second.ChangePasswordAsync(userId, "current-password", "second-new-passphrase"));

        results.Should().ContainSingle(result => result.Status == PasswordChangeStatus.Success);
        results.Count(result => result.Status is PasswordChangeStatus.Conflict or PasswordChangeStatus.InvalidCurrentPassword)
            .Should().Be(1);
        await using var verification = CreateDatabase();
        var user = await verification.AdminUsers.SingleAsync(admin => admin.Id == userId);
        user.TokenVersion.Should().Be(1);
        user.MustChangePassword.Should().BeFalse();
        var winner = hasher.Verify("first-new-passphrase", user.PasswordHash!)
            ? "first-new-passphrase"
            : "second-new-passphrase";
        hasher.Verify(winner, user.PasswordHash!).Should().BeTrue();
        hasher.Verify("current-password", user.PasswordHash!).Should().BeFalse();
        (await verification.RefreshTokens.SingleAsync(token => token.AdminUserId == userId)).RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RotateAsync_WhenTwoCallsUseTheSameToken_LeavesNoActiveSession()
    {
        var hasher = new IdentityPasswordHasher();
        var userId = await SeedAdminAsync(hasher, "current-password", mustChangePassword: false);
        var rawToken = await SeedRefreshTokenAsync(userId);
        var first = CreateStore(hasher);
        var second = CreateStore(hasher);
        var gate = new Barrier(2);

        var firstTask = Task.Run(async () =>
        {
            gate.SignalAndWait();
            return await first.RotateAsync(rawToken);
        });
        var secondTask = Task.Run(async () =>
        {
            gate.SignalAndWait();
            return await second.RotateAsync(rawToken);
        });
        var work = Task.WhenAll(firstTask, secondTask);
        var completed = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(30)));

        completed.Should().BeSameAs(work, "the row locks should finish without a deadlock");
        var results = await work;
        results.Should().ContainSingle(result => result is SessionRotated);
        results.Should().ContainSingle(result => result is SessionRotationRejected);

        await using var verification = CreateDatabase();
        var tokens = await verification.RefreshTokens.Where(token => token.AdminUserId == userId).ToListAsync();
        tokens.Should().HaveCount(2);
        tokens.Should().OnlyContain(token => token.RevokedAt != null);
    }

    private AdminCredentialStore CreateStore(IdentityPasswordHasher hasher) =>
        new(CreateDatabase(), hasher, new AuthPolicy(), TimeProvider.System, NullLogger<AdminCredentialStore>.Instance);

    private AppDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.ConnectionString).Options;
        return new AppDbContext(options);
    }

    private async Task<Guid> SeedAdminAsync(IdentityPasswordHasher hasher, string password, bool mustChangePassword)
    {
        await using var context = CreateDatabase();
        var email = $"{Guid.NewGuid():N}@example.com";
        var user = new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = AdminUser.NormalizeEmail(email),
            PasswordHash = hasher.Hash(password),
            Role = AdminUser.AdminRole,
            IsActive = true,
            MustChangePassword = mustChangePassword,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.AdminUsers.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<string> SeedRefreshTokenAsync(Guid userId)
    {
        var rawToken = Guid.NewGuid().ToString("N");
        await using var context = CreateDatabase();
        context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            AdminUserId = userId,
            TokenHash = RefreshTokenProtector.Hash(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        return rawToken;
    }
}
