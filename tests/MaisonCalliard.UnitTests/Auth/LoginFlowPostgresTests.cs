using System.Text;
using FluentAssertions;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.DevAdminPasswordReset;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Infrastructure.Data;
using MaisonCalliard.Infrastructure.Options;
using MaisonCalliard.Infrastructure.Repositories;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class LoginFlowPostgresTests : IClassFixture<PostgresAdminDatabase>
{
    private const string Password = "current-password";
    private readonly PostgresAdminDatabase _database;
    private readonly IdentityPasswordHasher _hasher = new();

    public LoginFlowPostgresTests(PostgresAdminDatabase database)
    {
        _database = database;
    }

    [Fact]
    public async Task WrongPassword_DoesNotSendACodeOrCreateAChallenge()
    {
        var userId = await SeedAdminAsync();
        var sender = new CapturingLoginEmailSender();
        var logger = new CollectingLogger<LoginFlow>();

        var result = await LoginAsync(userId, "wrong-password", EnabledPolicy(), sender, logger);

        result.Should().BeOfType<AuthRejected>();
        sender.Calls.Should().Be(0);
        (await ChallengeCountAsync(userId)).Should().Be(0);
        (await ActiveRefreshCountAsync(userId)).Should().Be(0);
        logger.Text.Should().NotContain("wrong-password");
    }

    [Fact]
    public async Task CorrectPassword_CreatesAnUnverifiedChallengeAndNoSession()
    {
        var userId = await SeedAdminAsync();
        var sender = new CapturingLoginEmailSender();
        var logger = new CollectingLogger<LoginFlow>();

        var result = await LoginAsync(userId, Password, EnabledPolicy(), sender, logger);

        var required = result.Should().BeOfType<AuthVerificationRequired>().Subject;
        sender.Calls.Should().Be(1);
        var code = sender.Sent.Single().Code;
        code.Should().MatchRegex("^[0-9]{6}$");
        (await ActiveRefreshCountAsync(userId)).Should().Be(0);
        var challenge = await SingleChallengeAsync(userId);
        challenge.SentAt.Should().NotBeNull();
        challenge.CodeHash.Should().NotBe(code);
        challenge.TokenHash.Should().NotBe(required.ChallengeToken);
        logger.Text.Should().NotContain(code);
        logger.Text.Should().NotContain(required.ChallengeToken);
        logger.Text.Should().Contain("login_challenge_created");
        logger.Text.Should().Contain(userId.ToString());
    }

    [Fact]
    public async Task FailedSend_ConsumesASendSlotAndLeavesTheChallengeUnverifiable()
    {
        var userId = await SeedAdminAsync();
        var sender = new CapturingLoginEmailSender { Succeed = false };

        var result = await LoginAsync(userId, Password, EnabledPolicy(TimeSpan.Zero), sender);

        result.Should().BeOfType<AuthRejected>();
        sender.Calls.Should().Be(1);
        (await ActiveRefreshCountAsync(userId)).Should().Be(0);
        var challenge = await SingleChallengeAsync(userId);
        challenge.SentAt.Should().BeNull();
        (await ReadQuotaAsync(userId)).SendCount.Should().Be(1);
        (await VerifyAsync("not-a-token", sender.Sent.Single().Code, EnabledPolicy(TimeSpan.Zero), false))
            .Should().BeOfType<AuthRejected>();
    }

    [Fact]
    public async Task Verify_AcceptsTheCodeOnceAndRejectsTheSecondUse()
    {
        var userId = await SeedAdminAsync();
        var policy = EnabledPolicy();
        var (required, code) = await StartChallengeAsync(userId, policy);

        var first = await VerifyAsync(required.ChallengeToken, code, policy, rememberDevice: false);
        var second = await VerifyAsync(required.ChallengeToken, code, policy, rememberDevice: false);

        first.Should().BeOfType<AuthSuccess>();
        second.Should().BeOfType<AuthRejected>();
        (await ActiveRefreshCountAsync(userId)).Should().Be(1);
        (await DeviceCountAsync(userId)).Should().Be(0);
    }

    [Fact]
    public async Task TwoParallelCorrectVerifies_CreateOnlyOneSession()
    {
        var userId = await SeedAdminAsync();
        var policy = EnabledPolicy();
        var (required, code) = await StartChallengeAsync(userId, policy);
        await using var firstContext = CreateDatabase();
        await using var secondContext = CreateDatabase();
        var first = CreateFlow(firstContext, policy, new CapturingLoginEmailSender());
        var second = CreateFlow(secondContext, policy, new CapturingLoginEmailSender());

        var results = await RunTogether(
            () => first.VerifyEmailCodeAsync(required.ChallengeToken, code, false, null),
            () => second.VerifyEmailCodeAsync(required.ChallengeToken, code, false, null));
        var outcomes = new[] { results.First, results.Second };

        outcomes.OfType<AuthSuccess>().Should().ContainSingle();
        outcomes.OfType<AuthRejected>().Should().ContainSingle();
        (await ActiveRefreshCountAsync(userId)).Should().Be(1);
        (await SingleChallengeAsync(userId)).ConsumedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TwoParallelWrongCodes_IncrementTheAttemptCounterByTwo()
    {
        var userId = await SeedAdminAsync();
        var policy = EnabledPolicy();
        var (required, _) = await StartChallengeAsync(userId, policy);
        await using var firstContext = CreateDatabase();
        await using var secondContext = CreateDatabase();
        var first = CreateFlow(firstContext, policy, new CapturingLoginEmailSender());
        var second = CreateFlow(secondContext, policy, new CapturingLoginEmailSender());

        var results = await RunTogether(
            () => first.VerifyEmailCodeAsync(required.ChallengeToken, "000000", false, null),
            () => second.VerifyEmailCodeAsync(required.ChallengeToken, "000001", false, null));

        results.First.Should().BeOfType<AuthRejected>();
        results.Second.Should().BeOfType<AuthRejected>();
        (await SingleChallengeAsync(userId)).AttemptCount.Should().Be(2);
        (await ReadQuotaAsync(userId)).FailedOtpCount.Should().Be(2);
        (await ReadUserAsync(userId)).FailedLoginCount.Should().Be(0);
    }

    [Fact]
    public async Task TwoParallelResends_AllowOnlyOneInsideTheCooldown()
    {
        var userId = await SeedAdminAsync();
        var policy = EnabledPolicy();
        var (required, _) = await StartChallengeAsync(userId, policy);
        await BackdateLastSendAsync(userId);
        await using var firstContext = CreateDatabase();
        await using var secondContext = CreateDatabase();
        var firstSender = new CapturingLoginEmailSender();
        var secondSender = new CapturingLoginEmailSender();
        var first = CreateFlow(firstContext, policy, firstSender);
        var second = CreateFlow(secondContext, policy, secondSender);

        var results = await RunTogether(
            () => first.ResendEmailCodeAsync(required.ChallengeToken),
            () => second.ResendEmailCodeAsync(required.ChallengeToken));
        var outcomes = new[] { results.First, results.Second };

        outcomes.OfType<ResendLoginCodeSent>().Should().ContainSingle();
        outcomes.OfType<ResendLoginCodeRejected>().Should().ContainSingle();
        (firstSender.Calls + secondSender.Calls).Should().Be(1);
        (await SingleChallengeAsync(userId)).SendCount.Should().Be(2);
        (await ReadQuotaAsync(userId)).SendCount.Should().Be(2);
    }

    [Fact]
    public async Task SixthSendWithinTheWindow_IsBlockedAcrossChallenges()
    {
        var userId = await SeedAdminAsync();
        var policy = EnabledPolicy(TimeSpan.Zero);
        var sender = new CapturingLoginEmailSender();
        AuthResult? last = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            last = await LoginAsync(userId, Password, policy, sender);
        }

        sender.Calls.Should().Be(5);
        last.Should().BeOfType<AuthRejected>();
        (await ReadQuotaAsync(userId)).SendCount.Should().Be(5);
        (await ChallengeCountAsync(userId)).Should().Be(5);
        (await ActiveRefreshCountAsync(userId)).Should().Be(0);
    }

    [Fact]
    public async Task Resend_ReplacesThePreviousCode()
    {
        var userId = await SeedAdminAsync();
        var policy = EnabledPolicy(TimeSpan.Zero);
        var sender = new CapturingLoginEmailSender();
        var started = (await LoginAsync(userId, Password, policy, sender)).Should().BeOfType<AuthVerificationRequired>().Subject;
        var firstCode = sender.Sent.Single().Code;

        var resent = await ResendAsync(started.ChallengeToken, policy, sender);

        resent.Should().BeOfType<ResendLoginCodeSent>();
        var secondCode = sender.Sent.Last().Code;
        secondCode.Should().NotBe(firstCode);
        (await VerifyAsync(started.ChallengeToken, firstCode, policy, rememberDevice: false)).Should().BeOfType<AuthRejected>();
        (await VerifyAsync(started.ChallengeToken, secondCode, policy, rememberDevice: false)).Should().BeOfType<AuthSuccess>();
    }

    [Fact]
    public async Task ThirdResendOnTheSameChallenge_IsRejected()
    {
        var userId = await SeedAdminAsync();
        var policy = EnabledPolicy(TimeSpan.Zero);
        var sender = new CapturingLoginEmailSender();
        var started = (await LoginAsync(userId, Password, policy, sender)).Should().BeOfType<AuthVerificationRequired>().Subject;

        (await ResendAsync(started.ChallengeToken, policy, sender)).Should().BeOfType<ResendLoginCodeSent>();
        (await ResendAsync(started.ChallengeToken, policy, sender)).Should().BeOfType<ResendLoginCodeSent>();
        (await ResendAsync(started.ChallengeToken, policy, sender)).Should().BeOfType<ResendLoginCodeRejected>();

        (await SingleChallengeAsync(userId)).SendCount.Should().Be(3);
        sender.Calls.Should().Be(3);
    }

    [Fact]
    public async Task TenWrongCodes_LockTheAccountForBothVerifyAndResend()
    {
        var userId = await SeedAdminAsync();
        var policy = EnabledPolicy(TimeSpan.Zero);
        var sender = new CapturingLoginEmailSender();
        var first = (await LoginAsync(userId, Password, policy, sender)).Should().BeOfType<AuthVerificationRequired>().Subject;
        var second = (await LoginAsync(userId, Password, policy, sender)).Should().BeOfType<AuthVerificationRequired>().Subject;
        var kept = (await LoginAsync(userId, Password, policy, sender)).Should().BeOfType<AuthVerificationRequired>().Subject;
        var keptCode = sender.Sent.Last().Code;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            (await VerifyAsync(first.ChallengeToken, "111111", policy, false)).Should().BeOfType<AuthRejected>();
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await VerifyAsync(second.ChallengeToken, "222222", policy, false)).Should().BeOfType<AuthRejected>();
        }

        (await VerifyAsync(first.ChallengeToken, "111111", policy, false)).Should().BeOfType<AuthRejected>();
        var callsBeforeLockout = sender.Calls;

        (await VerifyAsync(kept.ChallengeToken, keptCode, policy, false)).Should().BeOfType<AuthRejected>();
        (await ResendAsync(kept.ChallengeToken, policy, sender)).Should().BeOfType<ResendLoginCodeRejected>();
        (await LoginAsync(userId, Password, policy, sender)).Should().BeOfType<AuthRejected>();

        sender.Calls.Should().Be(callsBeforeLockout);
        var quota = await ReadQuotaAsync(userId);
        quota.FailedOtpCount.Should().Be(10);
        quota.OtpLockoutEnd.Should().BeAfter(DateTimeOffset.UtcNow);
        (await ReadUserAsync(userId)).FailedLoginCount.Should().Be(0);
        (await ActiveRefreshCountAsync(userId)).Should().Be(0);
        (await SingleOpenChallengeAsync(userId)).ConsumedAt.Should().BeNull();
    }

    [Fact]
    public async Task ExpiredCode_IsRejected()
    {
        var userId = await SeedAdminAsync();
        var policy = EnabledPolicy();
        var (required, code) = await StartChallengeAsync(userId, policy);
        await using (var context = CreateDatabase())
        {
            var expired = DateTimeOffset.UtcNow.AddMinutes(-1);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "LoginEmailChallenges" SET "ExpiresAt" = {expired} WHERE "AdminUserId" = {userId}""");
        }

        (await VerifyAsync(required.ChallengeToken, code, policy, false)).Should().BeOfType<AuthRejected>();
        (await ActiveRefreshCountAsync(userId)).Should().Be(0);
    }

    [Fact]
    public async Task TrustedDevice_SkipsEmailUntilItExpiresIsRevokedOrBelongsToSomeoneElse()
    {
        var owner = await SeedAdminAsync();
        var other = await SeedAdminAsync();
        var policy = EnabledPolicy(TimeSpan.Zero);
        var sender = new CapturingLoginEmailSender();
        var (required, code) = await StartChallengeAsync(owner, policy, sender);
        var verified = (await VerifyAsync(required.ChallengeToken, code, policy, rememberDevice: true))
            .Should().BeOfType<AuthSuccess>().Subject;
        var deviceToken = verified.TrustedDeviceToken!;

        sender.Calls.Should().Be(1);
        (await LoginAsync(owner, Password, policy, sender, deviceToken: deviceToken)).Should().BeOfType<AuthSuccess>();
        sender.Calls.Should().Be(1);

        var otherLogin = (await LoginAsync(other, Password, policy, sender, deviceToken: deviceToken))
            .Should().BeOfType<AuthVerificationRequired>().Subject;
        otherLogin.ClearTrustedDeviceCookie.Should().BeFalse();
        (await ReadDeviceAsync(owner)).RevokedAt.Should().BeNull();

        await using (var context = CreateDatabase())
        {
            var expired = DateTimeOffset.UtcNow.AddMinutes(-1);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "TrustedDevices" SET "ExpiresAt" = {expired} WHERE "AdminUserId" = {owner}""");
        }

        var expiredLogin = (await LoginAsync(owner, Password, policy, sender, deviceToken: deviceToken))
            .Should().BeOfType<AuthVerificationRequired>().Subject;
        expiredLogin.ClearTrustedDeviceCookie.Should().BeTrue();

        await using (var context = CreateDatabase())
        {
            var future = DateTimeOffset.UtcNow.AddDays(1);
            var revoked = DateTimeOffset.UtcNow;
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "TrustedDevices" SET "ExpiresAt" = {future}, "RevokedAt" = {revoked} WHERE "AdminUserId" = {owner}""");
        }

        var revokedLogin = (await LoginAsync(owner, Password, policy, sender, deviceToken: deviceToken))
            .Should().BeOfType<AuthVerificationRequired>().Subject;
        revokedLogin.ClearTrustedDeviceCookie.Should().BeTrue();
    }

    [Fact]
    public async Task PasswordChange_InvalidatesChallengesAndDevices()
    {
        var userId = await SeedAdminAsync();
        var policy = EnabledPolicy(TimeSpan.Zero);
        var sender = new CapturingLoginEmailSender();
        var (required, code) = await StartChallengeAsync(userId, policy, sender);
        var verified = (await VerifyAsync(required.ChallengeToken, code, policy, rememberDevice: true))
            .Should().BeOfType<AuthSuccess>().Subject;
        var next = (await LoginAsync(userId, Password, policy, sender)).Should().BeOfType<AuthVerificationRequired>().Subject;
        var nextCode = sender.Sent.Last().Code;

        var changed = await ChangePasswordAsync(userId, Password, "a-new-passphrase", policy);

        changed.Status.Should().Be(PasswordChangeStatus.Success);
        (await ReadUserAsync(userId)).TokenVersion.Should().Be(1);
        (await ReadDeviceAsync(userId)).RevokedAt.Should().NotBeNull();
        (await ActiveRefreshCountAsync(userId)).Should().Be(0);
        (await VerifyAsync(next.ChallengeToken, nextCode, policy, false)).Should().BeOfType<AuthRejected>();
        (await LoginAsync(userId, "a-new-passphrase", policy, sender, deviceToken: verified.TrustedDeviceToken))
            .Should().BeOfType<AuthVerificationRequired>();
    }

    [Fact]
    public async Task ConcurrentPasswordChangeAndVerify_CannotLeaveASessionFromTheOldLogin()
    {
        var userId = await SeedAdminAsync();
        var policy = EnabledPolicy();
        var (required, code) = await StartChallengeAsync(userId, policy);
        await using var verifyContext = CreateDatabase();
        await using var changeContext = CreateDatabase();
        var flow = CreateFlow(verifyContext, policy, new CapturingLoginEmailSender());
        var store = CreateStore(changeContext, policy);

        var results = await RunTogether(
            () => flow.VerifyEmailCodeAsync(required.ChallengeToken, code, false, null),
            () => store.ChangePasswordAsync(userId, Password, "a-new-passphrase"));

        results.Second.Status.Should().Be(PasswordChangeStatus.Success);
        (await ReadUserAsync(userId)).TokenVersion.Should().Be(1);
        (await ActiveRefreshCountAsync(userId)).Should().Be(0);
        (await VerifyAsync(required.ChallengeToken, code, policy, false)).Should().BeOfType<AuthRejected>();
    }

    [Fact]
    public async Task ConcurrentPasswordChangeAndTrustedDeviceLogin_RevokesTheDeviceAndSession()
    {
        var userId = await SeedAdminAsync();
        var policy = EnabledPolicy(TimeSpan.Zero);
        var (required, code) = await StartChallengeAsync(userId, policy);
        var verified = (await VerifyAsync(required.ChallengeToken, code, policy, rememberDevice: true))
            .Should().BeOfType<AuthSuccess>().Subject;
        await using var loginContext = CreateDatabase();
        await using var changeContext = CreateDatabase();
        var flow = CreateFlow(loginContext, policy, new CapturingLoginEmailSender());
        var store = CreateStore(changeContext, policy);

        var results = await RunTogether(
            () => flow.CompletePasswordLoginAsync(userId, Password, verified.TrustedDeviceToken),
            () => store.ChangePasswordAsync(userId, Password, "a-new-passphrase"));

        results.Second.Status.Should().Be(PasswordChangeStatus.Success);
        (await ReadDeviceAsync(userId)).RevokedAt.Should().NotBeNull();
        (await ActiveRefreshCountAsync(userId)).Should().Be(0);
        (await ReadUserAsync(userId)).TokenVersion.Should().Be(1);
    }

    [Fact]
    public async Task Logout_KeepsTheTrustedDevice()
    {
        var userId = await SeedAdminAsync();
        var policy = EnabledPolicy();
        var (required, code) = await StartChallengeAsync(userId, policy);
        var verified = (await VerifyAsync(required.ChallengeToken, code, policy, rememberDevice: true))
            .Should().BeOfType<AuthSuccess>().Subject;
        await using var context = CreateDatabase();
        var auth = new AuthService(
            new AdminUserRepository(context),
            new RefreshTokenRepository(context),
            _hasher,
            new FakeAccessTokens(),
            new UnusedGoogleVerifier(),
            CreateStore(context, policy),
            CreateFlow(context, policy, new CapturingLoginEmailSender()),
            policy,
            TimeProvider.System);

        await auth.LogoutAsync(verified.RefreshToken);

        (await ReadDeviceAsync(userId)).RevokedAt.Should().BeNull();
        (await ActiveRefreshCountAsync(userId)).Should().Be(0);
    }

    [Fact]
    public async Task GoogleLogin_IssuesASessionWithoutATrustedDevice()
    {
        var userId = await SeedAdminAsync();
        var sender = new CapturingLoginEmailSender();
        await using var context = CreateDatabase();
        var flow = CreateFlow(context, EnabledPolicy(), sender);

        var result = await flow.CompleteGoogleLoginAsync(userId, "google-subject");

        result.Should().BeOfType<AuthSuccess>();
        sender.Calls.Should().Be(0);
        (await DeviceCountAsync(userId)).Should().Be(0);
        (await ChallengeCountAsync(userId)).Should().Be(0);
        (await ReadUserAsync(userId)).GoogleSubject.Should().Be("google-subject");
    }

    [Fact]
    public async Task VerificationDisabled_IssuesASessionWithoutTheNewTablesBeingRequiredByTheFlow()
    {
        var userId = await SeedAdminAsync();
        var sender = new CapturingLoginEmailSender();

        var result = await LoginAsync(userId, Password, new AuthPolicy { LoginEmailVerification = false }, sender);

        result.Should().BeOfType<AuthSuccess>();
        sender.Calls.Should().Be(0);
        (await ChallengeCountAsync(userId)).Should().Be(0);
        (await ActiveRefreshCountAsync(userId)).Should().Be(1);
    }

    private async Task<AuthResult> LoginAsync(
        Guid userId,
        string password,
        AuthPolicy policy,
        CapturingLoginEmailSender sender,
        ILogger<LoginFlow>? logger = null,
        string? deviceToken = null)
    {
        await using var context = CreateDatabase();
        return await CreateFlow(context, policy, sender, logger).CompletePasswordLoginAsync(userId, password, deviceToken);
    }

    private async Task<(AuthVerificationRequired Required, string Code)> StartChallengeAsync(
        Guid userId,
        AuthPolicy policy,
        CapturingLoginEmailSender? sender = null)
    {
        sender ??= new CapturingLoginEmailSender();
        var result = await LoginAsync(userId, Password, policy, sender);
        return (result.Should().BeOfType<AuthVerificationRequired>().Subject, sender.Sent.Last().Code);
    }

    private async Task<AuthResult> VerifyAsync(string challengeToken, string code, AuthPolicy policy, bool rememberDevice)
    {
        await using var context = CreateDatabase();
        return await CreateFlow(context, policy, new CapturingLoginEmailSender())
            .VerifyEmailCodeAsync(challengeToken, code, rememberDevice, "Office");
    }

    private async Task<ResendLoginCodeResult> ResendAsync(string challengeToken, AuthPolicy policy, CapturingLoginEmailSender sender)
    {
        await using var context = CreateDatabase();
        return await CreateFlow(context, policy, sender).ResendEmailCodeAsync(challengeToken);
    }

    private async Task<PasswordChangeResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, AuthPolicy policy)
    {
        await using var context = CreateDatabase();
        return await CreateStore(context, policy).ChangePasswordAsync(userId, currentPassword, newPassword);
    }

    private static async Task<(T First, TSecond Second)> RunTogether<T, TSecond>(Func<Task<T>> first, Func<Task<TSecond>> second)
    {
        var gate = new Barrier(2);
        var firstTask = Task.Run(async () =>
        {
            gate.SignalAndWait();
            return await first();
        });
        var secondTask = Task.Run(async () =>
        {
            gate.SignalAndWait();
            return await second();
        });
        var work = Task.WhenAll(firstTask, secondTask);
        var completed = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(30)));
        completed.Should().BeSameAs(work, "the row locks should finish without a deadlock");
        await work;
        return (await firstTask, await secondTask);
    }

    private LoginFlow CreateFlow(
        AppDbContext context,
        AuthPolicy policy,
        ILoginVerificationEmailSender sender,
        ILogger<LoginFlow>? logger = null) =>
        new(
            context,
            _hasher,
            new FakeAccessTokens(),
            sender,
            policy,
            Microsoft.Extensions.Options.Options.Create(PepperOptions()),
            TimeProvider.System,
            logger ?? NullLogger<LoginFlow>.Instance);

    private AdminCredentialStore CreateStore(AppDbContext context, AuthPolicy policy) =>
        new(context, _hasher, policy, TimeProvider.System, NullLogger<AdminCredentialStore>.Instance);

    private static AuthPolicy EnabledPolicy(TimeSpan? minimumSendInterval = null) => new()
    {
        LoginEmailVerification = true,
        MinSendInterval = minimumSendInterval ?? TimeSpan.FromSeconds(60)
    };

    private static AuthOptions PepperOptions() => new()
    {
        LoginEmailVerification = true,
        LoginCodePepper = new string('p', 32),
        SigningKey = new string('s', 32)
    };

    private AppDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.ConnectionString).Options;
        return new AppDbContext(options);
    }

    private async Task<Guid> SeedAdminAsync()
    {
        await using var context = CreateDatabase();
        var email = $"{Guid.NewGuid():N}@example.com";
        var user = new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = AdminUser.NormalizeEmail(email),
            PasswordHash = _hasher.Hash(Password),
            Role = AdminUser.AdminRole,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.AdminUsers.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task BackdateLastSendAsync(Guid userId)
    {
        await using var context = CreateDatabase();
        var sentAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "LoginEmailQuotas" SET "LastSendAt" = {sentAt} WHERE "AdminUserId" = {userId}""");
    }

    private async Task<int> ChallengeCountAsync(Guid userId)
    {
        await using var context = CreateDatabase();
        return await context.LoginEmailChallenges.CountAsync(challenge => challenge.AdminUserId == userId);
    }

    private async Task<LoginEmailChallenge> SingleChallengeAsync(Guid userId)
    {
        await using var context = CreateDatabase();
        return await context.LoginEmailChallenges.SingleAsync(challenge => challenge.AdminUserId == userId);
    }

    private async Task<LoginEmailChallenge> SingleOpenChallengeAsync(Guid userId)
    {
        await using var context = CreateDatabase();
        return await context.LoginEmailChallenges.SingleAsync(challenge => challenge.AdminUserId == userId && challenge.ConsumedAt == null);
    }

    private async Task<int> ActiveRefreshCountAsync(Guid userId)
    {
        await using var context = CreateDatabase();
        return await context.RefreshTokens.CountAsync(token => token.AdminUserId == userId && token.RevokedAt == null);
    }

    private async Task<int> DeviceCountAsync(Guid userId)
    {
        await using var context = CreateDatabase();
        return await context.TrustedDevices.CountAsync(device => device.AdminUserId == userId);
    }

    private async Task<TrustedDevice> ReadDeviceAsync(Guid userId)
    {
        await using var context = CreateDatabase();
        return await context.TrustedDevices.SingleAsync(device => device.AdminUserId == userId);
    }

    private async Task<LoginEmailQuota> ReadQuotaAsync(Guid userId)
    {
        await using var context = CreateDatabase();
        return await context.LoginEmailQuotas.SingleAsync(quota => quota.AdminUserId == userId);
    }

    private async Task<AdminUser> ReadUserAsync(Guid userId)
    {
        await using var context = CreateDatabase();
        return await context.AdminUsers.SingleAsync(user => user.Id == userId);
    }

    private sealed class UnusedGoogleVerifier : IGoogleAccountVerifier
    {
        public Task<GoogleAccount?> VerifyAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Google was not part of this test.");
    }
}

