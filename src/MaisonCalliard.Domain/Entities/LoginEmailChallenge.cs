namespace MaisonCalliard.Domain.Entities;

public sealed class LoginEmailChallenge
{
    public Guid Id { get; set; }

    public Guid AdminUserId { get; set; }

    public int TokenVersion { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public string CodeHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public int AttemptCount { get; set; }

    public int SendCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
