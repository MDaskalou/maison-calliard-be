namespace MaisonCalliard.Application.Auth;

public sealed class AuthPolicy
{
    public int LoginLockoutThreshold { get; init; } = 5;

    public TimeSpan LockoutDuration { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(14);

    public bool LoginEmailVerification { get; init; }

    public TimeSpan LoginCodeLifetime { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan TrustedDeviceLifetime { get; init; } = TimeSpan.FromDays(30);

    public int MaxCodeAttemptsPerChallenge { get; init; } = 5;

    public int MaxFailedCodesPerAccount { get; init; } = 10;

    public TimeSpan OtpFailureWindow { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan OtpLockoutDuration { get; init; } = TimeSpan.FromMinutes(15);

    public int MaxSendsPerAccount { get; init; } = 5;

    public TimeSpan SendWindow { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan MinSendInterval { get; init; } = TimeSpan.FromSeconds(60);

    public int MaxSendsPerChallenge { get; init; } = 3;
}
