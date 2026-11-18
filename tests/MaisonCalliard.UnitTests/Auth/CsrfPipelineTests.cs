using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MaisonCalliard.Api.Controllers;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Application.Auth.Dtos;
using MaisonCalliard.Application.Orders;
using MaisonCalliard.Application.Orders.Dtos;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class CsrfPipelineTests : IClassFixture<CsrfApiFactory>
{
    private const string AdminOrigin = "http://localhost:5173";
    private const string PublicOrigin = "https://www.maisoncalliard.com";

    private readonly CsrfApiFactory _factory;

    public CsrfPipelineTests(CsrfApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Refresh_WithTrustedOriginAndCsrfHeader_RotatesThePartitionedCookie()
    {
        _factory.Auth.Reset();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Origin", AdminOrigin);
        request.Headers.Add(CookieCsrfFilterHeader, "1");
        request.Headers.Add("Cookie", $"{AuthController.RefreshCookieName}=raw-token");

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Auth.RefreshCalls.Should().Be(1);
        var cookie = SetCookie(response).Single().ToLowerInvariant();
        cookie.Should().Contain("mc_refresh=next-refresh");
        cookie.Should().Contain("httponly");
        cookie.Should().Contain("secure");
        cookie.Should().Contain("samesite=none");
        cookie.Should().Contain("partitioned");
        cookie.Should().Contain("path=/api/auth");
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOrigin).Should().BeTrue();
        allowOrigin.Should().ContainSingle().Which.Should().Be(AdminOrigin);
        allowOrigin.Should().NotContain("*");
    }

    [Fact]
    public async Task Logout_ClearsBothThePartitionedCookieAndALegacyCookie()
    {
        _factory.Auth.Reset();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("Origin", AdminOrigin);
        request.Headers.Add(CookieCsrfFilterHeader, "1");
        request.Headers.Add("Cookie", $"{AuthController.RefreshCookieName}=raw-token");

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.Auth.LogoutCalls.Should().Be(1);
        var cookies = SetCookie(response).Select(cookie => cookie.ToLowerInvariant()).ToArray();
        cookies.Should().HaveCount(2);
        cookies.Should().OnlyContain(cookie =>
            cookie.Contains("mc_refresh=")
            && cookie.Contains("httponly")
            && cookie.Contains("secure")
            && cookie.Contains("samesite=none")
            && cookie.Contains("path=/api/auth")
            && cookie.Contains("expires="));
        cookies.Count(cookie => cookie.Contains("partitioned")).Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("https://evil.example")]
    [InlineData("https://maison-calliard.netlify.app")]
    [InlineData("http://localhost:5174")]
    [InlineData("https://localhost:5173")]
    [InlineData("not a url")]
    public async Task Refresh_WhenTheOriginIsNotAnAllowedAdminOrigin_DoesNotTouchTheSession(string? origin)
    {
        _factory.Auth.Reset();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add(CookieCsrfFilterHeader, "1");
        request.Headers.Add("Cookie", $"{AuthController.RefreshCookieName}=raw-token");
        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Auth.RefreshCalls.Should().Be(0);
        SetCookie(response).Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_WhenTheOriginIsOnlyInThePublicCorsList_IsRejected()
    {
        _factory.Auth.Reset();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Origin", PublicOrigin);
        request.Headers.Add(CookieCsrfFilterHeader, "1");
        request.Headers.Add("Cookie", $"{AuthController.RefreshCookieName}=raw-token");

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Auth.RefreshCalls.Should().Be(0);
    }

    [Fact]
    public async Task Refresh_WithoutTheCsrfHeader_IsRejected()
    {
        _factory.Auth.Reset();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Origin", AdminOrigin);
        request.Headers.Add("Cookie", $"{AuthController.RefreshCookieName}=raw-token");

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Auth.RefreshCalls.Should().Be(0);
    }

    [Fact]
    public async Task Preflight_AllowsTheCsrfHeaderForTheAdminOriginOnly()
    {
        var allowed = await SendPreflightAsync(AdminOrigin);
        var denied = await SendPreflightAsync("https://evil.example");

        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Header(allowed, "Access-Control-Allow-Origin").Should().Be(AdminOrigin);
        Header(allowed, "Access-Control-Allow-Headers").ToLowerInvariant().Should().Contain("x-csrf");
        Header(allowed, "Access-Control-Allow-Credentials").Should().Be("true");
        Header(allowed, "Access-Control-Allow-Origin").Should().NotBe("*");

        Header(denied, "Access-Control-Allow-Origin").Should().NotBe("https://evil.example");
        Header(denied, "Access-Control-Allow-Origin").Should().NotBe("*");
    }

    [Fact]
    public async Task Login_WithJson_SetsThePartitionedCookieWithoutACsrfHeader()
    {
        _factory.Auth.Reset();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest { Email = "admin@example.com", Password = "secret" })
        };

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Auth.LoginCalls.Should().Be(1);
        SetCookie(response).Single().ToLowerInvariant().Should().Contain("partitioned");
    }

    [Fact]
    public async Task Login_WhenVerificationIsDisabled_ReachesTheServiceWithoutCsrf()
    {
        _factory.Auth.Reset();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest { Email = "admin@example.com", Password = "secret" })
        };

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Auth.LoginCalls.Should().Be(1);
    }

    [Theory]
    [InlineData("/api/auth/login/verify")]
    [InlineData("/api/auth/login/resend")]
    public async Task LoginCodeEndpoints_WithoutCsrf_DoNotCallTheService(string path)
    {
        _factory.Auth.Reset();
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { challengeToken = "token", code = "123456" })
        };
        request.Headers.Add("Origin", AdminOrigin);

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Auth.VerifyCalls.Should().Be(0);
        _factory.Auth.ResendCalls.Should().Be(0);
    }

    [Fact]
    public async Task VerifyLoginCode_WithTrustedOriginAndCsrfHeader_ReachesTheService()
    {
        _factory.Auth.Reset();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/verify")
        {
            Content = JsonContent.Create(new VerifyLoginCodeRequest { ChallengeToken = "token", Code = "123456" })
        };
        request.Headers.Add("Origin", AdminOrigin);
        request.Headers.Add(CookieCsrfFilterHeader, "1");

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Auth.VerifyCalls.Should().Be(1);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/google")]
    public async Task LoginEndpoints_RejectSimpleCrossSiteForms(string path)
    {
        _factory.Auth.Reset();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "admin@example.com",
            ["Password"] = "secret",
            ["Code"] = "code",
            ["CodeVerifier"] = "verifier",
            ["RedirectUri"] = "https://evil.example/callback"
        });
        using var plain = new StringContent("{\"email\":\"admin@example.com\"}", System.Text.Encoding.UTF8, "text/plain");

        var formResponse = await _factory.CreateClient().PostAsync(path, form);
        var plainResponse = await _factory.CreateClient().PostAsync(path, plain);

        formResponse.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        plainResponse.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        SetCookie(formResponse).Should().BeEmpty();
        SetCookie(plainResponse).Should().BeEmpty();
        _factory.Auth.LoginCalls.Should().Be(0);
        _factory.Auth.GoogleCalls.Should().Be(0);
    }

    [Fact]
    public async Task CustomerOrder_DoesNotRequireTheCsrfHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new { })
        };

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Cross-site request blocked.");
    }

    [Fact]
    public async Task AdminProfile_WithAnUntrustedBearerToken_IsNotBlockedAsCsrf()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/me");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer not-a-maison-token");

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Cross-site request blocked.");
    }

    [Fact]
    public void ConfigurationOverride_DoesNotPointAtARemoteDatabase()
    {
        var connectionString = _factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        connectionString.Should().StartWith("Host=127.0.0.1;Port=1;");
    }

    private async Task<HttpResponseMessage> SendPreflightAsync(string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/refresh");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "x-csrf");
        return await _factory.CreateClient().SendAsync(request);
    }

    private const string CookieCsrfFilterHeader = "X-CSRF";

    private static IReadOnlyList<string> SetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToArray() : [];

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(',', values) : string.Empty;
}

