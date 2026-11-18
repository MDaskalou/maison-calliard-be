using System.Data;
using MaisonCalliard.Infrastructure.Data;
using MaisonCalliard.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MaisonCalliard.Infrastructure.Services;

public static class LoginVerificationStartup
{
    public static async Task EnsureAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>().Value;
        if (options.DevCodeFile && !environment.IsDevelopment())
        {
            SecurityEvents.Write(logger, LogLevel.Error, "login_verification_configuration_error", null);
            throw new InvalidOperationException("Auth:DevCodeFile is only allowed when the host environment is Development.");
        }

        if (!options.LoginEmailVerification)
        {
            return;
        }

        if (!options.HasSeparateLoginCodePepper())
        {
            SecurityEvents.Write(logger, LogLevel.Error, "login_verification_configuration_error", null);
            throw new InvalidOperationException(
                "Auth:LoginCodePepper must be a separate random value of at least 32 characters when login email verification is enabled.");
        }

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT to_regclass('public."LoginEmailChallenges"') IS NOT NULL
                   AND to_regclass('public."TrustedDevices"') IS NOT NULL
                   AND to_regclass('public."LoginEmailQuotas"') IS NOT NULL
                """;
            var present = await command.ExecuteScalarAsync(cancellationToken);
            if (present is not true)
            {
                SecurityEvents.Write(logger, LogLevel.Error, "login_verification_configuration_error", null);
                throw new InvalidOperationException(
                    "Login email verification is enabled, but LoginEmailChallenges, TrustedDevices, or LoginEmailQuotas is missing.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception) when (IsConnectivityFailure(exception))
        {
            SecurityEvents.Database(logger, exception, null);
        }
    }

    private static bool IsConnectivityFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException or NpgsqlException)
            {
                return current is not PostgresException postgres || postgres.SqlState is null || postgres.SqlState.StartsWith("08", StringComparison.Ordinal);
            }
        }

        return false;
    }
}
