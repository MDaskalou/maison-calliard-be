using FluentAssertions;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Infrastructure.Data;
using MaisonCalliard.Infrastructure.Options;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class LoginVerificationGuardTests
{
    [Fact]
    public async Task EnsureAsync_WhenDevCodeFileIsEnabledOutsideDevelopment_RefusesToStart()
    {
        await using var services = CreateServices(connectionString: null, new AuthOptions { DevCodeFile = true });

        var act = () => LoginVerificationStartup.EnsureAsync(services, new TestEnvironment(development: false), NullLogger.Instance);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*DevCodeFile*");
    }

    [Fact]
    public async Task EnsureAsync_WhenVerificationIsEnabledWithoutASeparatePepper_RefusesToStart()
    {
        var signingKey = new string('s', 32);
        await using var services = CreateServices(connectionString: null, new AuthOptions
        {
            LoginEmailVerification = true,
            SigningKey = signingKey,
            LoginCodePepper = signingKey
        });

        var act = () => LoginVerificationStartup.EnsureAsync(services, new TestEnvironment(development: true), NullLogger.Instance);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*LoginCodePepper*");
    }

    [Fact]
    public async Task EnsureAsync_WhenThePepperIsTheKnownPlaceholder_RefusesToStart()
    {
        await using var services = CreateServices(connectionString: null, new AuthOptions
        {
            LoginEmailVerification = true,
            SigningKey = new string('s', 32),
            LoginCodePepper = AuthOptions.PlaceholderSigningKey
        });

        var act = () => LoginVerificationStartup.EnsureAsync(services, new TestEnvironment(development: true), NullLogger.Instance);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*LoginCodePepper*");
    }

    [Fact]
    public async Task EnsureAsync_WhenTheDatabaseCannotBeReached_DoesNotStopTheHost()
    {
        await using var services = CreateServices(UnreachableConnection, EnabledOptions());

        var act = () => LoginVerificationStartup.EnsureAsync(services, new TestEnvironment(development: true), NullLogger.Instance);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CompletePasswordLogin_WhenTheDatabaseCannotBeReached_DoesNotIssueASession()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(UnreachableConnection).Options;
        await using var context = new AppDbContext(options);
        var flow = new LoginFlow(
            context,
            new IdentityPasswordHasher(),
            new FakeAccessTokens(),
            new CapturingLoginEmailSender(),
            new AuthPolicy { LoginEmailVerification = true },
            Microsoft.Extensions.Options.Options.Create(EnabledOptions()),
            TimeProvider.System,
            NullLogger<LoginFlow>.Instance);

        var result = await flow.CompletePasswordLoginAsync(Guid.NewGuid(), "current-password", trustedDeviceToken: null);

        result.Should().BeOfType<AuthRejected>();
    }

    private const string UnreachableConnection = "Host=127.0.0.1;Port=1;Username=u;Password=p;Database=db;Timeout=2;Command Timeout=2";

    private static AuthOptions EnabledOptions() => new()
    {
        LoginEmailVerification = true,
        LoginCodePepper = new string('p', 32),
        SigningKey = new string('s', 32)
    };

    private static ServiceProvider CreateServices(string? connectionString, AuthOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<AuthOptions>>(Microsoft.Extensions.Options.Options.Create(options));
        if (connectionString is not null)
        {
            services.AddDbContext<AppDbContext>(db => db.UseNpgsql(connectionString));
        }

        return services.BuildServiceProvider();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public TestEnvironment(bool development) => EnvironmentName = development ? Environments.Development : Environments.Production;

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
