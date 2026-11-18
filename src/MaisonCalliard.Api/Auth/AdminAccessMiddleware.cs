using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Infrastructure.Options;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.Api.Auth;

public sealed class AdminAccessMiddleware : IMiddleware
{
    public const string PasswordChangeRequiredMessage = "Password change required.";
    public const string InvalidCredentialsMessage = "Invalid credentials.";

    private readonly IAdminUserRepository _users;
    private readonly AuthOptions _options;

    public AdminAccessMiddleware(IAdminUserRepository users, IOptions<AuthOptions> options)
    {
        _users = users;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var issuer = context.User.FindFirst(AuthTokenValidation.IssuerClaim)?.Value;
        if (!string.Equals(issuer, _options.Issuer, StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId)
            || !int.TryParse(context.User.FindFirst(AuthTokenValidation.TokenVersionClaim)?.Value, out var tokenVersion))
        {
            await Reject(context, StatusCodes.Status401Unauthorized, InvalidCredentialsMessage);
            return;
        }

        var user = await _users.GetByIdAsync(userId, context.RequestAborted);
        if (user is null || !user.IsActive || user.TokenVersion != tokenVersion)
        {
            await Reject(context, StatusCodes.Status401Unauthorized, InvalidCredentialsMessage);
            return;
        }

        if (user.MustChangePassword && !IsAllowedWhilePasswordChangeIsRequired(context.Request))
        {
            await Reject(context, StatusCodes.Status403Forbidden, PasswordChangeRequiredMessage);
            return;
        }

        await next(context);
    }

    public static bool IsOurIssuer(System.Security.Claims.ClaimsPrincipal user, string issuer) =>
        string.Equals(user.FindFirst(AuthTokenValidation.IssuerClaim)?.Value, issuer, StringComparison.Ordinal);

    private static bool IsAllowedWhilePasswordChangeIsRequired(HttpRequest request)
    {
        var path = (request.Path.Value ?? string.Empty).TrimEnd('/');
        if (path.Equals("/api/admin/me", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsGet(request.Method))
        {
            return true;
        }

        if (path.Equals("/api/admin/me/change-password", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(request.Method))
        {
            return true;
        }

        return path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(request.Method);
    }

    private static Task Reject(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new { message });
    }
}
