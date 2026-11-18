using Microsoft.Extensions.Configuration;

namespace MaisonCalliard.Infrastructure.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public const string PlaceholderSigningKey = "REPLACE_WITH_A_SECURE_SECRET_KEY_MIN_32_CHARS";

    public string Issuer { get; set; } = "MaisonCalliard";

    public string Audience { get; set; } = "MaisonCalliard";

    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 14;

    public int LoginLockoutThreshold { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    public string[] SeedEmails { get; set; } = [];

    public string? SeedPassword { get; set; }

    public SeedAdminCredential[] SeedAdmins { get; set; } = [];

    public bool LoginEmailVerification { get; set; }

    public bool DevCodeFile { get; set; }

    public string LoginCodePepper { get; set; } = string.Empty;

    public bool HasSeparateLoginCodePepper()
    {
        var pepper = LoginCodePepper?.Trim() ?? string.Empty;
        return pepper.Length >= 32
            && !string.Equals(pepper, SigningKey, StringComparison.Ordinal)
            && !string.Equals(pepper, PlaceholderSigningKey, StringComparison.Ordinal);
    }

    public static AuthOptions FromConfiguration(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<AuthOptions>() ?? new AuthOptions();
        options.ApplyEnvironmentOverrides(configuration);
        return options;
    }

    public void ApplyEnvironmentOverrides(IConfiguration configuration)
    {
        var signingKey = configuration["AUTH_SIGNING_KEY"];
        if (!string.IsNullOrWhiteSpace(signingKey))
        {
            SigningKey = signingKey;
        }
    }

    public void EnsureSigningKey()
    {
        if (string.IsNullOrWhiteSpace(SigningKey)
            || SigningKey.Length < 32
            || string.Equals(SigningKey, PlaceholderSigningKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Auth:SigningKey must be a random value of at least 32 characters, supplied via user secrets or the AUTH_SIGNING_KEY environment variable.");
        }
    }
}

public sealed class SeedAdminCredential
{
    public string Email { get; set; } = string.Empty;

    public string? Password { get; set; }
}
