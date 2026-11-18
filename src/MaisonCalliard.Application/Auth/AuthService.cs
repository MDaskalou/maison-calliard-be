using MaisonCalliard.Application.Auth.Dtos;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Repositories;

namespace MaisonCalliard.Application.Auth;

internal sealed class AuthService : IAuthService
{
    private readonly IAdminUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAccessTokenIssuer _accessTokens;
    private readonly IGoogleAccountVerifier _googleAccounts;
    private readonly IAdminCredentialStore _credentials;
    private readonly ILoginFlow _loginFlow;
    private readonly AuthPolicy _policy;
    private readonly TimeProvider _time;

    public AuthService(
        IAdminUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwordHasher,
        IAccessTokenIssuer accessTokens,
        IGoogleAccountVerifier googleAccounts,
        IAdminCredentialStore credentials,
        ILoginFlow loginFlow,
        AuthPolicy policy,
        TimeProvider time)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher;
        _accessTokens = accessTokens;
        _googleAccounts = googleAccounts;
        _credentials = credentials;
        _loginFlow = loginFlow;
        _policy = policy;
        _time = time;
    }

    public async Task<AuthResult> LoginAsync(
        LoginRequest request,
        string? trustedDeviceToken = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            _passwordHasher.Verify(request.Password ?? string.Empty, _passwordHasher.DummyHash);
            return new AuthRejected();
        }

        var user = await _users.GetByNormalizedEmailAsync(AdminUser.NormalizeEmail(request.Email), cancellationToken);
        if (user is null)
        {
            _passwordHasher.Verify(request.Password, _passwordHasher.DummyHash);
            return new AuthRejected();
        }

        var now = _time.GetUtcNow();
        var passwordMatches = _passwordHasher.Verify(request.Password, user.PasswordHash ?? _passwordHasher.DummyHash);
        if (!user.IsActive || IsLocked(user, now) || !passwordMatches)
        {
            if (user.IsActive && !IsLocked(user, now) && !passwordMatches)
            {
                await RegisterFailureAsync(user, now, cancellationToken);
            }

            return new AuthRejected();
        }

        return await _loginFlow.CompletePasswordLoginAsync(user.Id, request.Password, trustedDeviceToken, cancellationToken);
    }

    public Task<AuthResult> VerifyEmailCodeAsync(
        VerifyLoginCodeRequest request,
        string? deviceLabel,
        CancellationToken cancellationToken = default) =>
        _loginFlow.VerifyEmailCodeAsync(request.ChallengeToken, request.Code, request.RememberDevice, deviceLabel, cancellationToken);

    public Task<ResendLoginCodeResult> ResendEmailCodeAsync(
        ResendLoginCodeRequest request,
        CancellationToken cancellationToken = default) =>
        _loginFlow.ResendEmailCodeAsync(request.ChallengeToken, cancellationToken);

    public Task<TrustedDeviceListResult> ListTrustedDevicesAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _loginFlow.ListTrustedDevicesAsync(userId, cancellationToken);

    public Task<TrustedDeviceRevocationResult> RevokeTrustedDeviceAsync(
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken = default) =>
        _loginFlow.RevokeTrustedDeviceAsync(userId, deviceId, cancellationToken);

    public Task<TrustedDeviceRevocationResult> RevokeAllTrustedDevicesAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _loginFlow.RevokeAllTrustedDevicesAsync(userId, cancellationToken);

    public async Task<AuthResult> LoginWithGoogleAsync(GoogleLoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code)
            || string.IsNullOrWhiteSpace(request.CodeVerifier)
            || string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            return new AuthRejected();
        }

        var account = await _googleAccounts.VerifyAsync(
            request.Code,
            request.CodeVerifier,
            request.RedirectUri,
            cancellationToken);
        if (account is null || !account.EmailVerified || string.IsNullOrWhiteSpace(account.Email) || string.IsNullOrWhiteSpace(account.Subject))
        {
            return new AuthRejected();
        }

        var user = await _users.GetByNormalizedEmailAsync(AdminUser.NormalizeEmail(account.Email), cancellationToken);
        var now = _time.GetUtcNow();
        if (user is null || !user.IsActive || IsLocked(user, now))
        {
            return new AuthRejected();
        }

        if (user.GoogleSubject is not null && !string.Equals(user.GoogleSubject, account.Subject, StringComparison.Ordinal))
        {
            return new AuthRejected();
        }

        var subjectToLink = user.GoogleSubject is null ? account.Subject : null;
        if (user.GoogleSubject is null)
        {
            user.GoogleSubject = account.Subject;
        }

        return await _loginFlow.CompleteGoogleLoginAsync(user.Id, subjectToLink, cancellationToken);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var rotation = await _credentials.RotateAsync(refreshToken, cancellationToken);
        if (rotation is not SessionRotated rotated)
        {
            return new AuthRejected();
        }

        var accessToken = _accessTokens.Issue(rotated.User);
        return new AuthSuccess(accessToken.Token, accessToken.ExpiresAt, rotated.RefreshToken, rotated.ExpiresAt);
    }

    public async Task<AdminProfileResponse?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        return new AdminProfileResponse
        {
            Id = user.Id,
            Email = user.Email,
            Role = user.Role,
            MustChangePassword = user.MustChangePassword
        };
    }

    public Task<PasswordChangeResult> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return Task.FromResult(new PasswordChangeResult(PasswordChangeStatus.Mismatch));
        }

        if (!AdminPasswordPolicy.IsStrongEnough(request.NewPassword))
        {
            return Task.FromResult(new PasswordChangeResult(PasswordChangeStatus.Weak));
        }

        return _credentials.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword, cancellationToken);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var existing = await _refreshTokens.GetByHashAsync(RefreshTokenProtector.Hash(refreshToken), cancellationToken);
        if (existing is null || existing.RevokedAt is not null)
        {
            return;
        }

        existing.RevokedAt = _time.GetUtcNow();
        await _refreshTokens.UpdateAsync(existing, cancellationToken);
    }

    private async Task RegisterFailureAsync(AdminUser user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        user.FailedLoginCount++;
        if (user.FailedLoginCount >= _policy.LoginLockoutThreshold)
        {
            user.LockoutEnd = now.Add(_policy.LockoutDuration);
        }

        await _users.UpdateAsync(user, cancellationToken);
    }

    private static bool IsLocked(AdminUser user, DateTimeOffset now) =>
        user.LockoutEnd is not null && user.LockoutEnd > now;
}
