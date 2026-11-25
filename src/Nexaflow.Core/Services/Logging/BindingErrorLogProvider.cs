using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Nexaflow.Core.Services.Logging;

/// <summary>
/// The data-binding errors the UI toolkit reports, written down under UI automation (<c>--uiTest</c>) so a journey can fail on them.
/// WPF reports a binding it cannot resolve — a property the bound object does not have, an ancestor that is not there — only to a
/// debugger, and leaves the target at its default: a card that should show options shows nothing, and nothing says so. The app's
/// listener on WPF's binding trace logs each one under <see cref="Category"/>, WPF's own id as the event id; this destination takes
/// that category alone.
/// <para>
/// Each distinct error is written once, as its <see cref="SignatureOf">signature</see>, a line per error, to <see cref="FileName"/>
/// in the configuration folder, beside the crash log. The journey harness reads it when the app is closed.
/// </para>
/// </summary>
public sealed partial class BindingErrorLogProvider : LogDestination
{
    /// <summary>The file, in the configuration folder, that holds one signature per line.</summary>
    public const string FileName = "binding-errors.log";

    /// <summary>The category the binding errors are logged under.</summary>
    public const string Category = "Nexaflow.Bindings";

    private readonly Func<string>    _dir;
    private readonly HashSet<string> _written = new(StringComparer.Ordinal);
    private readonly Lock            _gate    = new();

    /// <param name="directory">The configuration folder, where <see cref="FileName"/> is written.</param>
    public BindingErrorLogProvider(Func<string> directory) => _dir = directory;

    /// <summary>
    /// The error as it is the same from one run to the next: WPF's own id and words, without the hash code it stamps on each
    /// object — the path, the type bound to, and the element and property that wanted the value all stay.
    /// </summary>
    public static string SignatureOf(int id, string message) =>
        $"{id.ToString(CultureInfo.InvariantCulture)}: {Spaces().Replace(HashCodes().Replace(message, string.Empty), " ").Trim()}";

    protected override bool Takes(string category, LogLevel level) => category == Category;

    protected override void Write(in ShellLogEntry entry)
    {
        var signature = SignatureOf(entry.Id.Id, entry.Message);
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
