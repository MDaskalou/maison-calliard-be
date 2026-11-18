using System.Data;
using System.Text.RegularExpressions;
using MaisonCalliard.Application.Auth;
using MaisonCalliard.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace MaisonCalliard.DevAdminPasswordReset;

internal static class DevAdminPasswordResetter
{
    public const string ConnectionEnvironmentVariable = "MAISON_DEV_ADMIN_RESET_CONNECTION";

    public const string AllowedEmail = "info@fuegodanceschool.se";

    private static readonly Regex ProjectRefPattern = new("^[a-z0-9]{20}$", RegexOptions.CultureInvariant);

    private static readonly Regex UsernameRefPattern = new("^postgres\\.([a-z0-9]{20})$", RegexOptions.CultureInvariant);

    private static readonly Regex DatabaseHostRefPattern = new("^db\\.([a-z0-9]{20})\\.supabase\\.co$", RegexOptions.CultureInvariant);

    private static readonly Regex ApiHostRefPattern = new("^([a-z0-9]{20})\\.supabase\\.co$", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> PlaceholderPasswords = new(StringComparer.Ordinal)
    {
        "your_local_password_here",
        "password",
        "postgres",
        "changeme",
        "change_me",
        "placeholder",
        "secret",
        "your-password",
        "your_password",
        "replace_me",
        "todo",
        "***",
        "xxx"
    };

    public static async Task<ResetOutcome> ResetAsync(
        string? connectionString,
        string? expectedDevRef,
        string? prodRef,
        string? email,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var error = ValidationError(connectionString, expectedDevRef, prodRef, email, password);
        if (error is not null || string.IsNullOrWhiteSpace(connectionString))
        {
            return ResetOutcome.Fail(error ?? "DevConnection saknas. Inget ändrades.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return await ApplyAsync(connection, email, password, cancellationToken);
        }
        catch (Exception)
        {
            return ResetOutcome.Fail("Anslutningen till DEV misslyckades. Inget ändrades.");
        }
    }

    public static string? ValidationError(
        string? connectionString,
        string? expectedDevRef,
        string? prodRef,
        string? email,
        string? password)
    {
        if (!string.Equals(email, AllowedEmail, StringComparison.Ordinal))
        {
            return "Endast info@fuegodanceschool.se kan återställas.";
        }

        if (!AdminPasswordPolicy.IsStrongEnough(password))
        {
            return "Lösenordet uppfyller inte lösenordspolicyn. Inget ändrades.";
        }

        var expected = (expectedDevRef ?? "").Trim().ToLowerInvariant();
        var prod = (prodRef ?? "").Trim().ToLowerInvariant();
        if (!ProjectRefPattern.IsMatch(expected))
        {
            return "Den förväntade DEV-referensen saknas eller har fel format.";
        }

        if (!ProjectRefPattern.IsMatch(prod))
        {
            return "PROD-referensen saknas eller har fel format.";
        }

        if (string.Equals(expected, prod, StringComparison.Ordinal))
        {
            return "DEV och PROD är samma Supabase-projekt.";
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "DevConnection saknas. Inget ändrades.";
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            return "DevConnection är inte en giltig PostgreSQL-anslutningssträng.";
        }

        if (string.IsNullOrWhiteSpace(builder.Host)
            || string.IsNullOrWhiteSpace(builder.Username)
            || string.IsNullOrWhiteSpace(builder.Database))
        {
            return "DevConnection är inte en giltig PostgreSQL-anslutningssträng.";
        }

        if (string.IsNullOrWhiteSpace(builder.Password))
        {
            return "DevConnection saknar lösenord.";
        }

        if (IsPlaceholderPassword(builder.Password))
        {
            return "DevConnection använder ett platshållarlösenord.";
        }

        if (builder.SslMode is not (SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull))
        {
            return "DevConnection kräver inte TLS.";
        }

#pragma warning disable CS0618
        if (builder.TrustServerCertificate)
        {
            return "DevConnection stänger av certifikatkontrollen.";
        }
#pragma warning restore CS0618

        if (!TryReadProjectRef(builder.Username, builder.Host, out var projectRef))
        {
            return "DevConnection saknar Supabase-projektreferens.";
        }

        if (!string.Equals(projectRef, expected, StringComparison.Ordinal))
        {
            return "DevConnection matchar inte den angivna DEV-referensen.";
        }

        if (string.Equals(projectRef, prod, StringComparison.Ordinal))
        {
            return "DEV och PROD är samma Supabase-projekt.";
        }

        return null;
    }

    internal static async Task<ResetOutcome> ApplyAsync(
        NpgsqlConnection connection,
        string? email,
        string? password,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? beforeCommit = null)
    {
        if (!string.Equals(email, AllowedEmail, StringComparison.Ordinal))
        {
            return ResetOutcome.Fail("Endast info@fuegodanceschool.se kan återställas.");
        }

        if (!AdminPasswordPolicy.IsStrongEnough(password))
        {
            return ResetOutcome.Fail("Lösenordet uppfyller inte lösenordspolicyn. Inget ändrades.");
        }

        var exactPassword = password!;
        var hasher = new PasswordHasher<AdminUser>();
        var passwordHash = hasher.HashPassword(new AdminUser(), exactPassword);
        if (passwordHash.Length > 512 || !PasswordMatches(hasher, exactPassword, passwordHash))
        {
            return ResetOutcome.Fail("Återställningen avbröts. Inget ändrades.");
        }

        var normalizedEmail = AdminUser.NormalizeEmail(AllowedEmail);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var matches = await ReadMatchesAsync(connection, transaction, normalizedEmail, cancellationToken);
            if (matches.Count == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ResetOutcome.Fail("Kontot finns inte i DEV. Inget ändrades.");
            }

            if (matches.Count != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ResetOutcome.Fail("Fler än ett konto matchade. Inget ändrades.");
            }

            var account = matches[0];
            if (!string.Equals(account.Email.Trim(), AllowedEmail, StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync(cancellationToken);
                return ResetOutcome.Fail("Kontot matchade inte exakt. Inget ändrades.");
            }

            if (!account.IsActive)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ResetOutcome.Fail("Kontot är inaktivt. Inget ändrades.");
            }

            await using (var update = CreateCommand(connection, transaction, """
                UPDATE "AdminUsers"
                SET "PasswordHash" = @hash,
                    "FailedLoginCount" = 0,
                    "LockoutEnd" = NULL,
                    "MustChangePassword" = TRUE,
                    "TokenVersion" = "TokenVersion" + 1
                WHERE "Id" = @id
                  AND "NormalizedEmail" = @normalized
                  AND "IsActive" = TRUE
                  AND (SELECT COUNT(*) FROM "AdminUsers" WHERE "NormalizedEmail" = @normalized) = 1
                """))
            {
                update.Parameters.AddWithValue("hash", passwordHash);
                update.Parameters.AddWithValue("id", account.Id);
                update.Parameters.AddWithValue("normalized", normalizedEmail);
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return ResetOutcome.Fail("Återställningen avbröts. Inget ändrades.");
                }
            }

            await using (var devices = CreateCommand(connection, transaction, """SELECT to_regclass('public."TrustedDevices"')::text"""))
            {
                var table = await devices.ExecuteScalarAsync(cancellationToken);
                if (table is string)
                {
                    await using var revokeDevices = CreateCommand(connection, transaction, """
                        UPDATE "TrustedDevices"
                        SET "RevokedAt" = @now
                        WHERE "AdminUserId" = @id
                          AND "RevokedAt" IS NULL
                        """);
                    revokeDevices.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
                    revokeDevices.Parameters.AddWithValue("id", account.Id);
                    await revokeDevices.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await using (var revoke = CreateCommand(connection, transaction, """
                UPDATE "RefreshTokens"
                SET "RevokedAt" = @now
                WHERE "AdminUserId" = @id
                  AND "RevokedAt" IS NULL
                """))
            {
                revoke.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
                revoke.Parameters.AddWithValue("id", account.Id);
                await revoke.ExecuteNonQueryAsync(cancellationToken);
            }

            string? storedHash = null;
            var changeIsConsistent = false;
            await using (var readBack = CreateCommand(connection, transaction, """
                SELECT "PasswordHash", "MustChangePassword", "TokenVersion", "FailedLoginCount", "LockoutEnd"
                FROM "AdminUsers"
                WHERE "Id" = @id
                  AND "NormalizedEmail" = @normalized
                """))
            {
                readBack.Parameters.AddWithValue("id", account.Id);
                readBack.Parameters.AddWithValue("normalized", normalizedEmail);
                await using (var reader = await readBack.ExecuteReaderAsync(cancellationToken))
                {
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        storedHash = reader.IsDBNull(0) ? null : reader.GetString(0);
                        var mustChangePassword = !reader.IsDBNull(1) && reader.GetBoolean(1);
                        var tokenVersion = reader.IsDBNull(2) ? -1 : reader.GetInt32(2);
                        var failedLoginCount = reader.IsDBNull(3) ? -1 : reader.GetInt32(3);
                        var lockoutCleared = reader.IsDBNull(4);
                        changeIsConsistent = storedHash is not null
                            && mustChangePassword
                            && tokenVersion == account.TokenVersion + 1
                            && failedLoginCount == 0
                            && lockoutCleared
                            && PasswordMatches(hasher, exactPassword, storedHash);
                    }
                }
            }

            if (!changeIsConsistent || storedHash is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ResetOutcome.Fail("Återställningen avbröts. Inget ändrades.");
            }

            await using (var activeTokens = CreateCommand(connection, transaction, """
                SELECT COUNT(*)
                FROM "RefreshTokens"
                WHERE "AdminUserId" = @id
                  AND "RevokedAt" IS NULL
                """))
            {
                activeTokens.Parameters.AddWithValue("id", account.Id);
                var activeCount = Convert.ToInt64(await activeTokens.ExecuteScalarAsync(cancellationToken));
                if (activeCount != 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return ResetOutcome.Fail("Återställningen avbröts. Inget ändrades.");
                }
            }

            await using (var others = CreateCommand(connection, transaction, """
                SELECT COUNT(*)
                FROM "AdminUsers"
                WHERE "PasswordHash" = @hash
                  AND "Id" <> @id
                """))
            {
                others.Parameters.AddWithValue("hash", storedHash);
                others.Parameters.AddWithValue("id", account.Id);
                var otherCount = Convert.ToInt64(await others.ExecuteScalarAsync(cancellationToken));
                if (otherCount != 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return ResetOutcome.Fail("Återställningen avbröts. Inget ändrades.");
                }
            }

            if (beforeCommit is not null)
            {
                await beforeCommit(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return ResetOutcome.Success(
                "Lösenordet återställdes för info@fuegodanceschool.se. Kontot måste byta lösenord vid nästa inloggning. Aktiva sessioner för det kontot återkallades.");
        }
        catch (Exception)
        {
            await SafeRollbackAsync(transaction, cancellationToken);
            return ResetOutcome.Fail("Återställningen avbröts. Inget ändrades.");
        }
    }

    private static async Task<List<AdminMatch>> ReadMatchesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var matches = new List<AdminMatch>();
        await using var select = CreateCommand(connection, transaction, """
            SELECT "Id", "Email", "IsActive", "TokenVersion"
            FROM "AdminUsers"
            WHERE "NormalizedEmail" = @normalized
            FOR UPDATE
            """);
        select.Parameters.AddWithValue("normalized", normalizedEmail);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(new AdminMatch(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                !reader.IsDBNull(2) && reader.GetBoolean(2),
                reader.IsDBNull(3) ? 0 : reader.GetInt32(3)));
        }

        return matches;
    }

    private static NpgsqlCommand CreateCommand(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        return new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 15
        };
    }