public sealed class CsrfStartupTests
{
    [Fact]
    public async Task Create_WhenBrowserOriginsAreEmpty_StartsTheApi()
    {
        using var factory = new CsrfApiFactory(includeBrowserOrigins: false, browserOriginMissingFromCors: false, corsWildcard: false);
        var response = await factory.CreateClient().GetAsync("/api/admin/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refresh.Headers.Add("Origin", "http://localhost:5173");
        refresh.Headers.Add("X-CSRF", "1");
        refresh.Headers.Add("Cookie", $"{AuthController.RefreshCookieName}=raw-token");
        var refreshResponse = await factory.CreateClient().SendAsync(refresh);

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Auth.RefreshCalls.Should().Be(0);
    }

    [Fact]
    public void Create_WhenAnAdminOriginIsMissingFromCors_DoesNotStart()
    {
        var act = () => new CsrfApiFactory(includeBrowserOrigins: true, browserOriginMissingFromCors: true, corsWildcard: false).CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*CORS*");
    }

    [Fact]
    public void Create_WhenCorsContainsAWildcard_DoesNotStart()
    {
        var act = () => new CsrfApiFactory(includeBrowserOrigins: true, browserOriginMissingFromCors: false, corsWildcard: true).CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Wildcard*");
    }
}

public class CsrfApiFactory : WebApplicationFactory<Program>
{
    private const string IsolatedConnectionString = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none";
    private const string IsolatedSigningKey = "design-time-only-key-not-used-for-tokens";
    private static readonly object ConfigurationLock = new();

