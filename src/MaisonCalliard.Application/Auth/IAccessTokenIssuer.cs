using MaisonCalliard.Domain.Entities;

namespace MaisonCalliard.Application.Auth;

public interface IAccessTokenIssuer
{
    AccessToken Issue(AdminUser user);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);
