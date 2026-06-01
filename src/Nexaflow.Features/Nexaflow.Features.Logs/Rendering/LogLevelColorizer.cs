using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nexaflow.Features.Logs.Parsing;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Logs.Rendering;

/// <summary>
/// AvalonEdit line transformer that tints the background of lines whose log
/// level is in <see cref="EnabledLevels"/>. Attach to
/// <c>TextArea.TextView.LineTransformers</c>.
/// </summary>
public sealed class LogLevelColorizer : DocumentColorizingTransformer
{
    // Resolved from the active theme's Log.* tokens (shipped via the Logs theme contribution); lazily
    // built per instance, and a colorizer is created per view — recreated on theme switch, so it stays
    // current. A missing token throws (named) rather than silently painting a literal.
    private Dictionary<LogLevel, Brush>? _levelBrushes;
    private Dictionary<LogLevel, Brush> LevelBrushes => _levelBrushes ??= new()
    {
        [LogLevel.Fatal]   = Res("Log.Fatal"),
        [LogLevel.Error]   = Res("Log.Error"),
        [LogLevel.Warning] = Res("Log.Warning"),
        [LogLevel.Info]    = Res("Log.Info"),
        [LogLevel.Debug]   = Res("Log.Debug"),
    };

    private static Brush Res(string key)
        => Application.Current?.Resources[key] as Brush
           ?? throw new InvalidOperationException($"Theme brush '{key}' not found.");

    public HashSet<LogLevel> EnabledLevels { get; set; } = [];
    public ILogParser? Parser { get; set; }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (EnabledLevels.Count == 0 || Parser is null) return;

        var text  = CurrentContext.Document.GetText(line.Offset, line.Length);
        var level = Parser.ParseLine(text).Level;

        if (level == LogLevel.Unknown || !EnabledLevels.Contains(level)) return;
        if (!LevelBrushes.TryGetValue(level, out var brush)) return;

        ChangeLinePart(line.Offset, line.EndOffset,
            el => el.TextRunProperties.SetBackgroundBrush(brush));
    }
}