    private readonly bool _includeBrowserOrigins;
    private readonly bool _browserOriginMissingFromCors;
    private readonly bool _corsWildcard;
    private readonly string? _browserOriginsCsv;
    private readonly bool _loginEmailVerification;

    public CsrfApiFactory()
        : this(includeBrowserOrigins: true, browserOriginMissingFromCors: false, corsWildcard: false)
    {
    }

    internal CsrfApiFactory(
        bool includeBrowserOrigins,
        bool browserOriginMissingFromCors,
        bool corsWildcard,
        string? browserOriginsCsv = null,
        bool loginEmailVerification = false)
    {
        _includeBrowserOrigins = includeBrowserOrigins;
        _browserOriginMissingFromCors = browserOriginMissingFromCors;
        _corsWildcard = corsWildcard;
        _browserOriginsCsv = browserOriginsCsv;
        _loginEmailVerification = loginEmailVerification;
        Auth = new RecordingAuthService();
    }

    public RecordingAuthService Auth { get; }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (ConfigurationLock)
        {
            var names = new[]
            {
                "AUTH_SIGNING_KEY",
                "ConnectionStrings__DefaultConnection",
                "Cors__AllowedOrigins__0",
                "Cors__AllowedOrigins__1",
                "Cors__AllowedOriginsCsv",
                "Frontend__Url",
                "FrontendUrl",
                "Auth__AllowedBrowserOrigins__0",
                "Auth__AllowedBrowserOrigins__1",
                "Auth__AllowedBrowserOriginsCsv",
                "Auth__LoginEmailVerification",
                "Auth__LoginCodePepper",
                "Auth__DevCodeFile"
            };
            var previous = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
            try
            {
                ApplyIsolatedConfiguration();
                return base.CreateHost(builder);
            }
            finally
            {
                foreach (var (name, value) in previous)
                {
                    Environment.SetEnvironmentVariable(name, value);
                }
            }
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services.Where(service =>
                         service.ServiceType == typeof(IHostedService)
                         && service.ImplementationType == typeof(AdminUserSeeder)).ToList())
            {
                services.Remove(descriptor);
            }

