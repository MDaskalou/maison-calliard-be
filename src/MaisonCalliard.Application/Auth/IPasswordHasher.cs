using MaisonCalliard.Domain.Entities;

namespace MaisonCalliard.Application.Auth;

public interface IPasswordHasher
{
    string DummyHash { get; }

    string Hash(string password);

    bool Verify(string password, string passwordHash);
}
