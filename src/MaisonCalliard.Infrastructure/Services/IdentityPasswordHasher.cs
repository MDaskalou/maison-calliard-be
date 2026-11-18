using MaisonCalliard.Application.Auth;
using MaisonCalliard.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<AdminUser> _hasher = new();

    public IdentityPasswordHasher()
    {
        DummyHash = _hasher.HashPassword(new AdminUser(), "not-a-real-password");
    }

    public string DummyHash { get; }

    public string Hash(string password) => _hasher.HashPassword(new AdminUser(), password);

    public bool Verify(string password, string passwordHash)
    {
        var result = _hasher.VerifyHashedPassword(new AdminUser(), passwordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
