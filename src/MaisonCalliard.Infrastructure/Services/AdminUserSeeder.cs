using MaisonCalliard.Application.Auth;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed class AdminUserSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<AuthOptions> _options;
    private readonly ILogger<AdminUserSeeder> _logger;

    public AdminUserSeeder(
        IServiceScopeFactory scopes,
        IOptions<AuthOptions> options,
        ILogger<AdminUserSeeder> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var admins = _options.Value.SeedAdmins ?? [];
        if (admins.Length == 0)
        {
            return;
        }

        await using var scope = _scopes.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IAdminUserRepository>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        foreach (var admin in admins)
        {
            if (string.IsNullOrWhiteSpace(admin.Email))
            {
                continue;
            }

            var normalized = AdminUser.NormalizeEmail(admin.Email);
            if (await users.GetByNormalizedEmailAsync(normalized, cancellationToken) is not null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(admin.Password))
            {
                _logger.LogWarning(
                    "Admin seed skipped for {Email} because no individual password is configured.",
                    admin.Email.Trim());
                continue;
            }

            await users.AddAsync(new AdminUser
            {
                Id = Guid.NewGuid(),
                Email = admin.Email.Trim(),
                NormalizedEmail = normalized,
                PasswordHash = hasher.Hash(admin.Password),
                Role = AdminUser.AdminRole,
                IsActive = true,
                MustChangePassword = true,
                TokenVersion = 0,
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