public sealed class LoginVerificationSchemaPostgresTests : IClassFixture<PostgresAdminDatabase>, IAsyncLifetime
{
    private readonly PostgresAdminDatabase _database;
    private readonly IdentityPasswordHasher _hasher = new();

    public LoginVerificationSchemaPostgresTests(PostgresAdminDatabase database)
    {
        _database = database;
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateDatabase();
        await context.Database.ExecuteSqlRawAsync("""
            DROP TABLE IF EXISTS "LoginEmailChallenges";
            DROP TABLE IF EXISTS "TrustedDevices";
            DROP TABLE IF EXISTS "LoginEmailQuotas";
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FlagOff_LoginRefreshPasswordChangeAndDevResetWorkWithoutTheNewTables()
    {
        var userId = await SeedAdminAsync("admin@example.com", "current-password");
        var sender = new CapturingLoginEmailSender();
        await using var loginContext = CreateDatabase();
        var login = await new LoginFlow(
            loginContext,
            _hasher,
            new FakeAccessTokens(),
            sender,
            new AuthPolicy(),
            Microsoft.Extensions.Options.Options.Create(new AuthOptions()),
            TimeProvider.System,
            NullLogger<LoginFlow>.Instance).CompletePasswordLoginAsync(userId, "current-password", null);

        var session = login.Should().BeOfType<AuthSuccess>().Subject;
        sender.Calls.Should().Be(0);
        await using var refreshContext = CreateDatabase();
        var rotated = await new AdminCredentialStore(refreshContext, _hasher, new AuthPolicy(), TimeProvider.System, NullLogger<AdminCredentialStore>.Instance)
            .RotateAsync(session.RefreshToken);
        rotated.Should().BeOfType<SessionRotated>();

        await using var changeContext = CreateDatabase();
        var changed = await new AdminCredentialStore(changeContext, _hasher, new AuthPolicy(), TimeProvider.System, NullLogger<AdminCredentialStore>.Instance)
            .ChangePasswordAsync(userId, "current-password", "a-new-passphrase");
        changed.Status.Should().Be(PasswordChangeStatus.Success);

        var resetId = await SeedAdminAsync(DevAdminPasswordResetter.AllowedEmail, "current-password");
        await using var connection = new Npgsql.NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        var reset = await DevAdminPasswordResetter.ApplyAsync(connection, DevAdminPasswordResetter.AllowedEmail, "Ny-lösenord-åäö-1", CancellationToken.None);
        reset.Ok.Should().BeTrue();
        await using var verification = CreateDatabase();
        (await verification.AdminUsers.SingleAsync(user => user.Id == resetId)).TokenVersion.Should().Be(1);
    }

    [Fact]
    public async Task FlagOn_MissingTablesDoNotIssueASessionAndRollBackPasswordChange()
    {
        var userId = await SeedAdminAsync("flag-on@example.com", "current-password");
        var hash = (await ReadUserAsync(userId)).PasswordHash;
        await using var loginContext = CreateDatabase();
        var login = await CreateEnabledFlow(loginContext).CompletePasswordLoginAsync(userId, "current-password", null);
        login.Should().BeOfType<AuthRejected>();
        await using var verification = CreateDatabase();
        (await verification.RefreshTokens.CountAsync(token => token.AdminUserId == userId)).Should().Be(0);

        await using var changeContext = CreateDatabase();
        var changed = await new AdminCredentialStore(changeContext, _hasher, new AuthPolicy { LoginEmailVerification = true }, TimeProvider.System, NullLogger<AdminCredentialStore>.Instance)
            .ChangePasswordAsync(userId, "current-password", "a-new-passphrase");
        changed.Status.Should().Be(PasswordChangeStatus.Unavailable);
        (await ReadUserAsync(userId)).PasswordHash.Should().Be(hash);
    }

    [Fact]
    public async Task EnsureAsync_WhenVerificationIsEnabledAndTheTablesAreMissing_RefusesToStart()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<AuthOptions>>(Microsoft.Extensions.Options.Options.Create(new AuthOptions
        {
            LoginEmailVerification = true,
            LoginCodePepper = new string('p', 32),
            SigningKey = new string('s', 32)
        }));
        services.AddDbContext<AppDbContext>(db => db.UseNpgsql(_database.ConnectionString));
        await using var provider = services.BuildServiceProvider();

        var act = () => LoginVerificationStartup.EnsureAsync(provider, new DevelopmentHost(), NullLogger.Instance);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*missing*");
    }

    private LoginFlow CreateEnabledFlow(AppDbContext context) =>
        new(
            context,
            _hasher,
            new FakeAccessTokens(),
            new CapturingLoginEmailSender(),
            new AuthPolicy { LoginEmailVerification = true },
            Microsoft.Extensions.Options.Options.Create(new AuthOptions
            {
                LoginEmailVerification = true,
                LoginCodePepper = new string('p', 32),
                SigningKey = new string('s', 32)
            }),
            TimeProvider.System,
            NullLogger<LoginFlow>.Instance);

    private AppDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.ConnectionString).Options;
        return new AppDbContext(options);
    }

    private async Task<Guid> SeedAdminAsync(string email, string password)
    {
        await using var context = CreateDatabase();
        var user = new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = AdminUser.NormalizeEmail(email),
            PasswordHash = _hasher.Hash(password),
            Role = AdminUser.AdminRole,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.AdminUsers.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<AdminUser> ReadUserAsync(Guid userId)
    {
        await using var context = CreateDatabase();
        return await context.AdminUsers.SingleAsync(user => user.Id == userId);
    }

    private sealed class DevelopmentHost : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Microsoft.Extensions.Hosting.Environments.Development;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

internal sealed class CapturingLoginEmailSender : ILoginVerificationEmailSender
{
    private readonly object _gate = new();

    public bool Succeed { get; set; } = true;

    public int Calls { get; private set; }

    public List<(Guid AdminUserId, string Code)> Sent { get; } = [];

    public Task<bool> SendCodeAsync(
        Guid adminUserId,
        string recipientEmail,
        string code,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Calls++;
            Sent.Add((adminUserId, code));
        }

        return Task.FromResult(Succeed);
    }
}

internal sealed class FakeAccessTokens : IAccessTokenIssuer
{
    public AccessToken Issue(AdminUser user) =>
        new($"access.{user.Id:N}.{user.TokenVersion}", DateTimeOffset.UtcNow.AddMinutes(15));
}

internal sealed class CollectingLogger<T> : ILogger<T>
{
    private readonly StringBuilder _text = new();

    public string Text => _text.ToString();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _text.Append(formatter(state, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
