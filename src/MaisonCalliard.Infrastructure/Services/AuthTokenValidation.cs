using System.Text;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.IdentityModel.Tokens;

namespace MaisonCalliard.Infrastructure.Services;

public static class AuthTokenValidation
{
    public const string RoleClaimType = "roles";

    public const string TokenVersionClaim = "token_version";

    public const string IssuerClaim = "iss";

    public static TokenValidationParameters CreateParameters(AuthOptions options)
    {
        options.EnsureSigningKey();

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            RoleClaimType = RoleClaimType,
            NameClaimType = "sub"
        };
    }
}
