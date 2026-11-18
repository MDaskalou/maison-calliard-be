using MaisonCalliard.Application.Auth.Dtos;

namespace MaisonCalliard.Application.Auth;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(
        LoginRequest request,
        string? trustedDeviceToken = null,
        CancellationToken cancellationToken = default);

    Task<AuthResult> VerifyEmailCodeAsync(
        VerifyLoginCodeRequest request,
        string? deviceLabel,
        CancellationToken cancellationToken = default);

    Task<ResendLoginCodeResult> ResendEmailCodeAsync(
        ResendLoginCodeRequest request,
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

    Task<AuthResult> LoginWithGoogleAsync(GoogleLoginRequest request, CancellationToken cancellationToken = default);

    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);

    Task<AdminProfileResponse?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<PasswordChangeResult> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}
