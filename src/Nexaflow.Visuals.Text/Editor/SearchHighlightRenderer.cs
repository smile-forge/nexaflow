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

    /// <summary>
    /// Which of <see cref="Highlights"/> is the one being looked at — outlined as well as washed, since a wash alone cannot say
    /// which of several it is — or -1 where none is.
    /// </summary>
    public int Current { get; set; } = -1;

    // Themed (AccentBrush), resolved lazily like the wash.
    private Pen? _outline;
    private Pen Outline => _outline ??= Outlined(Application.Current?.Resources["AccentBrush"] as Brush
        ?? throw new InvalidOperationException("Theme brush 'AccentBrush' not found."));

    private static Pen Outlined(Brush brush)
    {
        var pen = new Pen(brush, 1.5);
        pen.Freeze();
        return pen;
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Highlights.Count == 0) return;

        textView.EnsureVisualLines();

        for (var at = 0; at < Highlights.Count; at++)
        {
            var (offset, length) = Highlights[at];
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
                drawingContext.DrawGeometry(HighlightBrush, at == Current ? Outline : null, geo);
        }
    }
}
