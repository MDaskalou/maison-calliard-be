using MaisonCalliard.Api.Auth;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Application.Auth.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MaisonCalliard.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    public const string RefreshCookieName = AuthCookies.RefreshCookieName;
    public const string RefreshCookiePath = AuthCookies.CookiePath;
    public const string InvalidCredentialsMessage = "Invalid credentials.";

    private readonly IAuthService _authService;
    private readonly AuthPolicy _policy;
    private readonly FrontendOriginPolicy _origins;

    public AuthController(IAuthService authService, AuthPolicy policy, FrontendOriginPolicy origins)
    {
        _authService = authService;
        _policy = policy;
        _origins = origins;
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (_policy.LoginEmailVerification && !HasCookieCsrf())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = CookieCsrfFilter.ForbiddenMessage });
        }

        Request.Cookies.TryGetValue(AuthCookies.TrustedDeviceCookieName, out var trustedDevice);
        var result = await _authService.LoginAsync(request, _policy.LoginEmailVerification ? trustedDevice : null, cancellationToken);
        return ToAuthResult(result);
    }

    [AllowAnonymous]
    [RequireCookieCsrf]
    [EnableRateLimiting("auth")]
    [HttpPost("login/verify")]
    public async Task<IActionResult> VerifyLoginCode([FromBody] VerifyLoginCodeRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.VerifyEmailCodeAsync(request, Request.Headers.UserAgent.ToString(), cancellationToken);
        return ToAuthResult(result);
    }

    [AllowAnonymous]
    [RequireCookieCsrf]
    [EnableRateLimiting("auth")]
    [HttpPost("login/resend")]
    public async Task<IActionResult> ResendLoginCode([FromBody] ResendLoginCodeRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.ResendEmailCodeAsync(request, cancellationToken);
        if (result is not ResendLoginCodeSent sent)
        {
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        return Ok(new { expiresAt = sent.ExpiresAt });
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("google")]
    public async Task<IActionResult> Google([FromBody] GoogleLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginWithGoogleAsync(request, cancellationToken);
        return ToAuthResult(result);
    }

    [AllowAnonymous]
    [RequireCookieCsrf]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken))
        {
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        var result = await _authService.RefreshAsync(refreshToken, cancellationToken);
        return ToAuthResult(result);
    }

    [AllowAnonymous]
    [RequireCookieCsrf]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken);
        await _authService.LogoutAsync(refreshToken, cancellationToken);
        ClearRefreshCookie();
        return NoContent();
    }

    private IActionResult ToAuthResult(AuthResult result)
    {
        if (result is AuthVerificationRequired required)
        {
            if (required.ClearTrustedDeviceCookie)
            {
                AuthCookies.ExpireTrustedDevice(Response);
            }

            return Ok(new
            {
                status = "emailVerificationRequired",
                challengeToken = required.ChallengeToken,
                expiresAt = required.ExpiresAt
            });
        }

        if (result is not AuthSuccess success)
        {
            if (result is AuthRejected rejected && rejected.ClearTrustedDeviceCookie)
            {
                AuthCookies.ExpireTrustedDevice(Response);
            }

            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        if (success.ClearTrustedDeviceCookie)
        {
            AuthCookies.ExpireTrustedDevice(Response);
        }

        Response.Cookies.Append(RefreshCookieName, success.RefreshToken, CreateRefreshCookieOptions(success.RefreshTokenExpiresAt, partitioned: true));
        if (success.TrustedDeviceToken is not null && success.TrustedDeviceExpiresAt is DateTimeOffset deviceExpires)
        {
            AuthCookies.AppendTrustedDevice(Response, success.TrustedDeviceToken, deviceExpires);
        }

        return Ok(new AuthResponse
        {
            AccessToken = success.AccessToken,
            ExpiresAt = success.AccessTokenExpiresAt
        });
    }

    private void ClearRefreshCookie()
    {
        // Same name and path share one slot in the cookie collection, so the two
        // deletions are written directly. One removes the partitioned cookie and
        // the other removes a cookie issued before Partitioned existed.
        Response.Headers.Append(Microsoft.Net.Http.Headers.HeaderNames.SetCookie, FormatExpiredRefreshCookie(partitioned: true));
        Response.Headers.Append(Microsoft.Net.Http.Headers.HeaderNames.SetCookie, FormatExpiredRefreshCookie(partitioned: false));
    }

    private static string FormatExpiredRefreshCookie(bool partitioned)
    {
        var cookie = new Microsoft.Net.Http.Headers.SetCookieHeaderValue(RefreshCookieName, string.Empty)
        {
            Expires = DateTimeOffset.UnixEpoch,
            Path = RefreshCookiePath,
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

    private static CookieOptions CreateRefreshCookieOptions(DateTimeOffset expiresAt, bool partitioned)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = RefreshCookiePath,
            Expires = expiresAt
        };
        if (partitioned)
        {
            options.Extensions.Add("Partitioned");
        }

        return options;
    }

    private bool HasCookieCsrf()
    {
        var csrfHeader = Request.Headers[CookieCsrfFilter.HeaderName].ToString();
        return string.Equals(csrfHeader, CookieCsrfFilter.ExpectedHeaderValue, StringComparison.Ordinal)
            && _origins.IsAllowedBrowserOrigin(Request.Headers.Origin.ToString());
    }
}
