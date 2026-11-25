using System.Diagnostics;
using System.Globalization;
using System.Windows;
using Microsoft.Extensions.Logging;
using Nexaflow.Core.Services.Logging;

namespace Nexaflow.Core.Services;

/// <summary>
/// WPF's side of the binding-error log: listens to WPF's data-binding trace and logs each error it reports under
/// <see cref="BindingErrorLogProvider.Category"/>, WPF's own id as the event id, for the destination that writes them down under UI
/// automation (<see cref="BindingErrorLogProvider"/>). Attached by the app under <c>--uiTest</c> only.
/// </summary>
public sealed class BindingErrorTrace : TraceListener
{
    private readonly ILogger _log;

    private BindingErrorTrace(ILogger log) => _log = log;

    /// <summary>
    /// Starts logging every binding error the process reports to <paramref name="logs"/>, from now on. WPF only traces bindings to a
    /// debugger until its trace sources are refreshed, so this refreshes them first, then listens at <see cref="SourceLevels.Error"/>:
    /// the warnings below it are WPF's own retries that end in a value.
    /// </summary>
    public static BindingErrorTrace Attach(ILoggerFactory logs)
    {
        var trace = new BindingErrorTrace(logs.CreateLogger(BindingErrorLogProvider.Category));
        PresentationTraceSources.Refresh();
        var source = PresentationTraceSources.DataBindingSource;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(trace);
        return trace;
    }

    /// <summary>Stops listening. What was written stays.</summary>
    public void Detach() => PresentationTraceSources.DataBindingSource.Listeners.Remove(this);

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message) =>
        Report(id, message ?? string.Empty);

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args) =>
        Report(id, format is null ? string.Empty
                 : args is { Length: > 0 } ? string.Format(CultureInfo.InvariantCulture, format, args) : format);

    // WPF reports a binding error through TraceEvent alone; what reaches these is the source's own banner, not an error.
    public override void Write(string? message) { }

    public override void WriteLine(string? message) { }

    // The error's words go in as a value, never as the template: they are WPF's, and may hold braces.
    private void Report(int id, string message) => _log.LogWarning(new EventId(id), "{BindingError}", message);
}
