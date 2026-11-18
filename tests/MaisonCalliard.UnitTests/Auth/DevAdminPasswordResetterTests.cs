using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.DevAdminPasswordReset;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Infrastructure.Services;
using Npgsql;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class DevAdminPasswordResetterTests
{
    private const string DevRef = "devref00000000000000";
    private const string ProdRef = "prodref0000000000000";
    private const string OtherRef = "otherref000000000000";
    private const string DatabasePassword = "synthetic-password-not-real";
    private const string NewPassword = "Ny-lösenord-åäö-1";

    [Fact]
    public void ValidationError_AcceptsTheExplicitDevReference()
    {
        var error = DevAdminPasswordResetter.ValidationError(
            Connection(DevRef),
            DevRef.ToUpperInvariant(),
            ProdRef,
            DevAdminPasswordResetter.AllowedEmail,
            NewPassword);

        error.Should().BeNull();
    }

    [Fact]
    public void ValidationError_AcceptsADirectSupabaseHost()
    {
        var connection =
            $"Host=db.{DevRef}.supabase.co;Database=postgres;Username=postgres;Password={DatabasePassword};SSL Mode=VerifyFull";

        var error = DevAdminPasswordResetter.ValidationError(
            connection,
            DevRef,
            ProdRef,
            DevAdminPasswordResetter.AllowedEmail,
            NewPassword);

        error.Should().BeNull();
    }

    [Theory]
    [InlineData("other@example.com", "Endast info@fuegodanceschool.se kan återställas.")]
    [InlineData("info@fuegodanceschool.se ", "Endast info@fuegodanceschool.se kan återställas.")]
    public void ValidationError_RejectsEveryOtherEmail(string email, string message)
    {
        var error = DevAdminPasswordResetter.ValidationError(Connection(DevRef), DevRef, ProdRef, email, NewPassword);

        error.Should().Be(message);
        error.Should().NotContain(DatabasePassword);
    }

    [Fact]
    public void ValidationError_RejectsAWeakPasswordBeforeUsingTheConnection()
    {
        var error = DevAdminPasswordResetter.ValidationError(
            "this is not a connection string",
            DevRef,
            ProdRef,
            DevAdminPasswordResetter.AllowedEmail,
            "short-pass");

        error.Should().Be("Lösenordet uppfyller inte lösenordspolicyn. Inget ändrades.");
    }

    [Fact]
    public void ValidationError_RejectsTheSameDevAndProdProject()
    {
        var error = DevAdminPasswordResetter.ValidationError(
            Connection(DevRef),
            DevRef,
            DevRef,
            DevAdminPasswordResetter.AllowedEmail,
            NewPassword);

        error.Should().Be("DEV och PROD är samma Supabase-projekt.");
    }

    [Fact]
    public void ValidationError_RejectsAConnectionThatDoesNotMatchTheExplicitReference()
    {
        var error = DevAdminPasswordResetter.ValidationError(
            Connection(DevRef),
            OtherRef,
            ProdRef,
            DevAdminPasswordResetter.AllowedEmail,
            NewPassword);

        error.Should().Be("DevConnection matchar inte den angivna DEV-referensen.");
        error.Should().NotContain(DatabasePassword);
        error.Should().NotContain(DevRef);
    }

    [Fact]
    public void ValidationError_RejectsMissingTlsAndDisabledCertificateChecks()
    {
        DevAdminPasswordResetter.ValidationError(Connection(DevRef, ssl: null), DevRef, ProdRef, DevAdminPasswordResetter.AllowedEmail, NewPassword)
            .Should().Be("DevConnection kräver inte TLS.");
        DevAdminPasswordResetter.ValidationError(Connection(DevRef, ssl: "Disable"), DevRef, ProdRef, DevAdminPasswordResetter.AllowedEmail, NewPassword)
            .Should().Be("DevConnection kräver inte TLS.");
        DevAdminPasswordResetter.ValidationError(Connection(DevRef, trust: "true"), DevRef, ProdRef, DevAdminPasswordResetter.AllowedEmail, NewPassword)
            .Should().Be("DevConnection stänger av certifikatkontrollen.");
    }

    [Fact]
    public void ValidationError_RejectsPlaceholderPasswordsWithoutEchoingThem()
    {
        var error = DevAdminPasswordResetter.ValidationError(
            Connection(DevRef, password: "YOUR_LOCAL_PASSWORD_HERE"),
            DevRef,
            ProdRef,
            DevAdminPasswordResetter.AllowedEmail,
            NewPassword);

        error.Should().Be("DevConnection använder ett platshållarlösenord.");
        error.Should().NotContain("YOUR_LOCAL_PASSWORD_HERE");
    }

    [Fact]
    public async Task ResetAsync_RefusesAMismatchedReferenceWithoutConnecting()
    {
        var connection = $"Host=127.0.0.1;Port=1;Database=postgres;Username=postgres.{DevRef};Password={DatabasePassword};SSL Mode=Require;Timeout=1";

        var outcome = await DevAdminPasswordResetter.ResetAsync(
            connection,
            OtherRef,
            ProdRef,
            DevAdminPasswordResetter.AllowedEmail,
            NewPassword);

        outcome.Ok.Should().BeFalse();
        outcome.Message.Should().Be("DevConnection matchar inte den angivna DEV-referensen.");
        outcome.Message.Should().NotContain(DatabasePassword);
        outcome.Message.Should().NotContain("127.0.0.1");
    }

    [Fact]
    public async Task ApplyAsync_RejectsAnotherEmailAndAWeakPasswordWithoutADatabase()
    {
        await using var connection = new NpgsqlConnection();

        var otherEmail = await DevAdminPasswordResetter.ApplyAsync(connection, "other@example.com", NewPassword, CancellationToken.None);
        var weak = await DevAdminPasswordResetter.ApplyAsync(connection, DevAdminPasswordResetter.AllowedEmail, "short-pass", CancellationToken.None);

        otherEmail.Message.Should().Be("Endast info@fuegodanceschool.se kan återställas.");
        weak.Message.Should().Be("Lösenordet uppfyller inte lösenordspolicyn. Inget ändrades.");
    }

    [Fact]
    public void PasswordRulesMatchTheBackendAndDoNotTrimThePassword()
    {
        var spaced = "  a-new-passphrase  ";
        AdminPasswordPolicy.IsStrongEnough(NewPassword).Should().BeTrue();
        AdminPasswordPolicy.IsStrongEnough(spaced).Should().BeTrue();
        AdminPasswordPolicy.IsStrongEnough("a-new-pass\nphrase").Should().BeTrue();
        AdminPasswordPolicy.IsStrongEnough("   123456789").Should().BeFalse();
        AdminPasswordPolicy.IsStrongEnough(new string('x', 257)).Should().BeFalse();
        AdminPasswordPolicy.IsStrongEnough(new string('x', 256)).Should().BeTrue();

        DevAdminPasswordResetter.ValidationError(Connection(DevRef), DevRef, ProdRef, DevAdminPasswordResetter.AllowedEmail, spaced)
            .Should().BeNull();
        DevAdminPasswordResetter.ValidationError("not-a-connection", DevRef, ProdRef, DevAdminPasswordResetter.AllowedEmail, "   123456789")
            .Should().Be("Lösenordet uppfyller inte lösenordspolicyn. Inget ändrades.");
        DevAdminPasswordResetter.ValidationError("not-a-connection", DevRef, ProdRef, DevAdminPasswordResetter.AllowedEmail, new string('x', 257))
            .Should().Be("Lösenordet uppfyller inte lösenordspolicyn. Inget ändrades.");
    }

    [Fact]
    public void HashUsesTheBackendIdentityPasswordHasherFormat()
    {
        var password = "  a-new-passphrase  ";
        var produced = new PasswordHasher<AdminUser>().HashPassword(new AdminUser(), password);
        var backend = new IdentityPasswordHasher();

        backend.Verify(password, produced).Should().BeTrue();
        backend.Verify(password.Trim(), produced).Should().BeFalse();
        Convert.FromBase64String(produced)[0].Should().Be(0x01);
    }

    [Fact]
    public void ReadExactPassword_PreservesSpacesAndNewlines()
    {
        var password = "  a-new-pass\nphrase  ";
        var body = Encoding.UTF8.GetBytes(password);
        var payload = Encoding.ASCII.GetBytes(body.Length + "\n").Concat(body).ToArray();

        using var input = new MemoryStream(payload);
        MaisonCalliard.DevAdminPasswordReset.Program.ReadExactPassword(input).Should().Be(password);
    }

    [Fact]
    public void ReadExactPassword_RejectsInvalidUtf8InsteadOfReplacingIt()
    {
        var payload = Encoding.ASCII.GetBytes("1\n").Concat(new byte[] { 0xFF }).ToArray();

        using var input = new MemoryStream(payload);
        MaisonCalliard.DevAdminPasswordReset.Program.ReadExactPassword(input).Should().BeNull();
    }

    private static string Connection(string reference, string? password = null, string? ssl = "Require", string? trust = null)
    {
        var parts = new List<string>
        {
            "Host=aws-0-eu-west-1.pooler.supabase.com",
            "Database=postgres",
            $"Username=postgres.{reference}",
            $"Password={password ?? DatabasePassword}"
        };
        if (ssl is not null)
        {
            parts.Add($"SSL Mode={ssl}");
        }

        if (trust is not null)
        {
            parts.Add($"Trust Server Certificate={trust}");
        }

        return string.Join(";", parts);
    }
}

