namespace MaisonCalliard.Domain.Entities;

public sealed class AdminUser
{
    public const string AdminRole = "admin";

    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string? GoogleSubject { get; set; }
    public string Role { get; set; } = AdminRole;
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public bool MustChangePassword { get; set; }
    public int TokenVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
}
