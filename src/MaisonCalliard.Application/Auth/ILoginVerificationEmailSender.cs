namespace MaisonCalliard.Application.Auth;

public interface ILoginVerificationEmailSender
{
    Task<bool> SendCodeAsync(
        Guid adminUserId,
        string recipientEmail,
        string code,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}
