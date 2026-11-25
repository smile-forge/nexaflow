namespace Nexaflow.Core.Services.Logging;

/// <summary>
/// A run of the same fault close together. A fault the shell handles can recur as fast as it is handled — one thrown from the
/// WPF render pass re-throws on every frame — and a customer sent us a 170MB crash log that was a single exception 17,724 times
/// in 85 seconds. So identical faults are counted as one burst while each follows the last within <see cref="Window"/> of the
/// burst's start: the logs write the first few in full and count the rest (<see cref="CrashLogProvider"/>,
/// <see cref="SessionLogProvider"/>), and the dispatcher's fault handling stops handling one that has become a live-lock
/// (<see cref="DispatcherFaultPolicy"/>). Not thread-safe: each owner counts under its own lock.
/// </summary>
public sealed class FaultBurst
{
    /// <summary>How long after its start an identical fault still belongs to the burst.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private string?        _signature;
    private DateTimeOffset _start;

    /// <summary>Occurrences of the burst so far, the one just noted among them.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Counts one occurrence of the fault <paramref name="signature"/> names, at <paramref name="now"/>. True when it starts a
    /// burst — a different fault, or the same one after the window — which ends the one before.
    /// </summary>
    public bool Note(string signature, DateTimeOffset now)
    {
        bool starts = signature != _signature || now - _start > Window;
        if (starts)
        {
            _signature = signature;
            _start     = now;
            Count      = 0;
        }
        Count++;
        return starts;
    }

    /// <summary>How long the burst has run, at <paramref name="now"/>.</summary>
    public TimeSpan Length(DateTimeOffset now) => now - _start;

    /// <summary>Ends the burst: whatever is noted next starts one.</summary>
    public void End() => _signature = null;

    /// <summary>Type, message and top frame — enough to call two faults "the same" without holding the trace.</summary>
    public static string SignatureOf(Exception ex)
    {
        var trace   = ex.StackTrace ?? "";
        var newline = trace.IndexOf('\n');
        var top     = (newline < 0 ? trace : trace[..newline]).Trim();
        return $"{ex.GetType().FullName}|{ex.Message}|{top}";
    }
}
