using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Google.Apis.Auth;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed class GoogleAccountVerifier : IGoogleAccountVerifier
{
    public const string HttpClientName = "GoogleOAuth";

    private readonly HttpClient _httpClient;
    private readonly GoogleAuthOptions _options;
    private readonly ILogger<GoogleAccountVerifier> _logger;

    public GoogleAccountVerifier(
        IHttpClientFactory httpClientFactory,
        IOptions<GoogleAuthOptions> options,
        ILogger<GoogleAccountVerifier> logger)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GoogleAccount?> VerifyAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            _logger.LogWarning("Google sign-in is not configured.");
            return null;
        }

        if (!IsAllowedRedirect(redirectUri))
        {
            _logger.LogWarning("Rejected Google sign-in for a redirect URI that is not allowed.");
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = codeVerifier
            })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Google token exchange failed with status {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload?.IdToken))
        {
            return null;
        }

        try
        {
            var validated = await GoogleJsonWebSignature.ValidateAsync(
                payload.IdToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [_options.ClientId]
                });

            return new GoogleAccount(validated.Subject, validated.Email ?? string.Empty, validated.EmailVerified);
        }
        catch (InvalidJwtException exception)
        {
            _logger.LogWarning(exception, "Google id token was rejected.");
            return null;
        }
    }

    private bool IsAllowedRedirect(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var candidate = redirectUri.Trim().TrimEnd('/');
        return _options.AllowedRedirectUris.Any(allowed =>
            string.Equals(allowed.Trim().TrimEnd('/'), candidate, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }
    }
}
