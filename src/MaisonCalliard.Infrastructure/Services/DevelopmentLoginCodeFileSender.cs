using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MaisonCalliard.Infrastructure.Services;

internal sealed class DevelopmentLoginCodeFileSender : ILoginVerificationEmailSender
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly AuthOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly string? _path;

    public DevelopmentLoginCodeFileSender(
        IOptions<AuthOptions> options,
        IHostEnvironment environment)
        : this(options, environment, path: null)
    {
    }

    internal DevelopmentLoginCodeFileSender(
        IOptions<AuthOptions> options,
        IHostEnvironment environment,
        string? path)
    {
        _options = options.Value;
        _environment = environment;
        _path = path;
    }

    public async Task<bool> SendCodeAsync(
        Guid adminUserId,
        string recipientEmail,
        string code,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment() || !_options.DevCodeFile)
        {
            return false;
        }

        try
        {
            var path = string.IsNullOrWhiteSpace(_path) ? DefaultPath() : _path;
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            Directory.CreateDirectory(directory);
            var now = DateTimeOffset.UtcNow;
            var entries = await ReadAsync(path, cancellationToken);
            entries.RemoveAll(entry => entry.ExpiresAt <= now || entry.AdminUserId == adminUserId);
            entries.Add(new StoredCode(adminUserId, code, expiresAt));
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entries, JsonOptions), cancellationToken);
            Protect(temporary);
            File.Move(temporary, path, overwrite: true);
            Protect(path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("Local application data is unavailable.");
        }

        return Path.Combine(root, "MaisonCalliard", "dev-login-codes.json");
    }

    private static async Task<List<StoredCode>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        var entries = await JsonSerializer.DeserializeAsync<List<StoredCode>>(stream, cancellationToken: cancellationToken);
        return entries ?? [];
    }

    internal static void Protect(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var file = new FileInfo(path);
            var security = file.GetAccessControl();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The current user has no security identifier."),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            file.SetAccessControl(security);
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private sealed record StoredCode(Guid AdminUserId, string Code, DateTimeOffset ExpiresAt);
}
