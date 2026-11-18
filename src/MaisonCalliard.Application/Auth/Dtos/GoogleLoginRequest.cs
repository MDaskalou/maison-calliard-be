namespace MaisonCalliard.Application.Auth.Dtos;

public sealed class GoogleLoginRequest
{
    public string Code { get; set; } = string.Empty;

    public string CodeVerifier { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;
}
