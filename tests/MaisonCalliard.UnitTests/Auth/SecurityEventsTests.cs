using System.Diagnostics;
using FluentAssertions;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MaisonCalliard.UnitTests.Auth;

public sealed class SecurityEventsTests
{
    private const string Secret = "refresh-token-secret-do-not-log";
    private const string Sql = "SELECT secret_parameter FROM \"RefreshTokens\"";

    [Fact]
    public void Database_LogsSqlStateAndOmitsExceptionDetails()
    {
        var adminId = Guid.NewGuid();
        var logger = new RecordingLogger();
        var exception = new InvalidOperationException(
            $"wrapped {Secret} {Sql}",
            new PostgresException(Sql + " " + Secret, "ERROR", "ERROR", "42P01"));

        SecurityEvents.Database(logger, exception, adminId);

        var message = logger.Messages.Should().ContainSingle().Subject;
        message.Should().Contain("login_verification_database_error");
        message.Should().Contain(adminId.ToString());
        message.Should().Contain(nameof(InvalidOperationException));
        message.Should().Contain("42P01");
        message.Should().NotContain(Secret);
        message.Should().NotContain("secret_parameter");
        message.Should().NotContain("RefreshTokens");
        logger.Exceptions.Should().OnlyContain(exception => exception == null);
    }

    [Fact]
    public void Database_IncludesTheCurrentCorrelationId()
    {
        var logger = new RecordingLogger();
        using var activity = new Activity("security-event-test");
        activity.Start();

        SecurityEvents.Database(logger, new TimeoutException("timeout"), Guid.NewGuid());

        logger.Messages.Should().ContainSingle().Which.Should().Contain(activity.TraceId.ToString());
        logger.Exceptions.Should().OnlyContain(exception => exception == null);
    }

    [Fact]
    public void Database_OmitsCorrelationIdWhenNoActivityExists()
    {
        Activity.Current.Should().BeNull();
        var logger = new RecordingLogger();

        SecurityEvents.Database(logger, new TimeoutException(Secret), null);

        var message = logger.Messages.Should().ContainSingle().Subject;
        message.Should().Contain(nameof(TimeoutException));
        message.Should().Contain("SqlState");
        message.Should().NotContain(Secret);
        logger.Exceptions.Should().OnlyContain(exception => exception == null);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public List<Exception?> Exceptions { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Exceptions.Add(exception);
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
