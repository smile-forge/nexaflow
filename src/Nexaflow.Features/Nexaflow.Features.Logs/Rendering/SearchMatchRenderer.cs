using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Logs.Rendering;

/// <summary>
/// AvalonEdit background renderer that paints the matches of a <c>?</c> page search.
/// <para>Deliberately a sibling of <see cref="CustomTermHighlightRenderer"/> rather than a reuse of it:
/// the custom term is the user's own persistent marker (case-insensitive substring, green) while a search
/// match is transient and word-aware, and a log can show both at once. One renderer would make the two
/// fight over the same span list.</para>
/// </summary>
public sealed class SearchMatchRenderer : IBackgroundRenderer
{
    // Themed (Search.Match); resolved lazily per instance, throws if the token is missing.
    private Brush? _brush;
    private Brush HighlightBrush => _brush ??= Application.Current?.Resources["Search.Match"] as Brush
        ?? throw new InvalidOperationException("Theme brush 'Search.Match' not found.");

    public IReadOnlyList<(int offset, int length)> Highlights { get; set; } = [];

    // Same layer as the custom-term renderer; added after it, so on an overlap the search wash reads last.
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
