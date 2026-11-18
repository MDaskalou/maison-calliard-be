using System.Net.Http;
using System.Security.AccessControl;
using System.Text.Json;
using FluentAssertions;
using MaisonCalliard.Infrastructure.Options;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class DevelopmentLoginCodeFileSenderTests
{
    [Fact]
    public async Task SendCodeAsync_WhenTheFileFlagIsOff_DoesNotWriteACode()
    {
        var path = Path.Combine(Path.GetTempPath(), "mc-codes", Guid.NewGuid().ToString("N"), "codes.json");
        var sender = CreateSender(path, devCodeFile: false, development: true);

        var sent = await sender.SendCodeAsync(Guid.NewGuid(), "admin@example.com", "123456", DateTimeOffset.UtcNow.AddMinutes(5));

        sent.Should().BeFalse();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task SendCodeAsync_OutsideDevelopment_DoesNotWriteACode()
    {
        var path = Path.Combine(Path.GetTempPath(), "mc-codes", Guid.NewGuid().ToString("N"), "codes.json");
        var sender = CreateSender(path, devCodeFile: true, development: false);

        var sent = await sender.SendCodeAsync(Guid.NewGuid(), "admin@example.com", "123456", DateTimeOffset.UtcNow.AddMinutes(5));

        sent.Should().BeFalse();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task SendCodeAsync_RemovesExpiredCodesAndKeepsOnlyTheLatestForTheAdmin()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mc-codes", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "codes.json");
        var sender = CreateSender(path, devCodeFile: true, development: true);
        var adminId = Guid.NewGuid();

        (await sender.SendCodeAsync(adminId, "admin@example.com", "111111", DateTimeOffset.UtcNow.AddMinutes(-1))).Should().BeTrue();
        (await sender.SendCodeAsync(adminId, "admin@example.com", "222222", DateTimeOffset.UtcNow.AddMinutes(5))).Should().BeTrue();

        var json = await File.ReadAllTextAsync(path);
        json.Should().Contain("222222");
        json.Should().NotContain("111111");
        json.Should().NotContain("admin@example.com");
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetArrayLength().Should().Be(1);
        if (OperatingSystem.IsWindows())
        {
            new FileInfo(path).GetAccessControl().AreAccessRulesProtected.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ResendSender_InDevelopment_DoesNotCallResend()
    {
        var sender = new ResendLoginVerificationEmailSender(
            new ThrowingHttpClientFactory(),
            Options.Create(new ResendOptions { Enabled = true, ApiKey = "re_test" }),
            Options.Create(new ReceiptOptions { FromEmail = "receipts@example.com", FromName = "Maison" }),
            new TestEnvironment(development: true));

        var sent = await sender.SendCodeAsync(Guid.NewGuid(), "admin@example.com", "123456", DateTimeOffset.UtcNow.AddMinutes(5));

        sent.Should().BeFalse();
    }

    private static DevelopmentLoginCodeFileSender CreateSender(string path, bool devCodeFile, bool development) =>
        new(
            Options.Create(new AuthOptions { DevCodeFile = devCodeFile }),
            new TestEnvironment(development),
            path);

    private sealed class TestEnvironment : IHostEnvironment
    {
        public TestEnvironment(bool development) => EnvironmentName = development ? Environments.Development : Environments.Production;

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Development must not call Resend.");
    }
}
