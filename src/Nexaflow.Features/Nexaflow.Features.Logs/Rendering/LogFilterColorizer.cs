using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nexaflow.Features.Logs.Parsing;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Logs.Rendering;

/// <summary>
/// Fades out lines that do not match the active regex filter and/or fall
/// outside the time-stamp range. Attach to
/// <c>TextArea.TextView.LineTransformers</c> after
/// <see cref="LogLevelColorizer"/>.
/// </summary>
public sealed class LogFilterColorizer : DocumentColorizingTransformer
{
    // Themed (Log.Filter) by default; resolved lazily so a caller can still override it. Throws if the
    // token is missing rather than silently using a literal.
    private Brush? _faded;
    public Brush FadedBrush
    {
        get => _faded ??= Application.Current?.Resources["Log.Filter"] as Brush
            ?? throw new InvalidOperationException("Theme brush 'Log.Filter' not found.");
        set => _faded = value;
    }

    public Regex? ActiveFilter     { get; set; }
    public (DateTime? Start, DateTime? End)? TimestampRange { get; set; }
    public ILogParser? Parser      { get; set; }

    public bool IsActive => ActiveFilter is not null || TimestampRange.HasValue;

    protected override void ColorizeLine(DocumentLine line)
    {
        if (!IsActive) return;

        var text   = CurrentContext.Document.GetText(line.Offset, line.Length);
        var passes = true;

        if (ActiveFilter is not null)
            passes = ActiveFilter.IsMatch(text);

        if (passes && TimestampRange.HasValue && Parser is not null)
        {
            var parsed = Parser.ParseLine(text);
            if (parsed.Timestamp.HasValue)
            {
                var ts            = parsed.Timestamp.Value;
                var (start, end)  = TimestampRange.Value;
                if (start.HasValue && ts < start.Value) passes = false;
                if (end.HasValue   && ts > end.Value)   passes = false;
            }
        }

        if (!passes)
            ChangeLinePart(line.Offset, line.EndOffset,
                el => el.TextRunProperties.SetForegroundBrush(FadedBrush));
    }
}