public sealed class DevAdminPasswordResetterDatabaseTests : IClassFixture<PostgresAdminDatabase>
{
    private readonly PostgresAdminDatabase _database;
    private const string NewPassword = "Ny-lösenord-åäö-1";

    public DevAdminPasswordResetterDatabaseTests(PostgresAdminDatabase database)
    {
        _database = database;
    }

    [Fact]
    public async Task ApplyAsync_ResetsOnlyTheSelectedActiveAccount()
    {
        await ClearAsync();
        var hasher = new IdentityPasswordHasher();
        var targetId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var revokedAt = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        await InsertAdminAsync(targetId, "Info@FuegoDanceSchool.se", hasher.Hash("current-password"), active: true, failed: 4, lockout: DateTimeOffset.UtcNow.AddHours(1), mustChange: false, version: 7);
        await InsertAdminAsync(otherId, "other@example.com", hasher.Hash("other-admin-pass"), active: true, failed: 2, lockout: null, mustChange: false, version: 3);
        var targetActiveToken = await InsertTokenAsync(targetId, "target-active-token", null);
        var targetRevokedToken = await InsertTokenAsync(targetId, "target-revoked-token", revokedAt);
        var otherToken = await InsertTokenAsync(otherId, "other-active-token", null);

        await using var connection = await OpenAsync();
        var outcome = await DevAdminPasswordResetter.ApplyAsync(connection, DevAdminPasswordResetter.AllowedEmail, NewPassword, CancellationToken.None);

        outcome.Ok.Should().BeTrue();
        outcome.Message.Should().Be("Lösenordet återställdes för info@fuegodanceschool.se. Kontot måste byta lösenord vid nästa inloggning. Aktiva sessioner för det kontot återkallades.");
        outcome.Message.Should().NotContain(NewPassword);

        var target = await ReadAdminAsync(targetId);
        var other = await ReadAdminAsync(otherId);
        hasher.Verify(NewPassword, target.PasswordHash).Should().BeTrue();
        hasher.Verify("current-password", target.PasswordHash).Should().BeFalse();
        hasher.Verify("other-admin-pass", other.PasswordHash).Should().BeTrue();
        hasher.Verify(NewPassword, other.PasswordHash).Should().BeFalse();
        target.FailedLoginCount.Should().Be(0);
        target.LockoutEnd.Should().BeNull();
        target.MustChangePassword.Should().BeTrue();
        target.TokenVersion.Should().Be(8);
        target.IsActive.Should().BeTrue();
        target.Email.Should().Be("Info@FuegoDanceSchool.se");
        other.FailedLoginCount.Should().Be(2);
        other.TokenVersion.Should().Be(3);
        other.MustChangePassword.Should().BeFalse();
        (await ReadRevokedAtAsync(targetActiveToken)).Should().NotBeNull();
        (await ReadRevokedAtAsync(targetRevokedToken)).Should().Be(revokedAt);
        (await ReadRevokedAtAsync(otherToken)).Should().BeNull();
    }

