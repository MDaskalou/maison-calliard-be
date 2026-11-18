using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MaisonCalliard.Infrastructure.Services;

internal static class SecurityEvents
{
    public static void Write(ILogger logger, LogLevel level, string securityEvent, Guid? adminUserId) =>
        logger.Log(
            level,
            "Security event {SecurityEvent} at {EventAt:o} for admin {AdminUserId}",
            securityEvent,
            DateTimeOffset.UtcNow,
            adminUserId);

    public static void Database(ILogger logger, Exception exception, Guid? adminUserId) =>
        logger.Log(
            LogLevel.Error,
            "Security event {SecurityEvent} at {EventAt:o} for admin {AdminUserId}. ExceptionType {ExceptionType}. SqlState {SqlState}. CorrelationId {CorrelationId}",
            "login_verification_database_error",
            DateTimeOffset.UtcNow,
            adminUserId,
            exception.GetType().Name,
            FindSqlState(exception),
            Activity.Current?.TraceId.ToString());

    private static string? FindSqlState(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres.SqlState;
            }
        }

        return null;
    }
}
