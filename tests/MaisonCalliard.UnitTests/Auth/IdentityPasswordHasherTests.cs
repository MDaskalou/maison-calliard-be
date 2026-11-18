using FluentAssertions;
using MaisonCalliard.Infrastructure.Services;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class IdentityPasswordHasherTests
{
    [Fact]
    public void Hash_DoesNotStoreThePasswordAndVerifiesOnlyTheOriginal()
    {
        var hasher = new IdentityPasswordHasher();

        var hash = hasher.Hash("correct horse");

        hash.Should().NotBe("correct horse");
        hash.Should().NotContain("correct horse");
        hasher.Verify("correct horse", hash).Should().BeTrue();
        hasher.Verify("wrong horse", hash).Should().BeFalse();
        hasher.DummyHash.Should().NotBeNullOrWhiteSpace();
        hasher.Verify("correct horse", hasher.DummyHash).Should().BeFalse();
    }
}
