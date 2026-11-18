namespace MaisonCalliard.Application.Auth.Dtos;

public sealed class AdminProfileResponse
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public bool MustChangePassword { get; set; }
}
