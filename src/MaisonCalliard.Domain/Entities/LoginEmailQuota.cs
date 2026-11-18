namespace MaisonCalliard.Domain.Entities;

public sealed class LoginEmailQuota
{
    public Guid AdminUserId { get; set; }

    public int SendCount { get; set; }

    public DateTimeOffset SendWindowStart { get; set; }

    public DateTimeOffset? LastSendAt { get; set; }

    public int FailedOtpCount { get; set; }

    public DateTimeOffset? OtpFailureWindowStart { get; set; }

    public DateTimeOffset? OtpLockoutEnd { get; set; }
}
