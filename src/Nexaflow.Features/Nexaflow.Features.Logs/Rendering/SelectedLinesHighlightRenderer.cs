using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Logs.Rendering;

/// <summary>
/// AvalonEdit background renderer that draws a persistent highlight behind
/// every line that appears in the caller-supplied selected-line set.
/// Attach to <c>TextArea.TextView.BackgroundRenderers</c>.
/// </summary>
public sealed class SelectedLinesHighlightRenderer : IBackgroundRenderer
{
    // Themed (Log.SelectionLine); resolved lazily per instance, throws if missing.
    private Brush? _brush;
    private Brush SelectionBrush => _brush ??= Application.Current?.Resources["Log.SelectionLine"] as Brush
        ?? throw new InvalidOperationException("Theme brush 'Log.SelectionLine' not found.");

    public IReadOnlySet<int> SelectedLines { get; set; } = new HashSet<int>();

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (SelectedLines.Count == 0) return;

        textView.EnsureVisualLines();

        foreach (var lineNumber in SelectedLines)
        {
            DocumentLine docLine;
            try   { docLine = textView.Document.GetLineByNumber(lineNumber); }
            catch { continue; }

            var segment = new TextSegment
            {
                StartOffset = docLine.Offset,
                Length      = docLine.TotalLength,
            };

            var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true };
            builder.AddSegment(textView, segment);

            var geo = builder.CreateGeometry();
            if (geo is not null)
                drawingContext.DrawGeometry(SelectionBrush, null, geo);
        }
    }
}
