namespace MaisonCalliard.Api.Auth;

public static class AuthCookies
{
    public const string RefreshCookieName = "mc_refresh";
    public const string TrustedDeviceCookieName = "mc_trusted_device";
    public const string CookiePath = "/api/auth";

    public static void AppendTrustedDevice(HttpResponse response, string token, DateTimeOffset expiresAt)
    {
        response.Cookies.Append(TrustedDeviceCookieName, token, CreateOptions(expiresAt));
    }

    public static void ExpireTrustedDevice(HttpResponse response)
    {
        response.Headers.Append(Microsoft.Net.Http.Headers.HeaderNames.SetCookie, FormatExpired(partitioned: true));
        response.Headers.Append(Microsoft.Net.Http.Headers.HeaderNames.SetCookie, FormatExpired(partitioned: false));
    }

    private static CookieOptions CreateOptions(DateTimeOffset expiresAt)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = CookiePath,
            Expires = expiresAt
        };
        options.Extensions.Add("Partitioned");
        return options;
    }

    private static string FormatExpired(bool partitioned)
    {
        var cookie = new Microsoft.Net.Http.Headers.SetCookieHeaderValue(TrustedDeviceCookieName, string.Empty)
        {
            Expires = DateTimeOffset.UnixEpoch,
            Path = CookiePath,
            Secure = true,
            HttpOnly = true,
            SameSite = Microsoft.Net.Http.Headers.SameSiteMode.None
        };
        if (partitioned)
        {
            cookie.Extensions.Add("Partitioned");
        }

        return cookie.ToString();
    }
}
