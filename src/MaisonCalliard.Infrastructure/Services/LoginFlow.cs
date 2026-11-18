using System.Data;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Infrastructure.Data;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed class LoginFlow : ILoginFlow
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher _passwords;
    private readonly IAccessTokenIssuer _accessTokens;
    private readonly ILoginVerificationEmailSender _email;
    private readonly AuthPolicy _policy;
    private readonly AuthOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<LoginFlow> _logger;

    public LoginFlow(
        AppDbContext context,
        IPasswordHasher passwords,
        IAccessTokenIssuer accessTokens,
        ILoginVerificationEmailSender email,
        AuthPolicy policy,
        IOptions<AuthOptions> options,
        TimeProvider time,
        ILogger<LoginFlow> logger)
    {
        _context = context;
        _passwords = passwords;
        _accessTokens = accessTokens;
        _email = email;
        _policy = policy;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<AuthResult> CompletePasswordLoginAsync(
        Guid userId,
        string password,
        string? trustedDeviceToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LoginEmailChallenge? pendingChallenge = null;
            string? pendingToken = null;
            string? pendingCode = null;
            string? pendingEmail = null;
            var clearCookie = false;
            await using (var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
            {
                await AdminRowLock.LockUserAsync(_context, userId, cancellationToken);
                DetachAll();
                var user = await _context.AdminUsers.FirstOrDefaultAsync(admin => admin.Id == userId, cancellationToken);
                var now = _time.GetUtcNow();
                if (user is null || !user.IsActive || IsPasswordLocked(user, now) || !PasswordStillMatches(user, password))
                {
                    return new AuthRejected();
                }

                if (!_policy.LoginEmailVerification)
                {
                    return await IssueSessionAsync(user, now, null, null, false, cancellationToken);
                }

                if (!PepperReady(user.Id))
                {
                    return new AuthRejected();
                }

                if (!string.IsNullOrWhiteSpace(trustedDeviceToken))
                {
                    await AdminRowLock.LockQuotaAsync(_context, user.Id, cancellationToken);
                    await AdminRowLock.LockDevicesAsync(_context, user.Id, cancellationToken);
                }

                var device = await FindDeviceAsync(trustedDeviceToken, cancellationToken);
                if (device is not null && device.AdminUserId == user.Id && DeviceUsable(device, user, now))
                {
                    device.LastUsedAt = now;
                    return await IssueSessionAsync(user, now, null, null, false, cancellationToken);
                }

                clearCookie = device is not null && device.AdminUserId == user.Id;
                if (await IsOtpLockedAsync(user.Id, now, cancellationToken))
                {
                    SecurityEvents.Write(_logger, LogLevel.Warning, "login_otp_lockout", user.Id);
                    return new AuthRejected(clearCookie);
                }

                var reservation = await ReserveSendAsync(user.Id, now, cancellationToken);
                if (reservation != SendReservation.Reserved)
                {
                    LogSendBlocked(user.Id, reservation);
                    return new AuthRejected(clearCookie);
                }

                var (challenge, challengeToken) = CreateChallenge(user, now, _policy.LoginCodeLifetime);
                var code = LoginCodeHasher.CreateCode();
                challenge.CodeHash = LoginCodeHasher.Hash(Pepper(), challenge.Id, code);
                _context.LoginEmailChallenges.Add(challenge);
                user.FailedLoginCount = 0;
                user.LockoutEnd = null;
                DetachQuotas();
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                pendingChallenge = challenge;
                pendingToken = challengeToken;
                pendingCode = code;
                pendingEmail = user.Email;
            }

            SecurityEvents.Write(_logger, LogLevel.Information, "login_challenge_created", userId);
            return await ActivateAsync(userId, pendingEmail!, pendingChallenge!, pendingToken!, pendingCode!, clearCookie, cancellationToken);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            SecurityEvents.Database(_logger, exception, userId);
            DetachAll();
            return new AuthRejected();
        }
    }

    public async Task<AuthResult> CompleteGoogleLoginAsync(
        Guid userId,
        string? googleSubjectToLink,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await AdminRowLock.LockUserAsync(_context, userId, cancellationToken);
            DetachAll();
            var user = await _context.AdminUsers.FirstOrDefaultAsync(admin => admin.Id == userId, cancellationToken);
            var now = _time.GetUtcNow();
            if (user is null || !user.IsActive || IsPasswordLocked(user, now))
            {
                return new AuthRejected();
            }

            if (googleSubjectToLink is not null)
            {
                if (user.GoogleSubject is not null && !string.Equals(user.GoogleSubject, googleSubjectToLink, StringComparison.Ordinal))
                {
                    return new AuthRejected();
                }

                user.GoogleSubject ??= googleSubjectToLink;
            }

            return await IssueSessionAsync(user, now, null, null, false, cancellationToken);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            SecurityEvents.Database(_logger, exception, userId);
            DetachAll();
            return new AuthRejected();
        }
    }

    public async Task<AuthResult> VerifyEmailCodeAsync(
        string challengeToken,
        string code,
        bool rememberDevice,
        string? deviceLabel,
        CancellationToken cancellationToken = default)
    {
        if (!_policy.LoginEmailVerification || string.IsNullOrWhiteSpace(challengeToken) || string.IsNullOrWhiteSpace(code))
        {
            return new AuthRejected();
        }

        if (!PepperReady(null))
        {
            return new AuthRejected();
        }

        Guid? adminUserId = null;
        try
        {
            var located = await LocateAsync(challengeToken, cancellationToken);
            if (located is null)
            {
                SecurityEvents.Write(_logger, LogLevel.Information, "login_code_verification_failed", null);
                return new AuthRejected();
            }

            adminUserId = located.AdminUserId;
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await AdminRowLock.LockUserAsync(_context, located.AdminUserId, cancellationToken);
            await AdminRowLock.LockQuotaAsync(_context, located.AdminUserId, cancellationToken);
            await AdminRowLock.LockChallengeAsync(_context, located.Id, cancellationToken);
            DetachAll();
            var user = await _context.AdminUsers.FirstOrDefaultAsync(admin => admin.Id == located.AdminUserId, cancellationToken);
            var challenge = await _context.LoginEmailChallenges.FirstOrDefaultAsync(item => item.Id == located.Id, cancellationToken);
            var now = _time.GetUtcNow();
            if (user is null || challenge is null || !user.IsActive || IsPasswordLocked(user, now))
            {
                SecurityEvents.Write(_logger, LogLevel.Information, "login_code_verification_failed", located.AdminUserId);
                return new AuthRejected();
            }

            if (await ReadLockoutAsync(user.Id, cancellationToken) is DateTimeOffset lockout && lockout > now)
            {
                SecurityEvents.Write(_logger, LogLevel.Warning, "login_otp_lockout", user.Id);
                return new AuthRejected();
            }

            if (!CanAcceptCode(challenge, user, now))
            {
                SecurityEvents.Write(_logger, LogLevel.Information, "login_code_verification_failed", user.Id);
                return new AuthRejected();
            }

            if (!LoginCodeHasher.Matches(Pepper(), challenge.Id, code.Trim(), challenge.CodeHash))
            {
                challenge.AttemptCount++;
                if (challenge.AttemptCount >= _policy.MaxCodeAttemptsPerChallenge)
                {
                    challenge.ConsumedAt = now;
                }

                var locked = await RegisterOtpFailureAsync(user.Id, now, cancellationToken);
                DetachQuotas();
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                SecurityEvents.Write(
                    _logger,
                    locked ? LogLevel.Warning : LogLevel.Information,
                    locked ? "login_otp_lockout" : "login_code_verification_failed",
                    user.Id);
                return new AuthRejected();
            }

            challenge.ConsumedAt = now;
            string? deviceToken = null;
            DateTimeOffset? deviceExpires = null;
            if (rememberDevice)
            {
                deviceToken = RefreshTokenProtector.Generate();
                deviceExpires = now.Add(_policy.TrustedDeviceLifetime);
                _context.TrustedDevices.Add(new TrustedDevice
                {
                    Id = Guid.NewGuid(),
                    AdminUserId = user.Id,
                    TokenVersion = user.TokenVersion,
                    TokenHash = RefreshTokenProtector.Hash(deviceToken),
                    Label = SanitizeLabel(deviceLabel),
                    CreatedAt = now,
                    ExpiresAt = deviceExpires.Value
                });
                SecurityEvents.Write(_logger, LogLevel.Information, "trusted_device_registered", user.Id);
            }

            var session = await IssueSessionAsync(user, now, deviceToken, deviceExpires, false, cancellationToken);
            SecurityEvents.Write(_logger, LogLevel.Information, "login_code_verified", user.Id);
            return session;
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            SecurityEvents.Database(_logger, exception, adminUserId);
            DetachAll();
            return new AuthRejected();
        }
    }

    public async Task<ResendLoginCodeResult> ResendEmailCodeAsync(
        string challengeToken,
        CancellationToken cancellationToken = default)
    {
        if (!_policy.LoginEmailVerification || string.IsNullOrWhiteSpace(challengeToken) || !PepperReady(null))
        {
            return new ResendLoginCodeRejected();
        }

        Guid? adminUserId = null;
        try
        {
            var located = await LocateAsync(challengeToken, cancellationToken);
            if (located is null)
            {
                return new ResendLoginCodeRejected();
            }

            adminUserId = located.AdminUserId;
            string? pendingCode = null;
            string? pendingEmail = null;
            LoginEmailChallenge? pendingChallenge = null;
            await using (var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
            {
                await AdminRowLock.LockUserAsync(_context, located.AdminUserId, cancellationToken);
                await AdminRowLock.LockQuotaAsync(_context, located.AdminUserId, cancellationToken);
                await AdminRowLock.LockChallengeAsync(_context, located.Id, cancellationToken);
                DetachAll();
                var user = await _context.AdminUsers.FirstOrDefaultAsync(admin => admin.Id == located.AdminUserId, cancellationToken);
                var challenge = await _context.LoginEmailChallenges.FirstOrDefaultAsync(item => item.Id == located.Id, cancellationToken);
                var now = _time.GetUtcNow();
                if (user is null || challenge is null || !user.IsActive || challenge.AdminUserId != user.Id
                    || challenge.TokenVersion != user.TokenVersion || challenge.ConsumedAt is not null
                    || challenge.SendCount >= _policy.MaxSendsPerChallenge)
                {
                    return new ResendLoginCodeRejected();
                }

                if (await ReadLockoutAsync(user.Id, cancellationToken) is DateTimeOffset lockout && lockout > now)
                {
                    SecurityEvents.Write(_logger, LogLevel.Warning, "login_otp_lockout", user.Id);
                    return new ResendLoginCodeRejected();
                }

                var reservation = await ReserveSendAsync(user.Id, now, cancellationToken);
                if (reservation != SendReservation.Reserved)
                {
                    LogSendBlocked(user.Id, reservation);
                    return new ResendLoginCodeRejected();
                }

                pendingCode = LoginCodeHasher.CreateCode();
                challenge.CodeHash = LoginCodeHasher.Hash(Pepper(), challenge.Id, pendingCode);
                challenge.SentAt = null;
                challenge.AttemptCount = 0;
                challenge.SendCount++;
                challenge.ExpiresAt = now.Add(_policy.LoginCodeLifetime);
                DetachQuotas();
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                pendingEmail = user.Email;
                pendingChallenge = challenge;
            }

            var activated = await ActivateAsync(adminUserId.Value, pendingEmail!, pendingChallenge!, challengeToken, pendingCode!, false, cancellationToken);
            return activated is AuthVerificationRequired required
                ? new ResendLoginCodeSent(required.ExpiresAt)
                : new ResendLoginCodeRejected();
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            SecurityEvents.Database(_logger, exception, adminUserId);
            DetachAll();
            return new ResendLoginCodeRejected();
        }
    }

    public async Task<TrustedDeviceListResult> ListTrustedDevicesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!_policy.LoginEmailVerification)
        {
            return new TrustedDeviceListResult(TrustedDeviceListStatus.Disabled, []);
        }

        try
        {
            var now = _time.GetUtcNow();
            var devices = await _context.TrustedDevices.AsNoTracking()
                .Where(device => device.AdminUserId == userId && device.RevokedAt == null && device.ExpiresAt > now)
                .OrderByDescending(device => device.CreatedAt)
                .Select(device => new TrustedDeviceSummary(device.Id, device.CreatedAt, device.ExpiresAt, device.LastUsedAt, device.Label))
                .ToListAsync(cancellationToken);
            return new TrustedDeviceListResult(TrustedDeviceListStatus.Ok, devices);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            SecurityEvents.Database(_logger, exception, userId);
            return new TrustedDeviceListResult(TrustedDeviceListStatus.Unavailable, []);
        }
    }

    public async Task<TrustedDeviceRevocationResult> RevokeTrustedDeviceAsync(
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        if (!_policy.LoginEmailVerification)
        {
            return new TrustedDeviceRevocationResult(TrustedDeviceRevocationStatus.Disabled, false);
        }

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await AdminRowLock.LockUserAsync(_context, userId, cancellationToken);
            await AdminRowLock.LockDevicesAsync(_context, userId, cancellationToken);
            DetachAll();
            var device = await _context.TrustedDevices.FirstOrDefaultAsync(
                item => item.Id == deviceId && item.AdminUserId == userId,
                cancellationToken);
            if (device is null)
            {
                return new TrustedDeviceRevocationResult(TrustedDeviceRevocationStatus.NotFound, false);
            }

            if (device.RevokedAt is null)
            {
                device.RevokedAt = _time.GetUtcNow();
                await _context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            SecurityEvents.Write(_logger, LogLevel.Information, "trusted_device_revoked", userId);
            return new TrustedDeviceRevocationResult(TrustedDeviceRevocationStatus.Revoked, true);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            SecurityEvents.Database(_logger, exception, userId);
            DetachAll();
            return new TrustedDeviceRevocationResult(TrustedDeviceRevocationStatus.Unavailable, false);
        }
    }

    public async Task<TrustedDeviceRevocationResult> RevokeAllTrustedDevicesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!_policy.LoginEmailVerification)
        {
            return new TrustedDeviceRevocationResult(TrustedDeviceRevocationStatus.Disabled, false);
        }

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await AdminRowLock.LockUserAsync(_context, userId, cancellationToken);
            await AdminRowLock.LockDevicesAsync(_context, userId, cancellationToken);
            DetachAll();
            var now = _time.GetUtcNow();
            var devices = await _context.TrustedDevices
                .Where(device => device.AdminUserId == userId && device.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var device in devices)
            {
                device.RevokedAt = now;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            SecurityEvents.Write(_logger, LogLevel.Information, "trusted_device_revoked", userId);
            return new TrustedDeviceRevocationResult(TrustedDeviceRevocationStatus.Revoked, true);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            SecurityEvents.Database(_logger, exception, userId);
            DetachAll();
            return new TrustedDeviceRevocationResult(TrustedDeviceRevocationStatus.Unavailable, false);
        }
    }

    private async Task<AuthResult> ActivateAsync(
        Guid adminUserId,
        string recipientEmail,
        LoginEmailChallenge challenge,
        string challengeToken,
        string code,
        bool clearTrustedDeviceCookie,
        CancellationToken cancellationToken)
    {
        var sent = await _email.SendCodeAsync(adminUserId, recipientEmail, code, challenge.ExpiresAt, cancellationToken);
        if (!sent)
        {
            SecurityEvents.Write(_logger, LogLevel.Warning, "login_code_send_failed", adminUserId);
            return new AuthRejected(clearTrustedDeviceCookie);
        }

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await AdminRowLock.LockUserAsync(_context, adminUserId, cancellationToken);
            await AdminRowLock.LockChallengeAsync(_context, challenge.Id, cancellationToken);
            DetachAll();
            var currentUser = await _context.AdminUsers.FirstAsync(admin => admin.Id == adminUserId, cancellationToken);
            var current = await _context.LoginEmailChallenges.FirstAsync(item => item.Id == challenge.Id, cancellationToken);
            if (current.SentAt is not null || current.ConsumedAt is not null || current.TokenVersion != currentUser.TokenVersion)
            {
                SecurityEvents.Write(_logger, LogLevel.Warning, "login_code_send_failed", adminUserId);
                return new AuthRejected(clearTrustedDeviceCookie);
            }

            current.SentAt = _time.GetUtcNow();
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            SecurityEvents.Write(_logger, LogLevel.Information, "login_code_send_succeeded", adminUserId);
            return new AuthVerificationRequired(challengeToken, current.ExpiresAt, clearTrustedDeviceCookie);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            SecurityEvents.Database(_logger, exception, adminUserId);
            DetachAll();
            return new AuthRejected(clearTrustedDeviceCookie);
        }
    }

    private async Task<AuthSuccess> IssueSessionAsync(
        AdminUser user,
        DateTimeOffset now,
        string? trustedDeviceToken,
        DateTimeOffset? trustedDeviceExpiresAt,
        bool clearTrustedDeviceCookie,
        CancellationToken cancellationToken)
    {
        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        var rawRefreshToken = RefreshTokenProtector.Generate();
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AdminUserId = user.Id,
            TokenHash = RefreshTokenProtector.Hash(rawRefreshToken),
            ExpiresAt = now.Add(_policy.RefreshTokenLifetime),
            CreatedAt = now
        };
        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync(cancellationToken);
        var accessToken = _accessTokens.Issue(user);
        await _context.Database.CommitTransactionAsync(cancellationToken);
        return new AuthSuccess(
            accessToken.Token,
            accessToken.ExpiresAt,
            rawRefreshToken,
            refreshToken.ExpiresAt,
            trustedDeviceToken,
            trustedDeviceExpiresAt,
            clearTrustedDeviceCookie);
    }

    private async Task<TrustedDevice?> FindDeviceAsync(string? trustedDeviceToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trustedDeviceToken))
        {
            return null;
        }

        var hash = RefreshTokenProtector.Hash(trustedDeviceToken);
        return await _context.TrustedDevices.FirstOrDefaultAsync(device => device.TokenHash == hash, cancellationToken);
    }

    private bool DeviceUsable(TrustedDevice device, AdminUser user, DateTimeOffset now) =>
        device.RevokedAt is null && device.ExpiresAt > now && device.TokenVersion == user.TokenVersion;

    private async Task<bool> IsOtpLockedAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var lockout = await ReadLockoutAsync(userId, cancellationToken);
        return lockout is not null && lockout > now;
    }

    private async Task<DateTimeOffset?> ReadLockoutAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.LoginEmailQuotas.AsNoTracking()
            .Where(quota => quota.AdminUserId == userId)
            .Select(quota => quota.OtpLockoutEnd)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<SendReservation> ReserveSendAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - _policy.SendWindow;
        var cooldownCutoff = now - _policy.MinSendInterval;
        var max = _policy.MaxSendsPerAccount;
        var rows = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "LoginEmailQuotas" AS q
                ("AdminUserId", "SendCount", "SendWindowStart", "LastSendAt", "FailedOtpCount", "OtpFailureWindowStart", "OtpLockoutEnd")
            VALUES ({userId}, 1, {now}, {now}, 0, NULL, NULL)
            ON CONFLICT ("AdminUserId") DO UPDATE
            SET "SendCount" = CASE WHEN q."SendWindowStart" <= {cutoff} THEN 1 ELSE q."SendCount" + 1 END,
                "SendWindowStart" = CASE WHEN q."SendWindowStart" <= {cutoff} THEN {now} ELSE q."SendWindowStart" END,
                "LastSendAt" = {now}
            WHERE (q."OtpLockoutEnd" IS NULL OR q."OtpLockoutEnd" <= {now})
              AND (q."LastSendAt" IS NULL OR q."LastSendAt" <= {cooldownCutoff})
              AND (q."SendWindowStart" <= {cutoff} OR q."SendCount" < {max})
            """,
            cancellationToken);
        if (rows > 0)
        {
            return SendReservation.Reserved;
        }

        if (await IsOtpLockedAsync(userId, now, cancellationToken))
        {
            return SendReservation.Locked;
        }

        return SendReservation.Limited;
    }

    private async Task<bool> RegisterOtpFailureAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - _policy.OtpFailureWindow;
        var lockoutEnd = now.Add(_policy.OtpLockoutDuration);
        var max = _policy.MaxFailedCodesPerAccount;
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "LoginEmailQuotas" AS q
                ("AdminUserId", "SendCount", "SendWindowStart", "LastSendAt", "FailedOtpCount", "OtpFailureWindowStart", "OtpLockoutEnd")
            VALUES ({userId}, 0, {now}, NULL, 1, {now}, CASE WHEN 1 >= {max} THEN {lockoutEnd} ELSE NULL END)
            ON CONFLICT ("AdminUserId") DO UPDATE
            SET "FailedOtpCount" = CASE
                    WHEN q."OtpFailureWindowStart" IS NULL OR q."OtpFailureWindowStart" <= {cutoff} THEN 1
                    ELSE q."FailedOtpCount" + 1
                END,
                "OtpFailureWindowStart" = CASE
                    WHEN q."OtpFailureWindowStart" IS NULL OR q."OtpFailureWindowStart" <= {cutoff} THEN {now}
                    ELSE q."OtpFailureWindowStart"
                END,
                "OtpLockoutEnd" = CASE
                    WHEN (
                        CASE
                            WHEN q."OtpFailureWindowStart" IS NULL OR q."OtpFailureWindowStart" <= {cutoff} THEN 1
                            ELSE q."FailedOtpCount" + 1
                        END
                    ) >= {max} THEN {lockoutEnd}
                    ELSE q."OtpLockoutEnd"
                END
            """,
            cancellationToken);
        return await IsOtpLockedAsync(userId, now, cancellationToken);
    }

    private static (LoginEmailChallenge Challenge, string Token) CreateChallenge(
        AdminUser user,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        var token = RefreshTokenProtector.Generate();
        return (new LoginEmailChallenge
        {
            Id = Guid.NewGuid(),
            AdminUserId = user.Id,
            TokenVersion = user.TokenVersion,
            TokenHash = RefreshTokenProtector.Hash(token),
            ExpiresAt = now.Add(lifetime),
            SendCount = 1,
            CreatedAt = now
        }, token);
    }

    private bool CanAcceptCode(LoginEmailChallenge challenge, AdminUser user, DateTimeOffset now) =>
        challenge.AdminUserId == user.Id
        && challenge.TokenVersion == user.TokenVersion
        && challenge.SentAt is not null
        && challenge.ConsumedAt is null
        && challenge.ExpiresAt > now
        && challenge.AttemptCount < _policy.MaxCodeAttemptsPerChallenge;

    private async Task<LocatedChallenge?> LocateAsync(string challengeToken, CancellationToken cancellationToken)
    {
        var tokenHash = RefreshTokenProtector.Hash(challengeToken);
        return await _context.LoginEmailChallenges.AsNoTracking()
            .Where(challenge => challenge.TokenHash == tokenHash)
            .Select(challenge => new LocatedChallenge(challenge.Id, challenge.AdminUserId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private bool PasswordStillMatches(AdminUser user, string password) =>
        _passwords.Verify(password, user.PasswordHash ?? _passwords.DummyHash);

    private static bool IsPasswordLocked(AdminUser user, DateTimeOffset now) =>
        user.LockoutEnd is not null && user.LockoutEnd > now;

    private bool PepperReady(Guid? adminUserId)
    {
        if (_options.HasSeparateLoginCodePepper())
        {
            return true;
        }

        SecurityEvents.Write(_logger, LogLevel.Error, "login_verification_configuration_error", adminUserId);
        return false;
    }

    private string Pepper() => _options.LoginCodePepper.Trim();

    private void LogSendBlocked(Guid adminUserId, SendReservation reservation)
    {
        if (reservation == SendReservation.Locked)
        {
            SecurityEvents.Write(_logger, LogLevel.Warning, "login_otp_lockout", adminUserId);
            return;
        }

        SecurityEvents.Write(_logger, LogLevel.Information, "login_code_send_limited", adminUserId);
    }

    private void DetachAll() => _context.ChangeTracker.Clear();

    private void DetachQuotas()
    {
        foreach (var entry in _context.ChangeTracker.Entries<LoginEmailQuota>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static string? SanitizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var cleaned = new string(label.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            return null;
        }

        return cleaned.Length <= 200 ? cleaned : cleaned[..200];
    }

    private static bool IsDatabaseFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException or NpgsqlException or DbUpdateException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    private enum SendReservation
    {
        Reserved,
        Locked,
        Limited
    }

    private sealed record LocatedChallenge(Guid Id, Guid AdminUserId);
}
