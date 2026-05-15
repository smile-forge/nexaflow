using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nexaflow.Features.Logs.Parsing;
using System.Windows.Media;

namespace Nexaflow.Features.Logs.Rendering;

/// <summary>
/// AvalonEdit line transformer that tints the background of lines whose log
/// level is in <see cref="EnabledLevels"/>. Attach to
/// <c>TextArea.TextView.LineTransformers</c>.
/// </summary>
public sealed class LogLevelColorizer : DocumentColorizingTransformer
{
    private static readonly Dictionary<LogLevel, SolidColorBrush> LevelBrushes = new()
    {
        [LogLevel.Fatal]   = new SolidColorBrush(Color.FromArgb(70,  200,  20,  60)),
        [LogLevel.Error]   = new SolidColorBrush(Color.FromArgb(60,  255,  69,  58)),
        [LogLevel.Warning] = new SolidColorBrush(Color.FromArgb(60,  255, 165,   0)),
        [LogLevel.Info]    = new SolidColorBrush(Color.FromArgb(40,   30, 144, 255)),
        [LogLevel.Debug]   = new SolidColorBrush(Color.FromArgb(30,  128, 128, 128)),
    };

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
