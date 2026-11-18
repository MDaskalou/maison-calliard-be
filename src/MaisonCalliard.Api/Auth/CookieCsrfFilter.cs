using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MaisonCalliard.Api.Auth;

public sealed class CookieCsrfFilter : IAsyncActionFilter
{
    public const string HeaderName = "X-CSRF";
    public const string ExpectedHeaderValue = "1";
    public const string ForbiddenMessage = "Cross-site request blocked.";

    private readonly FrontendOriginPolicy _origins;

    public CookieCsrfFilter(FrontendOriginPolicy origins)
    {
        _origins = origins;
    }

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var csrfHeader = request.Headers[HeaderName].ToString();
        if (!string.Equals(csrfHeader, ExpectedHeaderValue, StringComparison.Ordinal)
            || !_origins.IsAllowedBrowserOrigin(request.Headers.Origin.ToString()))
        {
            context.Result = new ObjectResult(new { message = ForbiddenMessage })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return Task.CompletedTask;
        }

        return next();
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireCookieCsrfAttribute() : TypeFilterAttribute(typeof(CookieCsrfFilter));
