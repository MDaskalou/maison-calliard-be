namespace MaisonCalliard.Application.Auth;

public interface IGoogleAccountVerifier
{
    Task<GoogleAccount?> VerifyAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken = default);
}

public sealed record GoogleAccount(string Subject, string Email, bool EmailVerified);
