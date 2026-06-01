using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Logs.Rendering;

/// <summary>
/// AvalonEdit background renderer that draws a green highlight behind
/// every occurrence of a user-supplied custom highlight term. Attach to
/// <c>TextArea.TextView.BackgroundRenderers</c>.
/// </summary>
public sealed class CustomTermHighlightRenderer : IBackgroundRenderer
{
    // Themed (Log.Term); resolved lazily per instance, throws if the token is missing.
    private Brush? _brush;
    private Brush HighlightBrush => _brush ??= Application.Current?.Resources["Log.Term"] as Brush
        ?? throw new InvalidOperationException("Theme brush 'Log.Term' not found.");

    public IReadOnlyList<(int offset, int length)> Highlights { get; set; } = [];

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Highlights.Count == 0) return;

        textView.EnsureVisualLines();

        foreach (var (offset, length) in Highlights)
        {
            if (offset < 0 || offset + length > textView.Document.TextLength) continue;

            var segment = new TextSegment { StartOffset = offset, Length = length };
            var builder = new BackgroundGeometryBuilder
            {
                AlignToWholePixels = true,
                CornerRadius       = 2,
            };
            builder.AddSegment(textView, segment);

            var geo = builder.CreateGeometry();
            if (geo is not null)
                drawingContext.DrawGeometry(HighlightBrush, null, geo);
        }
    }
}
