using Microsoft.Extensions.Logging;

namespace Nexaflow.Core.Services.Logging;

/// <summary>One entry as a destination receives it: who wrote it, how severe, its event id, its text, and what it carries.</summary>
/// <param name="Category">The category its logger was made for — the full name of the type that wrote it, for an <c>ILogger&lt;T&gt;</c>.</param>
/// <param name="Level">How severe it is.</param>
/// <param name="Id">The event it reports, where it names one.</param>
/// <param name="Message">Its text, its template filled in.</param>
/// <param name="Exception">The fault it records, if any.</param>
/// <param name="Fields">The template's named values, where it had a template.</param>
public readonly record struct ShellLogEntry(
    string Category,
    LogLevel Level,
    EventId Id,
    string Message,
    Exception? Exception,
    IReadOnlyList<KeyValuePair<string, object?>>? Fields);

/// <summary>
/// One destination of the session's logs (<see cref="ShellLogging"/>): an <see cref="ILoggerProvider"/> that decides which entries it
/// takes and writes each one it takes. Every logger the session's factory makes writes to every destination; each keeps to its own
/// level, file and format. A destination never throws: a log that cannot be written is one the process runs on without.
/// </summary>
public abstract class LogDestination : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new DestinationLogger(this, categoryName);

    /// <summary>Whether an entry of <paramref name="level"/> from <paramref name="category"/> is written here — asked before the
    /// entry's text is made, so an entry nothing takes costs nothing.</summary>
    protected abstract bool Takes(string category, LogLevel level);

    /// <summary>Writes an entry <see cref="Takes"/> accepted.</summary>
    protected abstract void Write(in ShellLogEntry entry);

    /// <summary>Writes out what is held and lets go of the file; nothing is written after it.</summary>
    public virtual void Dispose() { }

    private sealed class DestinationLogger(LogDestination destination, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && destination.Takes(category, logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                                Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            destination.Write(new ShellLogEntry(category, logLevel, eventId, formatter(state, exception), exception,
                                                state as IReadOnlyList<KeyValuePair<string, object?>>));
        }
    }
}
