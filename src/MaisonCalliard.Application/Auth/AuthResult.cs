namespace MaisonCalliard.Application.Auth;

public abstract record AuthResult;

public sealed record AuthSuccess(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    string? TrustedDeviceToken = null,
    DateTimeOffset? TrustedDeviceExpiresAt = null,
    bool ClearTrustedDeviceCookie = false) : AuthResult;

public sealed record AuthRejected(bool ClearTrustedDeviceCookie = false) : AuthResult;
