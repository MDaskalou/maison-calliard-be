using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Infrastructure.Options;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class JwtAccessTokenIssuerTests
{
    [Fact]
    public void Issue_ProducesATokenThatCarriesTheAdminRole()
    {
        var options = new AuthOptions
        {
            Issuer = "MaisonCalliard",
            Audience = "MaisonCalliard",
            SigningKey = new string('k', 32),
            AccessTokenMinutes = 15
        };
        var now = TimeProvider.System.GetUtcNow();
        var issuer = new JwtAccessTokenIssuer(Options.Create(options), TimeProvider.System);
        var user = new AdminUser
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Email = "admin@example.com",
            Role = AdminUser.AdminRole
        };

        var issued = issuer.Issue(user);

        issued.ExpiresAt.Should().BeCloseTo(now.AddMinutes(15), TimeSpan.FromSeconds(5));
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(issued.Token, AuthTokenValidation.CreateParameters(options), out _);
        principal.IsInRole(AdminUser.AdminRole).Should().BeTrue();
        principal.FindFirst("sub")!.Value.Should().Be(user.Id.ToString());
        principal.FindFirst("email")!.Value.Should().Be(user.Email);
        principal.FindFirst(AuthTokenValidation.TokenVersionClaim)!.Value.Should().Be("0");
    }
}
