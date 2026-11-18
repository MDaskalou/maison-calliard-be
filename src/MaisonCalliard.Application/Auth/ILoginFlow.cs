namespace MaisonCalliard.Application.Auth;

public interface ILoginFlow
{
    Task<AuthResult> CompletePasswordLoginAsync(
        Guid userId,
        string password,
        string? trustedDeviceToken,
        CancellationToken cancellationToken = default);

    Task<AuthResult> CompleteGoogleLoginAsync(
        Guid userId,
        string? googleSubjectToLink,
        CancellationToken cancellationToken = default);

    Task<AuthResult> VerifyEmailCodeAsync(
        string challengeToken,
        string code,
        bool rememberDevice,
        string? deviceLabel,
        CancellationToken cancellationToken = default);

    Task<ResendLoginCodeResult> ResendEmailCodeAsync(
        string challengeToken,
        CancellationToken cancellationToken = default);

    Task<TrustedDeviceListResult> ListTrustedDevicesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<TrustedDeviceRevocationResult> RevokeTrustedDeviceAsync(
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<TrustedDeviceRevocationResult> RevokeAllTrustedDevicesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
