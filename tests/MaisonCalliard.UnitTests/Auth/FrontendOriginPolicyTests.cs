using System.Text.Json;
using FluentAssertions;
using MaisonCalliard.Api.Auth;
using Microsoft.Extensions.Configuration;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class FrontendOriginPolicyTests
{
    [Fact]
    public void Create_WhenBrowserOriginsAreMissing_DoesNotThrowAndFailsClosed()
    {
        var policy = FrontendOriginPolicy.Create(Config(
            ("Cors:AllowedOrigins:0", "https://www.maisoncalliard.com")));

        policy.BrowserOrigins.Should().BeEmpty();
        policy.IsAllowedBrowserOrigin("https://www.maisoncalliard.com").Should().BeFalse();
    }

    [Fact]
    public void Create_WhenAnAdminOriginIsMissingFromCors_Throws()
    {
        var act = () => FrontendOriginPolicy.Create(Config(
            ("Cors:AllowedOrigins:0", "https://www.maisoncalliard.com"),
            ("Auth:AllowedBrowserOrigins:0", "https://admin.example:8443")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*CORS*");
    }

    [Theory]
    [InlineData("https://*.netlify.app")]
    [InlineData("*")]
    [InlineData("https://admin.example/admin")]
    [InlineData("null")]
    public void Create_WhenAnOriginIsNotExplicit_Throws(string origin)
    {
        var act = () => FrontendOriginPolicy.Create(Config(("Cors:AllowedOrigins:0", origin)));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Normalize_ComparesSchemeHostAndPort()
    {
        FrontendOriginPolicy.Normalize("HTTPS://Admin.Example:443/").Should().Be("https://admin.example");
        FrontendOriginPolicy.Normalize("https://admin.example:8443").Should().Be("https://admin.example:8443");
        FrontendOriginPolicy.Normalize("http://localhost:5173").Should().Be("http://localhost:5173");
    }

    [Fact]
    public void Create_WhenCsvIsAPlainString_BindsEveryCommaSeparatedOrigin()
    {
        var policy = FrontendOriginPolicy.Create(Config(
            ("Cors:AllowedOriginsCsv", "https://admin.example,https://staging-admin.example,https://www.maisoncalliard.com"),
            ("Auth:AllowedBrowserOriginsCsv", "https://admin.example, https://staging-admin.example")));

        policy.BrowserOrigins.Should().BeEquivalentTo(
            "https://admin.example",
            "https://staging-admin.example");
        policy.IsAllowedBrowserOrigin("https://admin.example").Should().BeTrue();
        policy.IsAllowedBrowserOrigin("https://staging-admin.example").Should().BeTrue();
        policy.IsAllowedBrowserOrigin("https://www.maisoncalliard.com").Should().BeFalse();
    }

    [Fact]
    public void Create_WhenACsvAdminOriginIsMissingFromCors_Throws()
    {
        var act = () => FrontendOriginPolicy.Create(Config(
            ("Cors:AllowedOriginsCsv", "https://www.maisoncalliard.com"),
            ("Auth:AllowedBrowserOriginsCsv", "https://admin.example,https://staging-admin.example")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*CORS*");
    }

    [Fact]
    public void Create_WhenTheCsvValueIsBlank_FailsClosed()
    {
        var policy = FrontendOriginPolicy.Create(Config(
            ("Cors:AllowedOrigins:0", "https://www.maisoncalliard.com"),
            ("Auth:AllowedBrowserOriginsCsv", "   ")));

        policy.BrowserOrigins.Should().BeEmpty();
        policy.IsAllowedBrowserOrigin("https://www.maisoncalliard.com").Should().BeFalse();
    }

    [Fact]
    public void DeployedAppsettings_DoesNotGrantLocalhostAdminOrigins()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(DeployedAppsettingsPath()));
        var auth = document.RootElement.GetProperty("Auth");

        auth.TryGetProperty("AllowedBrowserOrigins", out _).Should().BeFalse();
    }

    private static string DeployedAppsettingsPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "MaisonCalliard.Api", "appsettings.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find the deployed appsettings.json.");
    }

    private static IConfiguration Config(params (string Key, string Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => item.Key, item => (string?)item.Value))
            .Build();
    }
}