            services.RemoveAll<IAuthService>();
            services.AddSingleton<IAuthService>(Auth);
            var orders = new Mock<IOrderService>();
            orders.Setup(service => service.CreateAsync(It.IsAny<CreateOrderRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentException("validation"));
            services.RemoveAll<IOrderService>();
            services.AddSingleton(orders.Object);
        });
    }

    private void ApplyIsolatedConfiguration()
    {
        Environment.SetEnvironmentVariable("AUTH_SIGNING_KEY", IsolatedSigningKey);
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", IsolatedConnectionString);
        Environment.SetEnvironmentVariable("Auth__DevCodeFile", "false");
        Environment.SetEnvironmentVariable("Auth__LoginEmailVerification", _loginEmailVerification ? "true" : "false");
        Environment.SetEnvironmentVariable(
            "Auth__LoginCodePepper",
            _loginEmailVerification ? new string('p', 32) : null);
        if (_browserOriginsCsv is not null)
        {
            ApplyCsvConfiguration(_browserOriginsCsv);
            return;
        }

        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", _corsWildcard ? "https://*.netlify.app" : "http://localhost:5173");
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__1", "https://www.maisoncalliard.com");
        Environment.SetEnvironmentVariable("Cors__AllowedOriginsCsv", " ");
        Environment.SetEnvironmentVariable("Frontend__Url", " ");
        Environment.SetEnvironmentVariable("FrontendUrl", " ");
        Environment.SetEnvironmentVariable("Auth__AllowedBrowserOriginsCsv", " ");
        Environment.SetEnvironmentVariable(
            "Auth__AllowedBrowserOrigins__0",
            _includeBrowserOrigins
                ? _browserOriginMissingFromCors ? "https://admin.example:8443" : "http://localhost:5173"
                : " ");
        Environment.SetEnvironmentVariable("Auth__AllowedBrowserOrigins__1", " ");
    }

    private static void ApplyCsvConfiguration(string browserOriginsCsv)
    {
        Environment.SetEnvironmentVariable("Frontend__Url", " ");
        Environment.SetEnvironmentVariable("FrontendUrl", " ");
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", null);
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__1", null);
        Environment.SetEnvironmentVariable("Cors__AllowedOriginsCsv", $"{browserOriginsCsv},https://www.maisoncalliard.com");
        Environment.SetEnvironmentVariable("Auth__AllowedBrowserOrigins__0", null);
        Environment.SetEnvironmentVariable("Auth__AllowedBrowserOrigins__1", null);
        Environment.SetEnvironmentVariable("Auth__AllowedBrowserOriginsCsv", browserOriginsCsv);
    }
}

public sealed class RecordingAuthService : IAuthService
{
    private static readonly DateTimeOffset Expires = new(2026, 11, 10, 16, 0, 0, TimeSpan.Zero);

    public int LoginCalls { get; private set; }
    public int GoogleCalls { get; private set; }
    public int RefreshCalls { get; private set; }
    public int LogoutCalls { get; private set; }
    public int VerifyCalls { get; private set; }
    public int ResendCalls { get; private set; }

    public void Reset()
    {
        LoginCalls = 0;
        GoogleCalls = 0;
        RefreshCalls = 0;
        LogoutCalls = 0;
        VerifyCalls = 0;
        ResendCalls = 0;
    }

    public Task<AuthResult> LoginAsync(
        LoginRequest request,
        string? trustedDeviceToken = null,
        CancellationToken cancellationToken = default)
    {
        LoginCalls++;
        return Task.FromResult<AuthResult>(new AuthSuccess("access-token", Expires, "next-refresh", Expires.AddDays(14)));
    }

    public Task<AuthResult> VerifyEmailCodeAsync(
        VerifyLoginCodeRequest request,
        string? deviceLabel,
        CancellationToken cancellationToken = default)
    {
        VerifyCalls++;
        return Task.FromResult<AuthResult>(new AuthRejected());
    }

    public Task<ResendLoginCodeResult> ResendEmailCodeAsync(
        ResendLoginCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        ResendCalls++;
        return Task.FromResult<ResendLoginCodeResult>(new ResendLoginCodeRejected());
    }

