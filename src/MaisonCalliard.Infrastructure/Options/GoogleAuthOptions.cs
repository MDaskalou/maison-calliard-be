using Microsoft.Extensions.Configuration;

namespace MaisonCalliard.Infrastructure.Options;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "Google";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string[] AllowedRedirectUris { get; set; } = [];

    public void ApplyEnvironmentOverrides(IConfiguration configuration)
    {
        var clientId = configuration["GOOGLE_CLIENT_ID"];
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            ClientId = clientId;
        }

        var clientSecret = configuration["GOOGLE_CLIENT_SECRET"];
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            ClientSecret = clientSecret;
        }
    }
}
