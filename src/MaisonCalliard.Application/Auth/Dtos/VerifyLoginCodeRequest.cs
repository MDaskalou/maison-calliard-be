namespace MaisonCalliard.Application.Auth.Dtos;

public sealed class VerifyLoginCodeRequest
{
    public string ChallengeToken { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public bool RememberDevice { get; set; }
}
