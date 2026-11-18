using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private readonly AuthOptions _options;
    private readonly TimeProvider _time;

    public JwtAccessTokenIssuer(IOptions<AuthOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public AccessToken Issue(AdminUser user)
    {
        var now = _time.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes > 0 ? _options.AccessTokenMinutes : 15);
        var credentials = new SigningCredentials(
            AuthTokenValidation.CreateParameters(_options).IssuerSigningKey,
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims:
            [
                new Claim("sub", user.Id.ToString()),
                new Claim("email", user.Email),
                new Claim(AuthTokenValidation.RoleClaimType, user.Role),
                new Claim(AuthTokenValidation.TokenVersionClaim, user.TokenVersion.ToString())
            ],
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
