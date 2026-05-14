using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Text.Rendering;

/// <summary>
/// AvalonEdit background renderer that draws gold highlights behind search matches.
/// </summary>
public sealed class SearchHighlightRenderer : IBackgroundRenderer
{
    private static readonly SolidColorBrush HighlightBrush =
        new(Color.FromArgb(100, 255, 200, 0));

    public IReadOnlyList<(int offset, int length)> Highlights { get; set; } = [];

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Highlights.Count == 0) return;

        textView.EnsureVisualLines();

        foreach (var (offset, length) in Highlights)
        {
            if (offset < 0 || offset + length > textView.Document.TextLength) continue;

            var segment = new ICSharpCode.AvalonEdit.Document.TextSegment
            {
                StartOffset = offset,
                Length      = length,
            };

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
