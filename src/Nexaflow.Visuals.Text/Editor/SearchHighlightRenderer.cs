using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Editor;

/// <summary>
/// AvalonEdit background renderer that draws highlights behind search matches.
/// Shared: every AvalonEdit surface that answers a search (the Text viewer's find, the editor's
/// <see cref="FileTextEditorViewModel"/> search) paints matches the same way, from the same theme token.
/// </summary>
public sealed class SearchHighlightRenderer : IBackgroundRenderer
{
    // Themed (Search.Match); resolved lazily per instance, throws if the token is missing.
    private Brush? _brush;
    private Brush HighlightBrush => _brush ??= Application.Current?.Resources["Search.Match"] as Brush
        ?? throw new InvalidOperationException("Theme brush 'Search.Match' not found.");

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