    [Fact]
    public async Task ApplyAsync_IncrementsTokenVersionAndRevokesTheSelectedAdminsTrustedDevices()
    {
        await ClearAsync();
        var hasher = new IdentityPasswordHasher();
        var targetId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await InsertAdminAsync(targetId, DevAdminPasswordResetter.AllowedEmail, hasher.Hash("current-password"), active: true, failed: 0, lockout: null, mustChange: false, version: 4);
        await InsertAdminAsync(otherId, "other@example.com", hasher.Hash("other-admin-pass"), active: true, failed: 0, lockout: null, mustChange: false, version: 1);
        var targetDevice = await InsertDeviceAsync(targetId, version: 4);
        var otherDevice = await InsertDeviceAsync(otherId, version: 1);

        await using var connection = await OpenAsync();
        var outcome = await DevAdminPasswordResetter.ApplyAsync(connection, DevAdminPasswordResetter.AllowedEmail, NewPassword, CancellationToken.None);

        outcome.Ok.Should().BeTrue();
        (await ReadAdminAsync(targetId)).TokenVersion.Should().Be(5);
        (await ReadDeviceRevokedAtAsync(targetDevice)).Should().NotBeNull();
        (await ReadDeviceRevokedAtAsync(otherDevice)).Should().BeNull();
    }

