namespace MaisonCalliard.Api.Auth;

public sealed class FrontendOriginPolicy
{
    private readonly HashSet<string> _browserOrigins;

    private FrontendOriginPolicy(IReadOnlyCollection<string> corsOrigins, HashSet<string> browserOrigins)
    {
        CorsOrigins = corsOrigins;
        _browserOrigins = browserOrigins;
    }

    public IReadOnlyCollection<string> CorsOrigins { get; }

    public IReadOnlyCollection<string> BrowserOrigins => _browserOrigins;

    public static FrontendOriginPolicy Create(IConfiguration configuration)
    {
        var corsOrigins = ReadOrigins(
            configuration,
            "Cors:AllowedOrigins",
            "Cors:AllowedOriginsCsv",
            "Frontend:Url",
            "FrontendUrl");
        var browserOrigins = ReadOrigins(
            configuration,
            "Auth:AllowedBrowserOrigins",
            "Auth:AllowedBrowserOriginsCsv");

        if (corsOrigins.Any(origin => !IsExplicitOrigin(origin)))
        {
            throw new InvalidOperationException(
                "CORS origins must be explicit. Wildcard origins, including Netlify wildcards, are not allowed.");
        }

        var cors = corsOrigins.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var browser = browserOrigins.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var missingFromCors = browser.FirstOrDefault(origin => !cors.Contains(origin));
        if (missingFromCors is not null)
        {
            throw new InvalidOperationException(
                $"Auth:AllowedBrowserOrigins contains '{missingFromCors}', which must also be present in the CORS allowlist.");
        }

        return new FrontendOriginPolicy(cors, browser);
    }

    public bool IsAllowedBrowserOrigin(string? originHeader)
    {
        if (_browserOrigins.Count == 0
            || string.IsNullOrWhiteSpace(originHeader)
            || string.Equals(originHeader.Trim(), "null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return _browserOrigins.Contains(Normalize(originHeader));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsExplicitOrigin(value))
        {
            throw new InvalidOperationException(
                "Frontend origins must be explicit absolute http or https origins. Null and wildcard origins are not allowed.");
        }

        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.IdnHost)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath is not ("/" or "")
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"Frontend origin '{trimmed}' must contain only scheme, host, and port.");
        }

        var builder = new UriBuilder(uri.Scheme, uri.IdnHost, uri.IsDefaultPort ? -1 : uri.Port);
        return builder.Uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.SafeUnescaped);
    }

    private static bool IsExplicitOrigin(string value)
    {
        var trimmed = value.Trim();
        return !string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Contains('*', StringComparison.Ordinal);
    }

    private static List<string> ReadOrigins(IConfiguration configuration, params string[] keys)
    {
        var values = new List<string>();
        foreach (var key in keys)
        {
            var section = configuration.GetSection(key);
            if (section.GetChildren().Any())
            {
                values.AddRange(section.Get<string[]>() ?? []);
            }

            // A CSV environment variable is one string, not an array. Binding it as
            // string[] throws, so the scalar value is split on comma and semicolon.
            values.AddRange(Split(section.Value));
        }

        return values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
    }

    private static IEnumerable<string> Split(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
