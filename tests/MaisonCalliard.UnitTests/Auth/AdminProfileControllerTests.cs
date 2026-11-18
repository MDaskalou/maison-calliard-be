using System.Security.Claims;
using FluentAssertions;
using MaisonCalliard.Api.Auth;
using MaisonCalliard.Api.Controllers;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Application.Auth.Dtos;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class AdminProfileControllerTests
{
    private readonly Mock<IAuthService> _authService = new();
    private readonly DefaultHttpContext _httpContext = new();
    private readonly AdminProfileController _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public AdminProfileControllerTests()
    {
        _sut = new AdminProfileController(
            _authService.Object,
            Options.Create(new AuthOptions { Issuer = "MaisonCalliard" }),
            new AuthPolicy())
        {
            ControllerContext = new ControllerContext { HttpContext = _httpContext }
        };
    }

    [Fact]
    public async Task GetProfile_WhenTheCallerIsOurAdmin_ReturnsTheProfileWithoutAPasswordHash()
    {
        SignIn("MaisonCalliard");
        var profile = new AdminProfileResponse
        {
            Id = _userId,
            Email = "admin@example.com",
            Role = "admin",
            MustChangePassword = true
        };
        _authService.Setup(service => service.GetProfileAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(profile);

        var result = await _sut.GetProfile(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(profile);
        ok.Value!.GetType().GetProperties().Select(property => property.Name)
            .Should().NotContain("PasswordHash");
    }

    [Fact]
    public async Task GetProfile_WhenTheCallerIsUnauthenticated_Returns401()
    {
        var result = await _sut.GetProfile(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _authService.Verify(service => service.GetProfileAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetProfile_WhenTheTokenWasIssuedByLogto_Returns401()
    {
        SignIn("https://k7ae42.logto.app/oidc");

        var result = await _sut.GetProfile(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _authService.Verify(service => service.GetProfileAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangePassword_WhenTheCurrentPasswordIsWrong_Returns401()
    {
        SignIn("MaisonCalliard");
        _authService
            .Setup(service => service.ChangePasswordAsync(_userId, It.IsAny<ChangePasswordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordChangeResult(PasswordChangeStatus.InvalidCurrentPassword));

        var result = await _sut.ChangePassword(new ChangePasswordRequest(), CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task ChangePassword_WhenTwoChangesConflict_Returns409()
    {
        SignIn("MaisonCalliard");
        _authService
            .Setup(service => service.ChangePasswordAsync(_userId, It.IsAny<ChangePasswordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordChangeResult(PasswordChangeStatus.Conflict));

        var result = await _sut.ChangePassword(new ChangePasswordRequest(), CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task ChangePassword_WhenItSucceeds_ExpiresTheTrustedDeviceCookie()
    {
        SignIn("MaisonCalliard");
        _authService
            .Setup(service => service.ChangePasswordAsync(_userId, It.IsAny<ChangePasswordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordChangeResult(PasswordChangeStatus.Success));

        var result = await _sut.ChangePassword(new ChangePasswordRequest(), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        var cookies = _httpContext.Response.Headers.SetCookie.ToArray();
        cookies.Should().HaveCount(2);
        cookies.Should().OnlyContain(cookie =>
            cookie.Contains(AuthCookies.TrustedDeviceCookieName, StringComparison.Ordinal)
            && cookie.Contains("path=/api/auth", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("samesite=none", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        cookies.Count(cookie => cookie.Contains("Partitioned", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task ListTrustedDevices_WhenTheCallerIsNotOurIssuer_DoesNotReadDevices()
    {
        var sut = EnabledController();
        SignIn("https://logto.example");

        var result = await sut.ListTrustedDevices(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _authService.Verify(
            service => service.ListTrustedDevicesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RevokeAllTrustedDevices_WhenRevocationSucceeds_ExpiresTheTrustedDeviceCookie()
    {
        var sut = EnabledController();
        SignIn("MaisonCalliard");
        _authService
            .Setup(service => service.RevokeAllTrustedDevicesAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrustedDeviceRevocationResult(TrustedDeviceRevocationStatus.Revoked, true));

        var result = await sut.RevokeAllTrustedDevices(CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        var cookies = _httpContext.Response.Headers.SetCookie.ToArray();
        cookies.Should().HaveCount(2);
        cookies.Should().OnlyContain(cookie =>
            cookie.Contains(AuthCookies.TrustedDeviceCookieName, StringComparison.Ordinal)
            && cookie.Contains("path=/api/auth", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("samesite=none", StringComparison.OrdinalIgnoreCase));
        cookies.Count(cookie => cookie.Contains("Partitioned", StringComparison.Ordinal)).Should().Be(1);
    }

    private AdminProfileController EnabledController() =>
        new(
            _authService.Object,
            Options.Create(new AuthOptions { Issuer = "MaisonCalliard" }),
            new AuthPolicy { LoginEmailVerification = true })
        {
            ControllerContext = new ControllerContext { HttpContext = _httpContext }
        };

    private void SignIn(string issuer)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim("iss", issuer),
                new Claim("sub", _userId.ToString()),
                new Claim("roles", "admin"),
                new Claim("token_version", "0")
            ],
            authenticationType: "Bearer");
        _httpContext.User = new ClaimsPrincipal(identity);
    }
}
