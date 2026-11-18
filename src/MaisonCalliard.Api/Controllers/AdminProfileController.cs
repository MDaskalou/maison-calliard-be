using MaisonCalliard.Api.Auth;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Application.Auth.Dtos;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin")]
public sealed class AdminProfileController : ControllerBase
{
    public const string InvalidCredentialsMessage = "Invalid credentials.";
    public const string PasswordMismatchMessage = "New password and confirmation do not match.";
    public const string WeakPasswordMessage = "New password must be at least 12 characters.";
    public const string ReusedPasswordMessage = "New password must be different from the current password.";
    public const string InvalidCurrentPasswordMessage = "Current password is incorrect.";
    public const string ConflictMessage = "Password change could not be completed. Try again.";

    private readonly IAuthService _authService;
    private readonly AuthOptions _options;
    private readonly AuthPolicy _policy;

    public AdminProfileController(IAuthService authService, IOptions<AuthOptions> options, AuthPolicy policy)
    {
        _authService = authService;
        _options = options.Value;
        _policy = policy;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        if (!AdminAccessMiddleware.IsOurIssuer(User, _options.Issuer) || GetUserId() is not Guid userId)
        {
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        var profile = await _authService.GetProfileAsync(userId, cancellationToken);
        return profile is null ? Unauthorized(new { message = InvalidCredentialsMessage }) : Ok(profile);
    }

    [EnableRateLimiting("auth")]
    [HttpPost("me/change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (!AdminAccessMiddleware.IsOurIssuer(User, _options.Issuer) || GetUserId() is not Guid userId)
        {
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        var result = await _authService.ChangePasswordAsync(userId, request, cancellationToken);
        if (result.Status == PasswordChangeStatus.Success)
        {
            AuthCookies.ExpireTrustedDevice(Response);
            return NoContent();
        }

        return result.Status switch
        {
            PasswordChangeStatus.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ConflictMessage }),
            PasswordChangeStatus.Mismatch => BadRequest(new { message = PasswordMismatchMessage }),
            PasswordChangeStatus.Weak => BadRequest(new { message = WeakPasswordMessage }),
            PasswordChangeStatus.Reused => BadRequest(new { message = ReusedPasswordMessage }),
            PasswordChangeStatus.Conflict => Conflict(new { message = ConflictMessage }),
            PasswordChangeStatus.NotAllowed => Unauthorized(new { message = InvalidCredentialsMessage }),
            _ => Unauthorized(new { message = InvalidCurrentPasswordMessage })
        };
    }

    [HttpGet("me/trusted-devices")]
    public async Task<IActionResult> ListTrustedDevices(CancellationToken cancellationToken)
    {
        if (!CanManageDevices())
        {
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        var result = await _authService.ListTrustedDevicesAsync(GetUserId()!.Value, cancellationToken);
        return result.Status switch
        {
            TrustedDeviceListStatus.Ok => Ok(result.Devices),
            TrustedDeviceListStatus.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ConflictMessage }),
            _ => NotFound()
        };
    }

    [EnableRateLimiting("auth")]
    [HttpPost("me/trusted-devices/{deviceId:guid}/revoke")]
    public Task<IActionResult> RevokeTrustedDevice(Guid deviceId, CancellationToken cancellationToken) =>
        RevokeAsync(userId => _authService.RevokeTrustedDeviceAsync(userId, deviceId, cancellationToken), cancellationToken);

    [EnableRateLimiting("auth")]
    [HttpPost("me/trusted-devices/revoke-all")]
    public Task<IActionResult> RevokeAllTrustedDevices(CancellationToken cancellationToken) =>
        RevokeAsync(userId => _authService.RevokeAllTrustedDevicesAsync(userId, cancellationToken), cancellationToken);

    private async Task<IActionResult> RevokeAsync(
        Func<Guid, Task<TrustedDeviceRevocationResult>> revoke,
        CancellationToken cancellationToken)
    {
        if (!CanManageDevices() || GetUserId() is not Guid userId)
        {
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        var result = await revoke(userId);
        if (result.Status == TrustedDeviceRevocationStatus.Revoked)
        {
            if (result.ClearTrustedDeviceCookie)
            {
                AuthCookies.ExpireTrustedDevice(Response);
            }

            return NoContent();
        }

        return result.Status == TrustedDeviceRevocationStatus.Unavailable
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ConflictMessage })
            : NotFound();
    }

    private bool CanManageDevices() =>
        _policy.LoginEmailVerification
        && AdminAccessMiddleware.IsOurIssuer(User, _options.Issuer)
        && GetUserId() is not null;

    private Guid? GetUserId() =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) ? userId : null;
}
