namespace MaisonCalliard.Domain.Entities;

public sealed class TrustedDevice
{
    public Guid Id { get; set; }

    public Guid AdminUserId { get; set; }

    public int TokenVersion { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public string? Label { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
