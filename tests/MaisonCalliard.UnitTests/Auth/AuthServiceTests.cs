using MaisonCalliard.Application.Auth;
using MaisonCalliard.Application.Auth.Dtos;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.Repositories;
using FluentAssertions;
using Moq;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class AuthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryAdminUsers _users = new();
    private readonly InMemoryRefreshTokens _refreshTokens = new();
    private readonly FakePasswordHasher _passwords = new();
    private readonly FakeAccessTokens _accessTokens = new();
    private readonly Mock<IGoogleAccountVerifier> _google = new();
    private readonly Mock<IAdminCredentialStore> _credentials = new();
    private readonly FixedTimeProvider _time = new(Now);
    private readonly AuthPolicy _policy = new()
    {
        LoginLockoutThreshold = 5,
        LockoutDuration = TimeSpan.FromMinutes(15),
        RefreshTokenLifetime = TimeSpan.FromDays(14)
    };

    private AuthService CreateSut() => new(
        _users,
        _refreshTokens,
        _passwords,
        _accessTokens,
        _google.Object,
        _credentials.Object,
        new InMemoryLoginFlow(_users, _refreshTokens, _accessTokens, _time),
        _policy,
        _time);

    [Fact]
    public async Task LoginAsync_WhenPasswordMatches_ResetsFailuresAndStoresOnlyTheRefreshHash()
    {
        var user = AddUser("admin@example.com", "secret");
        user.FailedLoginCount = 3;
        user.LockoutEnd = Now.AddMinutes(-1);

        var result = await CreateSut().LoginAsync(new LoginRequest
        {
            Email = " Admin@Example.com ",
            Password = "secret"
        });

        var success = result.Should().BeOfType<AuthSuccess>().Subject;
        success.AccessToken.Should().Be("access-token");
        user.FailedLoginCount.Should().Be(0);
        user.LockoutEnd.Should().BeNull();
        var stored = _refreshTokens.Tokens.Should().ContainSingle().Subject;
        stored.TokenHash.Should().Be(RefreshTokenProtector.Hash(success.RefreshToken));
        stored.TokenHash.Should().NotBe(success.RefreshToken);
        stored.ExpiresAt.Should().Be(Now.AddDays(14));
    }

    [Fact]
    public async Task LoginAsync_WhenPasswordIsWrongOrUserIsUnknown_ReturnsTheSameRejection()
    {
        AddUser("admin@example.com", "secret");

        var wrongPassword = await CreateSut().LoginAsync(new LoginRequest
        {
            Email = "admin@example.com",
            Password = "nope"
        });
        var unknownUser = await CreateSut().LoginAsync(new LoginRequest
        {
            Email = "missing@example.com",
            Password = "secret"
        });

        wrongPassword.Should().BeOfType<AuthRejected>();
        unknownUser.Should().BeOfType<AuthRejected>();
        _passwords.Verifications.Should().Contain(("secret", _passwords.DummyHash));
        _refreshTokens.Tokens.Should().BeEmpty();
    }

    [Fact]
    public async Task LoginAsync_WhenFailuresReachTheThreshold_LocksTheAccount()
    {
        var user = AddUser("admin@example.com", "secret");
        user.FailedLoginCount = 4;

        var result = await CreateSut().LoginAsync(new LoginRequest
        {
            Email = "admin@example.com",
            Password = "nope"
        });

        result.Should().BeOfType<AuthRejected>();
        user.FailedLoginCount.Should().Be(5);
        user.LockoutEnd.Should().Be(Now.AddMinutes(15));
    }

    [Fact]
    public async Task LoginAsync_WhenAccountIsLockedOrInactive_RejectsWithoutChangingTheCounter()
    {
        var locked = AddUser("locked@example.com", "secret");
        locked.FailedLoginCount = 5;
        locked.LockoutEnd = Now.AddMinutes(10);
        var inactive = AddUser("inactive@example.com", "secret");
        inactive.IsActive = false;

        var lockedResult = await CreateSut().LoginAsync(new LoginRequest
        {
            Email = "locked@example.com",
            Password = "secret"
        });
        var inactiveResult = await CreateSut().LoginAsync(new LoginRequest
        {
            Email = "inactive@example.com",
            Password = "secret"
        });

        lockedResult.Should().BeOfType<AuthRejected>();
        inactiveResult.Should().BeOfType<AuthRejected>();
        locked.FailedLoginCount.Should().Be(5);
        inactive.FailedLoginCount.Should().Be(0);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheStoreRotates_ReturnsTheNewSession()
    {
        var user = AddUser("admin@example.com", "secret");
        var expires = Now.AddDays(14);
        _credentials
            .Setup(store => store.RotateAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionRotated(user, "new-refresh", expires));

        var refreshed = await CreateSut().RefreshAsync("old-refresh");

        var success = refreshed.Should().BeOfType<AuthSuccess>().Subject;
        success.AccessToken.Should().Be("access-token");
        success.RefreshToken.Should().Be("new-refresh");
        success.RefreshTokenExpiresAt.Should().Be(expires);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheStoreRejects_ReturnsRejected()
    {
        _credentials
            .Setup(store => store.RotateAsync("used-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionRotationRejected());

        var refreshed = await CreateSut().RefreshAsync("used-refresh");

        refreshed.Should().BeOfType<AuthRejected>();
    }

    [Fact]
    public async Task GetProfileAsync_ReturnsOnlyPublicFieldsForAnActiveAdmin()
    {
        var user = AddUser("admin@example.com", "secret");
        user.MustChangePassword = true;

        var profile = await CreateSut().GetProfileAsync(user.Id);

        profile.Should().NotBeNull();
        profile!.Email.Should().Be("admin@example.com");
        profile.Role.Should().Be(AdminUser.AdminRole);
        profile.MustChangePassword.Should().BeTrue();
        profile.GetType().GetProperties().Select(property => property.Name)
            .Should().Equal("Id", "Email", "Role", "MustChangePassword");
    }

    [Fact]
    public async Task GetProfileAsync_WhenTheAdminIsInactive_ReturnsNull()
    {
        var user = AddUser("admin@example.com", "secret");
        user.IsActive = false;

        var profile = await CreateSut().GetProfileAsync(user.Id);

        profile.Should().BeNull();
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenConfirmationOrStrengthFails_DoesNotTouchTheStore()
    {
        var user = AddUser("admin@example.com", "secret");

        var mismatch = await CreateSut().ChangePasswordAsync(user.Id, new ChangePasswordRequest
        {
            CurrentPassword = "secret",
            NewPassword = "a-new-passphrase",
            ConfirmPassword = "another-passphrase"
        });
        var weak = await CreateSut().ChangePasswordAsync(user.Id, new ChangePasswordRequest
        {
            CurrentPassword = "secret",
            NewPassword = "short-pass",
            ConfirmPassword = "short-pass"
        });

        mismatch.Status.Should().Be(PasswordChangeStatus.Mismatch);
        weak.Status.Should().Be(PasswordChangeStatus.Weak);
        _credentials.Verify(
            store => store.ChangePasswordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LoginWithGoogleAsync_WhenEmailIsVerifiedForAnActiveAdmin_LinksTheSubject()
    {
        var user = AddUser("admin@example.com", "secret");
        _google.Setup(verifier => verifier.VerifyAsync("code", "verifier", "https://admin.example/callback", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleAccount("google-sub", "admin@example.com", true));

        var result = await CreateSut().LoginWithGoogleAsync(new GoogleLoginRequest
        {
            Code = "code",
            CodeVerifier = "verifier",
            RedirectUri = "https://admin.example/callback"
        });

        result.Should().BeOfType<AuthSuccess>();
        user.GoogleSubject.Should().Be("google-sub");
    }

    [Fact]
    public async Task LoginWithGoogleAsync_WhenAccountIsUnknownUnverifiedOrSubjectDiffers_RejectsWithoutCreatingAUser()
    {
        var linked = AddUser("admin@example.com", "secret");
        linked.GoogleSubject = "original-sub";
        var sut = CreateSut();

        _google.Setup(verifier => verifier.VerifyAsync("missing", "verifier", "https://admin.example/callback", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleAccount("new-sub", "other@example.com", true));
        var unknown = await sut.LoginWithGoogleAsync(new GoogleLoginRequest
        {
            Code = "missing",
            CodeVerifier = "verifier",
            RedirectUri = "https://admin.example/callback"
        });

        _google.Setup(verifier => verifier.VerifyAsync("unverified", "verifier", "https://admin.example/callback", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleAccount("original-sub", "admin@example.com", false));
        var unverified = await sut.LoginWithGoogleAsync(new GoogleLoginRequest
        {
            Code = "unverified",
            CodeVerifier = "verifier",
            RedirectUri = "https://admin.example/callback"
        });

        _google.Setup(verifier => verifier.VerifyAsync("mismatch", "verifier", "https://admin.example/callback", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleAccount("other-sub", "admin@example.com", true));
        var mismatch = await sut.LoginWithGoogleAsync(new GoogleLoginRequest
        {
            Code = "mismatch",
            CodeVerifier = "verifier",
            RedirectUri = "https://admin.example/callback"
        });

        unknown.Should().BeOfType<AuthRejected>();
        unverified.Should().BeOfType<AuthRejected>();
        mismatch.Should().BeOfType<AuthRejected>();
        _users.Users.Should().ContainSingle();
        linked.GoogleSubject.Should().Be("original-sub");
        _users.Added.Should().Be(1);
    }

    private AdminUser AddUser(string email, string password)
    {
        var user = new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = AdminUser.NormalizeEmail(email),
            PasswordHash = _passwords.Hash(password),
            Role = AdminUser.AdminRole,
            IsActive = true,
            CreatedAt = Now
        };
        _users.Users.Add(user);
        _users.Added++;
        return user;
    }

    private sealed class InMemoryAdminUsers : IAdminUserRepository
    {
        public List<AdminUser> Users { get; } = [];

        public int Added { get; set; }

        public Task<AdminUser?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.SingleOrDefault(user => user.NormalizedEmail == normalizedEmail));

        public Task<AdminUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Id == id));

        public Task AddAsync(AdminUser user, CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            Added++;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(AdminUser user, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryRefreshTokens : IRefreshTokenRepository
    {
        public List<RefreshToken> Tokens { get; } = [];

        public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Tokens.SingleOrDefault(token => token.TokenHash == tokenHash));

        public Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default)
        {
            Tokens.Add(token);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RevokeActiveAsync(Guid adminUserId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default)
        {
            foreach (var token in Tokens.Where(token => token.AdminUserId == adminUserId && token.RevokedAt is null))
            {
                token.RevokedAt = revokedAt;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string DummyHash => "dummy-hash";

        public List<(string Password, string Hash)> Verifications { get; } = [];

        public string Hash(string password) => "hashed:" + password;

        public bool Verify(string password, string passwordHash)
        {
            Verifications.Add((password, passwordHash));
            return passwordHash == "hashed:" + password;
        }
    }

    private sealed class FakeAccessTokens : IAccessTokenIssuer
    {
        public AccessToken Issue(AdminUser user) => new("access-token", Now.AddMinutes(15));
    }

    private sealed class InMemoryLoginFlow : ILoginFlow
    {
        private readonly InMemoryAdminUsers _users;
        private readonly InMemoryRefreshTokens _refreshTokens;
        private readonly FakeAccessTokens _accessTokens;
        private readonly TimeProvider _time;

        public InMemoryLoginFlow(
            InMemoryAdminUsers users,
            InMemoryRefreshTokens refreshTokens,
            FakeAccessTokens accessTokens,
            TimeProvider time)
        {
            _users = users;
            _refreshTokens = refreshTokens;
            _accessTokens = accessTokens;
            _time = time;
        }

        public Task<AuthResult> CompletePasswordLoginAsync(
            Guid userId,
            string password,
            string? trustedDeviceToken,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Issue(userId));

        public Task<AuthResult> CompleteGoogleLoginAsync(
            Guid userId,
            string? googleSubjectToLink,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Issue(userId));

        public Task<AuthResult> VerifyEmailCodeAsync(
            string challengeToken,
            string code,
            bool rememberDevice,
            string? deviceLabel,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AuthResult>(new AuthRejected());

        public Task<ResendLoginCodeResult> ResendEmailCodeAsync(string challengeToken, CancellationToken cancellationToken = default) =>
            Task.FromResult<ResendLoginCodeResult>(new ResendLoginCodeRejected());

        public Task<TrustedDeviceListResult> ListTrustedDevicesAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TrustedDeviceListResult(TrustedDeviceListStatus.Disabled, []));

        public Task<TrustedDeviceRevocationResult> RevokeTrustedDeviceAsync(
            Guid userId,
            Guid deviceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TrustedDeviceRevocationResult(TrustedDeviceRevocationStatus.Disabled, false));

        public Task<TrustedDeviceRevocationResult> RevokeAllTrustedDevicesAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TrustedDeviceRevocationResult(TrustedDeviceRevocationStatus.Disabled, false));

        private AuthResult Issue(Guid userId)
        {
            var user = _users.Users.Single(admin => admin.Id == userId);
            var now = _time.GetUtcNow();
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;
            var rawRefreshToken = RefreshTokenProtector.Generate();
            _refreshTokens.Tokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                AdminUserId = user.Id,
                TokenHash = RefreshTokenProtector.Hash(rawRefreshToken),
                ExpiresAt = now.AddDays(14),
                CreatedAt = now
            });
            var accessToken = _accessTokens.Issue(user);
            return new AuthSuccess(accessToken.Token, accessToken.ExpiresAt, rawRefreshToken, accessToken.ExpiresAt.AddDays(14));
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public FixedTimeProvider(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
