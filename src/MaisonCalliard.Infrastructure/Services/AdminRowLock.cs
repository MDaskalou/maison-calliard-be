using MaisonCalliard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MaisonCalliard.Infrastructure.Services;

/// <summary>
/// Lock order for every auth transaction: AdminUsers, LoginEmailQuotas,
/// LoginEmailChallenges, TrustedDevices, RefreshTokens.
/// </summary>
internal static class AdminRowLock
{
    public static Task LockUserAsync(AppDbContext context, Guid userId, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "AdminUsers" WHERE "Id" = {userId} FOR UPDATE""",
            cancellationToken);

    public static Task LockQuotaAsync(AppDbContext context, Guid userId, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "LoginEmailQuotas" WHERE "AdminUserId" = {userId} FOR UPDATE""",
            cancellationToken);

    public static Task LockChallengeAsync(AppDbContext context, Guid challengeId, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "LoginEmailChallenges" WHERE "Id" = {challengeId} FOR UPDATE""",
            cancellationToken);

    public static Task LockDevicesAsync(AppDbContext context, Guid userId, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "TrustedDevices" WHERE "AdminUserId" = {userId} FOR UPDATE""",
            cancellationToken);

    public static Task LockRefreshTokensAsync(AppDbContext context, Guid userId, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "RefreshTokens" WHERE "AdminUserId" = {userId} FOR UPDATE""",
            cancellationToken);
}
