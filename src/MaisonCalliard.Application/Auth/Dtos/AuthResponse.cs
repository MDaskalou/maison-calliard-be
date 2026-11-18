namespace MaisonCalliard.Application.Auth.Dtos;

public sealed class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
}
