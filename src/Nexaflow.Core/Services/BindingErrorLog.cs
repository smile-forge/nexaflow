using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;

namespace Nexaflow.Core.Services;

/// <summary>
/// The data-binding errors WPF reports, written down under UI automation so a journey can fail on them. WPF reports a
/// binding it cannot resolve — a property the bound object does not have, an ancestor that is not there — only to a
/// debugger, and leaves the target at its default: a card that should show options shows nothing, and nothing says so.
/// <para>
/// Each distinct error is written once, as its <see cref="SignatureOf">signature</see>, a line per error, to
/// <see cref="FileName"/> in the config root, beside the crash log. The journey harness reads it when the app is closed.
/// </para>
/// </summary>
public sealed partial class BindingErrorLog : TraceListener
{
    /// <summary>The file, in the config root, that holds one signature per line.</summary>
    public const string FileName = "binding-errors.log";

    private readonly Func<string>    _dir;
    private readonly HashSet<string> _written = new(StringComparer.Ordinal);
    private readonly object          _gate    = new();

    internal BindingErrorLog(Func<string> directory) => _dir = directory;

    /// <summary>
    /// Starts writing down every binding error the process reports into <paramref name="directory"/>, from now on. WPF only
    /// traces bindings to a debugger until its trace sources are refreshed, so this refreshes them first, then listens at
    /// <see cref="SourceLevels.Error"/>: the warnings below it are WPF's own retries that end in a value.
    /// </summary>
    public static BindingErrorLog Attach(Func<string> directory)
    {
        var log = new BindingErrorLog(directory);
        PresentationTraceSources.Refresh();
        var source = PresentationTraceSources.DataBindingSource;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(log);
        return log;
    }

    /// <summary>Stops listening. The file stays.</summary>
    public void Detach() => PresentationTraceSources.DataBindingSource.Listeners.Remove(this);

    /// <summary>
    /// The error as it is the same from one run to the next: WPF's own id and words, without the hash code it stamps on
    /// each object — the path, the type bound to, and the element and property that wanted the value all stay.
    /// </summary>
    public static string SignatureOf(int id, string message) =>
        $"{id.ToString(CultureInfo.InvariantCulture)}: {Spaces().Replace(HashCodes().Replace(message, string.Empty), " ").Trim()}";

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message) =>
        Record(id, message ?? string.Empty);

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args) =>
        Record(id, format is null ? string.Empty
                 : args is { Length: > 0 } ? string.Format(CultureInfo.InvariantCulture, format, args) : format);

    // WPF reports a binding error through TraceEvent alone; what reaches these is the source's own banner, not an error.
    public override void Write(string? message) { }

    public override void WriteLine(string? message) { }

    internal void Record(int id, string message)
    {
        var signature = SignatureOf(id, message);
        lock (_gate)
        {
            if (!_written.Add(signature)) return;
            try
            {
                var dir = _dir();
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, FileName), signature + Environment.NewLine);
            }
            catch (IOException) { /* the error is still reported by WPF to any debugger; losing the line loses only this run's record */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    [GeneratedRegex(@"\s*\(HashCode=-?\d+\)")]
    private static partial Regex HashCodes();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