    [Fact]
    public async Task ApplyAsync_RollsBackPasswordVersionLockoutAndTokensTogether()
    {
        await ClearAsync();
        var hasher = new IdentityPasswordHasher();
        var targetId = Guid.NewGuid();
        var hash = hasher.Hash("current-password");
        var lockout = DateTimeOffset.UtcNow.AddHours(1);
        await InsertAdminAsync(targetId, DevAdminPasswordResetter.AllowedEmail, hash, active: true, failed: 4, lockout: lockout, mustChange: false, version: 7);
        var token = await InsertTokenAsync(targetId, "active-token", null);

        await using var connection = await OpenAsync();
        var outcome = await DevAdminPasswordResetter.ApplyAsync(
            connection,
            DevAdminPasswordResetter.AllowedEmail,
            NewPassword,
            CancellationToken.None,
            _ => throw new InvalidOperationException("stop before commit"));

        outcome.Ok.Should().BeFalse();
        outcome.Message.Should().Be("Återställningen avbröts. Inget ändrades.");
        outcome.Message.Should().NotContain(NewPassword);
        var target = await ReadAdminAsync(targetId);
        target.PasswordHash.Should().Be(hash);
        hasher.Verify(NewPassword, target.PasswordHash).Should().BeFalse();
        target.FailedLoginCount.Should().Be(4);
        target.LockoutEnd.Should().NotBeNull();
        target.MustChangePassword.Should().BeFalse();
        target.TokenVersion.Should().Be(7);
        (await ReadRevokedAtAsync(token)).Should().BeNull();
    }

    [Fact]
    public async Task ApplyAsync_StoresTheExactPasswordWithoutTrimming()
    {
        await ClearAsync();
        var hasher = new IdentityPasswordHasher();
        var targetId = Guid.NewGuid();
        var password = "  a-new-passphrase  ";
        await InsertAdminAsync(targetId, DevAdminPasswordResetter.AllowedEmail, hasher.Hash("current-password"), active: true, failed: 0, lockout: null, mustChange: false, version: 1);

        await using var connection = await OpenAsync();
        var outcome = await DevAdminPasswordResetter.ApplyAsync(connection, DevAdminPasswordResetter.AllowedEmail, password, CancellationToken.None);

        outcome.Ok.Should().BeTrue();
        var target = await ReadAdminAsync(targetId);
        hasher.Verify(password, target.PasswordHash).Should().BeTrue();
        hasher.Verify(password.Trim(), target.PasswordHash).Should().BeFalse();
        target.MustChangePassword.Should().BeTrue();
        target.TokenVersion.Should().Be(2);
    }

    [Fact]
    public async Task ApplyAsync_DoesNotChangeAnInactiveAccountOrItsSessions()
    {
        await ClearAsync();
        var hasher = new IdentityPasswordHasher();
        var targetId = Guid.NewGuid();
        var hash = hasher.Hash("current-password");
        await InsertAdminAsync(targetId, DevAdminPasswordResetter.AllowedEmail, hash, active: false, failed: 4, lockout: DateTimeOffset.UtcNow.AddHours(1), mustChange: true, version: 7);
        var token = await InsertTokenAsync(targetId, "inactive-token", null);

        await using var connection = await OpenAsync();
        var outcome = await DevAdminPasswordResetter.ApplyAsync(connection, DevAdminPasswordResetter.AllowedEmail, NewPassword, CancellationToken.None);

        outcome.Ok.Should().BeFalse();
        outcome.Message.Should().Be("Kontot är inaktivt. Inget ändrades.");
        var target = await ReadAdminAsync(targetId);
        target.PasswordHash.Should().Be(hash);
        target.FailedLoginCount.Should().Be(4);
        target.TokenVersion.Should().Be(7);
        target.MustChangePassword.Should().BeTrue();
        (await ReadRevokedAtAsync(token)).Should().BeNull();
    }

