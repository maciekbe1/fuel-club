using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FuelClub.Api.Tests;

/// <summary>Keeps every formatted log message of the test host, so tests can assert what never gets logged.</summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _messages);

    public void Dispose() { }

    private sealed class Logger(string category, ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) =>
            messages.Enqueue(
                $"{logLevel} {category}: {formatter(state, exception)} {state} {exception}"
            );
    }
}
