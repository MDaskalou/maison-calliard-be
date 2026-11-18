using MaisonCalliard.Domain.Entities;

namespace MaisonCalliard.Application.Auth;

public enum PasswordChangeStatus
{
    Success,
    Mismatch,
    Weak,
    Reused,
    InvalidCurrentPassword,
    NotAllowed,
    Conflict,
    Unavailable
}

public sealed record PasswordChangeResult(PasswordChangeStatus Status);

public abstract record SessionRotationResult;

public sealed record SessionRotated(AdminUser User, string RefreshToken, DateTimeOffset ExpiresAt) : SessionRotationResult;

public sealed record SessionRotationRejected : SessionRotationResult;

public interface IAdminCredentialStore
{
    Task<PasswordChangeResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<SessionRotationResult> RotateAsync(string refreshToken, CancellationToken cancellationToken = default);
}
