using System.Security.Claims;
using FluentAssertions;
using MaisonCalliard.Api.Auth;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class AdminAccessMiddlewareTests
{
    private readonly Mock<IAdminUserRepository> _users = new();
    private readonly AuthOptions _options = new() { Issuer = "MaisonCalliard" };

    [Fact]
    public async Task InvokeAsync_WhenTheCallerIsAnonymous_LeavesCustomerEndpointsAlone()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/orders";
        var called = false;

        await CreateSut().InvokeAsync(context, _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        called.Should().BeTrue();
        _users.Verify(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheIssuerIsLogto_DoesNotApplyTokenVersionRules()
    {
        var context = CreateContext("https://k7ae42.logto.app/oidc", Guid.NewGuid(), tokenVersion: null);
        var called = false;

        await CreateSut().InvokeAsync(context, _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        called.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        _users.Verify(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_WhenOurTokenHasNoVersion_Returns401()
    {
        var context = CreateContext("MaisonCalliard", Guid.NewGuid(), tokenVersion: null);

        await CreateSut().InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheAdminMustChangePassword_BlocksOtherAdminEndpoints()
    {
        var user = ActiveUser();
        user.MustChangePassword = true;
        _users.Setup(repository => repository.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var orders = CreateContext("MaisonCalliard", user.Id, "0");
        orders.Request.Path = "/api/orders";
        orders.Request.Method = HttpMethods.Get;

        await CreateSut().InvokeAsync(orders, _ => throw new InvalidOperationException("Orders should stay blocked."));

        orders.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheAdminMustChangePassword_BlocksTrustedDeviceRevocation()
    {
        var user = ActiveUser();
        user.MustChangePassword = true;
        _users.Setup(repository => repository.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var revoke = CreateContext("MaisonCalliard", user.Id, "0");
        revoke.Request.Path = "/api/admin/me/trusted-devices/revoke-all";
        revoke.Request.Method = HttpMethods.Post;

        await CreateSut().InvokeAsync(revoke, _ => throw new InvalidOperationException("Revocation should stay blocked."));

        revoke.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheAdminMustChangePassword_AllowsProfileAndPasswordChange()
    {
        var user = ActiveUser();
        user.MustChangePassword = true;
        _users.Setup(repository => repository.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var profile = CreateContext("MaisonCalliard", user.Id, "0");
        profile.Request.Path = "/api/admin/me";
        profile.Request.Method = HttpMethods.Get;
        var called = false;

        await CreateSut().InvokeAsync(profile, _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        called.Should().BeTrue();

        foreach (var (path, method) in new[]
        {
            ("/api/admin/me/change-password", HttpMethods.Post),
            ("/api/auth/logout", HttpMethods.Post)
        })
        {
            var allowed = CreateContext("MaisonCalliard", user.Id, "0");
            allowed.Request.Path = path;
            allowed.Request.Method = method;
            var allowedCalled = false;
            await CreateSut().InvokeAsync(allowed, _ =>
            {
                allowedCalled = true;
                return Task.CompletedTask;
            });
            allowedCalled.Should().BeTrue();
        }
    }

    [Fact]
    public async Task InvokeAsync_WhenTheTokenVersionDoesNotMatch_Returns401()
    {
        var user = ActiveUser();
        user.TokenVersion = 2;
        _users.Setup(repository => repository.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var context = CreateContext("MaisonCalliard", user.Id, "1");
        context.Request.Path = "/api/admin/me";

        await CreateSut().InvokeAsync(context, _ => throw new InvalidOperationException("Stale tokens should be rejected."));

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheAdminIsInactive_Returns401()
    {
        var user = ActiveUser();
        user.IsActive = false;
        _users.Setup(repository => repository.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var context = CreateContext("MaisonCalliard", user.Id, "0");

        await CreateSut().InvokeAsync(context, _ => throw new InvalidOperationException("Inactive admins should be rejected."));

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    private AdminAccessMiddleware CreateSut() => new(_users.Object, Options.Create(_options));

    private static AdminUser ActiveUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "admin@example.com",
        Role = AdminUser.AdminRole,
        IsActive = true
    };

    private static DefaultHttpContext CreateContext(string issuer, Guid userId, string? tokenVersion)
    {
        var claims = new List<Claim>
        {
            new("iss", issuer),
            new("sub", userId.ToString()),
            new("roles", AdminUser.AdminRole)
        };
        if (tokenVersion is not null)
        {
            claims.Add(new Claim("token_version", tokenVersion));
        }

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"))
        };
        return context;
    }
}