    private static bool TryReadProjectRef(string username, string host, out string projectRef)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);
        var usernameMatch = UsernameRefPattern.Match(username.Trim());
        if (usernameMatch.Success)
        {
            refs.Add(usernameMatch.Groups[1].Value);
        }

        var hostName = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (!hostName.EndsWith(".pooler.supabase.com", StringComparison.Ordinal))
        {
            var databaseHost = DatabaseHostRefPattern.Match(hostName);
            if (databaseHost.Success)
            {
                refs.Add(databaseHost.Groups[1].Value);
            }

            var apiHost = ApiHostRefPattern.Match(hostName);
            if (apiHost.Success)
            {
                refs.Add(apiHost.Groups[1].Value);
            }
        }

        if (refs.Count != 1)
        {
            projectRef = "";
            return false;
        }

        projectRef = refs.First();
        return true;
    }

    private static bool PasswordMatches(PasswordHasher<AdminUser> hasher, string password, string passwordHash)
    {
        var result = hasher.VerifyHashedPassword(new AdminUser(), passwordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }

    private static bool IsPlaceholderPassword(string password)
    {
        var normalized = password.Trim().ToLowerInvariant();
        return PlaceholderPasswords.Contains(normalized)
            || normalized.Contains("placeholder", StringComparison.Ordinal)
            || normalized.StartsWith("your_", StringComparison.Ordinal)
            || normalized.Contains("changeme", StringComparison.Ordinal)
            || normalized.Contains("change_me", StringComparison.Ordinal);
    }

    private static async Task SafeRollbackAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception)
        {
            // The database already aborted the transaction.
        }
    }

    private sealed record AdminMatch(Guid Id, string Email, bool IsActive, int TokenVersion);
}

internal sealed record ResetOutcome(bool Ok, string Message)
{
    public static ResetOutcome Success(string message) => new(true, message);

    public static ResetOutcome Fail(string message) => new(false, message);
}