    public Task<TrustedDeviceListResult> ListTrustedDevicesAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TrustedDeviceListResult(TrustedDeviceListStatus.Disabled, []));

    public Task<TrustedDeviceRevocationResult> RevokeTrustedDeviceAsync(
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new TrustedDeviceRevocationResult(TrustedDeviceRevocationStatus.Disabled, false));

    public Task<TrustedDeviceRevocationResult> RevokeAllTrustedDevicesAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new TrustedDeviceRevocationResult(TrustedDeviceRevocationStatus.Disabled, false));

    public Task<AuthResult> LoginWithGoogleAsync(GoogleLoginRequest request, CancellationToken cancellationToken = default)
    {
        GoogleCalls++;
        return Task.FromResult<AuthResult>(new AuthRejected());
    }

    public Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        RefreshCalls++;
        return Task.FromResult<AuthResult>(new AuthSuccess("access-token", Expires, "next-refresh", Expires.AddDays(14)));
    }

    public Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        LogoutCalls++;
        return Task.CompletedTask;
    }

    public Task<AdminProfileResponse?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<AdminProfileResponse?>(null);

    public Task<PasswordChangeResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PasswordChangeResult(PasswordChangeStatus.NotAllowed));
}

public sealed class LoginVerificationCsrfFactory : CsrfApiFactory
{
    public LoginVerificationCsrfFactory()
        : base(
            includeBrowserOrigins: true,
            browserOriginMissingFromCors: false,
            corsWildcard: false,
            loginEmailVerification: true)
    {
    }
}

public sealed class LoginVerificationCsrfTests : IClassFixture<LoginVerificationCsrfFactory>
{
    private readonly LoginVerificationCsrfFactory _factory;

    public LoginVerificationCsrfTests(LoginVerificationCsrfFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WhenVerificationIsEnabledAndCsrfIsMissing_DoesNotCallTheService()
    {
        _factory.Auth.Reset();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest { Email = "admin@example.com", Password = "secret" })
        };
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Auth.LoginCalls.Should().Be(0);
    }

    [Fact]
    public async Task Login_WhenVerificationIsEnabledAndCsrfIsPresent_ReachesTheService()
    {
        _factory.Auth.Reset();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest { Email = "admin@example.com", Password = "secret" })
        };
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("X-CSRF", "1");

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Auth.LoginCalls.Should().Be(1);
    }
}

public sealed class AzureCsvOriginFactory : CsrfApiFactory
{
    public const string Origins = "https://admin.example,https://staging-admin.example";

    public AzureCsvOriginFactory()
        : base(
            includeBrowserOrigins: false,
            browserOriginMissingFromCors: false,
            corsWildcard: false,
            browserOriginsCsv: Origins)
    {
    }
}

public sealed class AzureCsvOriginTests : IClassFixture<AzureCsvOriginFactory>
{
    private readonly AzureCsvOriginFactory _factory;

    public AzureCsvOriginTests(AzureCsvOriginFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("https://admin.example")]
    [InlineData("https://staging-admin.example")]
    public async Task Refresh_WhenTheOriginComesFromTheCsvVariable_RotatesTheSession(string origin)
    {
        _factory.Auth.Reset();
        using var request = RefreshRequest(origin);

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Auth.RefreshCalls.Should().Be(1);
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("http://localhost:5173")]
    [InlineData("https://www.maisoncalliard.com")]
    [InlineData("https://evil.example")]
    public async Task Refresh_WhenTheOriginIsOutsideTheCsvVariable_IsRejected(string origin)
    {
        _factory.Auth.Reset();
        using var request = RefreshRequest(origin);

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Auth.RefreshCalls.Should().Be(0);
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    private static HttpRequestMessage RefreshRequest(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-CSRF", "1");
        request.Headers.Add("Cookie", $"{AuthController.RefreshCookieName}=raw-token");
        return request;
    }
}