    [Fact]
    public async Task ApplyAsync_DoesNothingWhenTheAccountIsMissing()
    {
        await ClearAsync();
        var hasher = new IdentityPasswordHasher();
        var otherId = Guid.NewGuid();
        var hash = hasher.Hash("other-admin-pass");
        await InsertAdminAsync(otherId, "other@example.com", hash, active: true, failed: 1, lockout: null, mustChange: false, version: 1);
        var token = await InsertTokenAsync(otherId, "other-token", null);

        await using var connection = await OpenAsync();
        var outcome = await DevAdminPasswordResetter.ApplyAsync(connection, DevAdminPasswordResetter.AllowedEmail, NewPassword, CancellationToken.None);

        outcome.Ok.Should().BeFalse();
        outcome.Message.Should().Be("Kontot finns inte i DEV. Inget ändrades.");
        var other = await ReadAdminAsync(otherId);
        other.PasswordHash.Should().Be(hash);
        (await ReadRevokedAtAsync(token)).Should().BeNull();
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task ClearAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("""
            DELETE FROM "RefreshTokens";
            DELETE FROM "AdminUsers";
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertAdminAsync(
        Guid id,
        string email,
        string passwordHash,
        bool active,
        int failed,
        DateTimeOffset? lockout,
        bool mustChange,
        int version)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO "AdminUsers"
                ("Id", "Email", "NormalizedEmail", "PasswordHash", "GoogleSubject", "Role", "IsActive", "FailedLoginCount", "LockoutEnd", "CreatedAt", "MustChangePassword", "TokenVersion")
            VALUES
                (@id, @email, @normalized, @hash, NULL, @role, @active, @failed, @lockout, @created, @mustChange, @version)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("normalized", AdminUser.NormalizeEmail(email));
        command.Parameters.AddWithValue("hash", passwordHash);
        command.Parameters.AddWithValue("role", AdminUser.AdminRole);
        command.Parameters.AddWithValue("active", active);
        command.Parameters.AddWithValue("failed", failed);
        command.Parameters.Add(new NpgsqlParameter("lockout", NpgsqlTypes.NpgsqlDbType.TimestampTz)
        {
            Value = lockout ?? (object)DBNull.Value
        });
        command.Parameters.AddWithValue("created", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("mustChange", mustChange);
        command.Parameters.AddWithValue("version", version);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<Guid> InsertTokenAsync(Guid adminId, string tokenHash, DateTimeOffset? revokedAt)
    {
        var id = Guid.NewGuid();
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO "RefreshTokens"
                ("Id", "AdminUserId", "TokenHash", "ExpiresAt", "RevokedAt", "ReplacedByTokenId", "CreatedAt")
            VALUES
                (@id, @adminId, @hash, @expires, @revoked, NULL, @created)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("adminId", adminId);
        command.Parameters.AddWithValue("hash", tokenHash);
        command.Parameters.AddWithValue("expires", DateTimeOffset.UtcNow.AddDays(1));
        command.Parameters.Add(new NpgsqlParameter("revoked", NpgsqlTypes.NpgsqlDbType.TimestampTz)
        {
            Value = revokedAt ?? (object)DBNull.Value
        });
        command.Parameters.AddWithValue("created", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<AdminRow> ReadAdminAsync(Guid id)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT "Email", "PasswordHash", "IsActive", "FailedLoginCount", "LockoutEnd", "MustChangePassword", "TokenVersion"
            FROM "AdminUsers"
            WHERE "Id" = @id
            """, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return new AdminRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetBoolean(5),
            reader.GetInt32(6));
    }

    private async Task<Guid> InsertDeviceAsync(Guid adminId, int version)
    {
        var id = Guid.NewGuid();
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO "TrustedDevices"
                ("Id", "AdminUserId", "TokenVersion", "TokenHash", "Label", "CreatedAt", "ExpiresAt", "RevokedAt", "LastUsedAt")
            VALUES
                (@id, @adminId, @version, @hash, NULL, @created, @expires, NULL, NULL)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("adminId", adminId);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("hash", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("created", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("expires", DateTimeOffset.UtcNow.AddDays(30));
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<DateTimeOffset?> ReadDeviceRevokedAtAsync(Guid deviceId)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("""SELECT "RevokedAt" FROM "TrustedDevices" WHERE "Id" = @id""", connection);
        command.Parameters.AddWithValue("id", deviceId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0);
    }

    private async Task<DateTimeOffset?> ReadRevokedAtAsync(Guid tokenId)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT "RevokedAt"
            FROM "RefreshTokens"
            WHERE "Id" = @id
            """, connection);
        command.Parameters.AddWithValue("id", tokenId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0);
    }

    private sealed record AdminRow(
        string Email,
        string PasswordHash,
        bool IsActive,
        int FailedLoginCount,
        DateTimeOffset? LockoutEnd,
        bool MustChangePassword,
        int TokenVersion);
}
