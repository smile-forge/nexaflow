using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nexaflow.Syntax;

namespace Nexaflow.Visuals.Text.Editor.Highlighting;

/// <summary>
/// Draws the colour a token denotes underneath it — <c>#FF3B30</c>, <c>rgb(…)</c>, <c>Tomato</c>, or a
/// <c>{StaticResource AccentBrush}</c> whose key resolves to a brush in the running theme. Language-agnostic:
/// it works from the highlighter's spans, so XAML, CSS and HTML all get it from the same code.
///
/// <para>
/// A bar under the token rather than Visual Studio's square before it, deliberately: a square has to occupy
/// layout space, which means an inline element, which changes column arithmetic for the caret, selection and
/// folding. A background bar needs none of that and reads just as fast. It is drawn at the very bottom of the
/// line box, full token width, with a hairline outline so white and transparent stay visible.
/// </para>
/// </summary>
public sealed class ColorSwatchRenderer : IBackgroundRenderer
{
    private const double BarHeight = 3;

    // The brush is built here rather than in Draw: Draw runs on every render pass, and a swatch's colour only
    // changes when the document is reparsed.
    private IReadOnlyList<(int Start, int Length, Brush Brush)> _swatches = [];

    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>Recomputes the swatches from a fresh parse. Cheap: it only inspects spans that could name a colour.</summary>
    public void SetSpans(string text, string? grammarId, IReadOnlyList<HighlightSpan> spans)
    {
        var found = new List<(int, int, Brush)>();
        var alphaFirst = ColorLiterals.AlphaFirst(grammarId);

        foreach (var span in spans)
        {
            if (span.Start < 0 || span.Start + span.Length > text.Length || span.Length == 0) continue;
            if (!CouldNameAColour(span.Capture)) continue;

            var token = text.Substring(span.Start, span.Length);
            var colour = ColorLiterals.Parse(token, alphaFirst) ?? FromTheme(span.Capture, token);
            if (colour is not { } c || c.A == 0) continue;   // fully transparent has nothing to show

            var brush = new SolidColorBrush(c);   // the colour carries its own alpha; don't dim it twice
            brush.Freeze();
            found.Add((span.Start, span.Length, brush));
        }
        _swatches = found;
    }

    /// <summary>
    /// Only value-shaped roles are considered. Element and attribute <em>names</em> are excluded so a
    /// <c>&lt;Red&gt;</c> element or a <c>Beige</c> property never grows a swatch.
    /// </summary>
    private static bool CouldNameAColour(string capture) =>
        capture is "string" or "constant" or "variable" or "number";

    /// <summary>
    /// A resource key resolved against the running theme — which is what makes
    /// <c>{StaticResource AccentBrush}</c> previewable. Only <c>constant</c> spans are tried, and XAML marks a
    /// key that way only inside a <c>*Resource</c> extension, so a binding path is never looked up by accident.
    /// </summary>
    private static Color? FromTheme(string capture, string key)
    {
        if (capture != "constant") return null;
        return Application.Current?.TryFindResource(key) switch
        {
            SolidColorBrush brush => brush.Color,
            Color colour => colour,
            _ => null,
        };
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_swatches.Count == 0) return;
        textView.EnsureVisualLines();

        foreach (var (start, length, brush) in _swatches)
        {
            if (start + length > textView.Document.TextLength) continue;

            var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true };
            builder.AddSegment(textView, new TextSegment { StartOffset = start, Length = length });
            if (builder.CreateGeometry() is not { } geometry) continue;

            var box = geometry.Bounds;
            if (box.IsEmpty || box.Width <= 0) continue;

            drawingContext.DrawRectangle(brush, Outline,
                                         new Rect(box.Left, box.Bottom - BarHeight, box.Width, BarHeight));
        }
    }

    // A hairline so a white or near-transparent swatch is still a visible mark rather than nothing.
    private static readonly Pen Outline = CreateOutline();

    private static Pen CreateOutline()
    {
        var brush = Application.Current?.TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;
        var pen = new Pen(brush, 0.6);
        pen.Freeze();
        return pen;
    }
}
