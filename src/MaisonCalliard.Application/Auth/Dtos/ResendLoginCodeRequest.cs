namespace MaisonCalliard.Application.Auth.Dtos;

public sealed class ResendLoginCodeRequest
{
    public string ChallengeToken { get; set; } = string.Empty;
}
