using System.Data;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed class AdminCredentialStore : IAdminCredentialStore
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AuthPolicy _policy;
    private readonly TimeProvider _time;
    private readonly ILogger<AdminCredentialStore> _logger;

    public AdminCredentialStore(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        AuthPolicy policy,
        TimeProvider time,
        ILogger<AdminCredentialStore> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _policy = policy;
        _time = time;
        _logger = logger;
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await AdminRowLock.LockUserAsync(_context, userId, cancellationToken);
            _context.ChangeTracker.Clear();
            var user = await _context.AdminUsers.FirstOrDefaultAsync(admin => admin.Id == userId, cancellationToken);
            var now = _time.GetUtcNow();
            if (user is null || !user.IsActive)
            {
                return new PasswordChangeResult(PasswordChangeStatus.NotAllowed);
            }

            var passwordMatches = _passwordHasher.Verify(currentPassword, user.PasswordHash ?? _passwordHasher.DummyHash);
            if (IsLocked(user, now) || !passwordMatches)
            {
                if (!IsLocked(user, now) && !passwordMatches)
                {
                    user.FailedLoginCount++;
                    if (user.FailedLoginCount >= _policy.LoginLockoutThreshold)
                    {
                        user.LockoutEnd = now.Add(_policy.LockoutDuration);
                    }

                    await SaveOrConflict(transaction, treatConflictAsInvalidPassword: true, cancellationToken);
                }

                return new PasswordChangeResult(PasswordChangeStatus.InvalidCurrentPassword);
            }

            if (_passwordHasher.Verify(newPassword, user.PasswordHash ?? _passwordHasher.DummyHash))
            {
                return new PasswordChangeResult(PasswordChangeStatus.Reused);
            }

            user.PasswordHash = _passwordHasher.Hash(newPassword);
            user.MustChangePassword = false;
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;
            user.TokenVersion++;

            if (_policy.LoginEmailVerification)
            {
                await AdminRowLock.LockDevicesAsync(_context, user.Id, cancellationToken);
                var devices = await _context.TrustedDevices
                    .Where(device => device.AdminUserId == user.Id && device.RevokedAt == null)
                    .ToListAsync(cancellationToken);
                foreach (var device in devices)
                {
                    device.RevokedAt = now;
                }

                SecurityEvents.Write(_logger, LogLevel.Information, "trusted_device_revoked", user.Id);
            }

            await AdminRowLock.LockRefreshTokensAsync(_context, user.Id, cancellationToken);
            var activeTokens = await _context.RefreshTokens
                .Where(token => token.AdminUserId == user.Id && token.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var token in activeTokens)
            {
                token.RevokedAt = now;
            }

            var saved = await SaveOrConflict(transaction, treatConflictAsInvalidPassword: false, cancellationToken);
            return saved
                ? new PasswordChangeResult(PasswordChangeStatus.Success)
                : new PasswordChangeResult(PasswordChangeStatus.Conflict);
        }
        catch (Exception exception) when (IsMissingSchema(exception))
        {
            await SafeRollback(transaction, cancellationToken);
            _context.ChangeTracker.Clear();
            SecurityEvents.Database(_logger, exception, userId);
            return new PasswordChangeResult(PasswordChangeStatus.Unavailable);
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            await SafeRollback(transaction, cancellationToken);
            _context.ChangeTracker.Clear();
            return new PasswordChangeResult(PasswordChangeStatus.Conflict);
        }
    }

    public async Task<SessionRotationResult> RotateAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return new SessionRotationRejected();
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var tokenHash = RefreshTokenProtector.Hash(refreshToken);
            var located = await _context.RefreshTokens.AsNoTracking()
                .Where(token => token.TokenHash == tokenHash)
                .Select(token => new { token.Id, token.AdminUserId })
                .FirstOrDefaultAsync(cancellationToken);
            if (located is null)
            {
                return new SessionRotationRejected();
            }

            await AdminRowLock.LockUserAsync(_context, located.AdminUserId, cancellationToken);
            await AdminRowLock.LockRefreshTokensAsync(_context, located.AdminUserId, cancellationToken);
            _context.ChangeTracker.Clear();
            var existing = await _context.RefreshTokens.FirstOrDefaultAsync(token => token.Id == located.Id, cancellationToken);
            if (existing is null)
            {
                return new SessionRotationRejected();
            }

            var now = _time.GetUtcNow();
            if (existing.RevokedAt is not null)
            {
                await RevokeActiveAsync(existing.AdminUserId, now, cancellationToken);
                await SaveOrConflict(transaction, treatConflictAsInvalidPassword: true, cancellationToken);
                return new SessionRotationRejected();
            }

            if (existing.ExpiresAt <= now)
            {
                return new SessionRotationRejected();
            }

            var user = await _context.AdminUsers.FirstOrDefaultAsync(admin => admin.Id == existing.AdminUserId, cancellationToken);
            if (user is null || !user.IsActive || IsLocked(user, now) || user.MustChangePassword)
            {
                return new SessionRotationRejected();
            }

            var rawRefreshToken = RefreshTokenProtector.Generate();
            var replacement = new RefreshToken
            {
                Id = Guid.NewGuid(),
                AdminUserId = user.Id,
                TokenHash = RefreshTokenProtector.Hash(rawRefreshToken),
                ExpiresAt = now.Add(_policy.RefreshTokenLifetime),
                CreatedAt = now
            };
            existing.RevokedAt = now;
            existing.ReplacedByTokenId = replacement.Id;
            _context.RefreshTokens.Add(replacement);

            var saved = await SaveOrConflict(transaction, treatConflictAsInvalidPassword: true, cancellationToken);
            return saved
                ? new SessionRotated(user, rawRefreshToken, replacement.ExpiresAt)
                : new SessionRotationRejected();
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            await SafeRollback(transaction, cancellationToken);
            _context.ChangeTracker.Clear();
            return new SessionRotationRejected();
        }
    }

    private async Task RevokeActiveAsync(Guid adminUserId, DateTimeOffset revokedAt, CancellationToken cancellationToken)
    {
        var tokens = await _context.RefreshTokens
            .Where(token => token.AdminUserId == adminUserId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.RevokedAt = revokedAt;
        }
    }

    private async Task<bool> SaveOrConflict(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        bool treatConflictAsInvalidPassword,
        CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            await SafeRollback(transaction, cancellationToken);
            _context.ChangeTracker.Clear();
            if (treatConflictAsInvalidPassword)
            {
                return false;
            }

            throw;
        }
    }

    private static async Task SafeRollback(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception)
        {
            // The database already aborted the transaction.
        }
    }

    private static bool IsLocked(AdminUser user, DateTimeOffset now) =>
        user.LockoutEnd is not null && user.LockoutEnd > now;

    private static bool IsMissingSchema(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && postgres.SqlState == "42P01")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsConcurrencyConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException)
            {
                return true;
            }

            if (current is PostgresException postgres && postgres.SqlState is "40001" or "40P01")
            {
                return true;
            }
        }

        return false;
    }
}
