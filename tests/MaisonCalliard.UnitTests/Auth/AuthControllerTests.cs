using FluentAssertions;
using MaisonCalliard.Api.Auth;
using MaisonCalliard.Api.Controllers;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Application.Auth.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class AuthControllerTests
{
    private readonly Mock<IAuthService> _authService = new();
    private readonly DefaultHttpContext _httpContext = new();
    private readonly AuthController _sut;

    public AuthControllerTests()
    {
        _sut = new AuthController(_authService.Object, new AuthPolicy(), CreateOrigins())
        {
            ControllerContext = new ControllerContext { HttpContext = _httpContext }
        };
    }

    [Fact]
    public async Task Login_WhenAccepted_ReturnsAccessTokenAndSetsRefreshCookie()
    {
        var expires = new DateTimeOffset(2026, 10, 21, 18, 15, 0, TimeSpan.Zero);
        var refreshExpires = expires.AddDays(14);
        _authService
            .Setup(service => service.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthSuccess("access-token", expires, "refresh-raw", refreshExpires));

        var result = await _sut.Login(new LoginRequest { Email = "admin@example.com", Password = "secret" }, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<AuthResponse>().Subject;
        body.AccessToken.Should().Be("access-token");
        body.ExpiresAt.Should().Be(expires);
        ok.Value.Should().NotBeAssignableTo<AuthSuccess>();
        var cookie = _httpContext.Response.Headers.SetCookie.ToString();
        cookie.Should().Contain($"{AuthController.RefreshCookieName}=refresh-raw");
        cookie.ToLowerInvariant().Should().Contain("httponly");
        cookie.ToLowerInvariant().Should().Contain("secure");
        cookie.ToLowerInvariant().Should().Contain("samesite=none");
        cookie.ToLowerInvariant().Should().Contain("partitioned");
        cookie.ToLowerInvariant().Should().Contain("path=/api/auth");
    }

    [Fact]
    public async Task Login_WhenRejected_Returns401WithoutCookie()
    {
        _authService
            .Setup(service => service.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthRejected());

        var result = await _sut.Login(new LoginRequest { Email = "admin@example.com", Password = "nope" }, CancellationToken.None);

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorized.Value.Should().BeEquivalentTo(new { message = AuthController.InvalidCredentialsMessage });
        _httpContext.Response.Headers.SetCookie.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_ReadsTheRefreshCookie()
    {
        var expires = new DateTimeOffset(2026, 10, 21, 18, 15, 0, TimeSpan.Zero);
        _httpContext.Request.Headers.Cookie = $"{AuthController.RefreshCookieName}=raw-token";
        _authService
            .Setup(service => service.RefreshAsync("raw-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthSuccess("next-access", expires, "next-refresh", expires.AddDays(14)));

        var result = await _sut.Refresh(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _httpContext.Response.Headers.SetCookie.ToString().Should().Contain("next-refresh");
        _authService.Verify(service => service.RefreshAsync("raw-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Logout_RevokesTheCookieTokenAndClearsIt()
    {
        _httpContext.Request.Headers.Cookie = $"{AuthController.RefreshCookieName}=raw-token";

        var result = await _sut.Logout(CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        _authService.Verify(service => service.LogoutAsync("raw-token", It.IsAny<CancellationToken>()), Times.Once);
        var cookies = _httpContext.Response.Headers.SetCookie.ToArray();
        cookies.Should().HaveCount(2);
        cookies.Should().OnlyContain(cookie =>
            cookie.Contains(AuthController.RefreshCookieName, StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("samesite=none", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("path=/api/auth", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        cookies.Count(cookie => cookie.Contains("Partitioned", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
    }

    private static FrontendOriginPolicy CreateOrigins()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "http://localhost:3031",
            ["Auth:AllowedBrowserOrigins:0"] = "http://localhost:3031"
        }).Build();
        return FrontendOriginPolicy.Create(configuration);
    }
}
