namespace MaisonCalliard.Application.Auth;

public sealed record AuthVerificationRequired(
    string ChallengeToken,
    DateTimeOffset ExpiresAt,
    bool ClearTrustedDeviceCookie = false) : AuthResult;

public abstract record ResendLoginCodeResult;

public sealed record ResendLoginCodeSent(DateTimeOffset ExpiresAt) : ResendLoginCodeResult;

public sealed record ResendLoginCodeRejected : ResendLoginCodeResult;

public sealed record TrustedDeviceSummary(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    string? Label);

public enum TrustedDeviceListStatus
{
    Ok,
    Disabled,
    Unavailable
}

public sealed record TrustedDeviceListResult(
    TrustedDeviceListStatus Status,
    IReadOnlyList<TrustedDeviceSummary> Devices);

public enum TrustedDeviceRevocationStatus
{
    Revoked,
    NotFound,
    Disabled,
    Unavailable
}

public sealed record TrustedDeviceRevocationResult(
    TrustedDeviceRevocationStatus Status,
    bool ClearTrustedDeviceCookie);
