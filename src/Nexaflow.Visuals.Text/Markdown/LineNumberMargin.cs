using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Each line's number beside it, counting from one: set in the face and size the content is set in as typed, so a number stands on its
/// line's first row as the line's own characters do; right-aligned in a column as wide as the document's last number; and beside only
/// the first row of a line wrapped onto several.
/// </summary>
public sealed class LineNumberMargin : ContentMargin
{
    /// <summary>Room before the numbers.</summary>
    private const double Lead = 8;

    /// <summary>Room between the numbers and the page.</summary>
    private const double Gap = 12;

    /// <summary>The fewest digits the column is wide enough for, so a short document's column does not widen as it grows past nine lines.</summary>
    private const int FewestDigits = 2;

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(LineNumberMargin),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>What the numbers are drawn in: the theme's muted text, unless set — and where the theme has none, the ink the surface's text is set in.</summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public LineNumberMargin() => SetResourceReference(ForegroundProperty, "TextMutedBrush");

    /// <summary>As wide as the document's last line's number, set as the numbers are, with room either side.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Surface is not { } surface) return default;

        var digits = Math.Max(FewestDigits, surface.Document.Current.LineCount.ToString(CultureInfo.InvariantCulture).Length);

        return new Size(Lead + Numeral(surface, new string('0', digits), Brushes.Transparent).WidthIncludingTrailingWhitespace + Gap, 0);
    }

    protected override void Draw(DrawingContext drawing, IReadOnlyList<ShownLine> lines)
    {
        if (Surface is not { } surface) return;

        var ink = Foreground ?? surface.TypedIn.Ink;

        foreach (var line in lines)
        {
            var number = Numeral(surface, line.Number.ToString(CultureInfo.InvariantCulture), ink);
            drawing.DrawText(number, new Point(ActualWidth - Gap - number.WidthIncludingTrailingWhitespace, line.Top));
        }
    }

    /// <summary><paramref name="digits"/> set in the face and size the surface's content is set in as typed, as it is drawn on the page.</summary>
    private FormattedText Numeral(MarkdownSurface surface, string digits, Brush ink)
    {
        var (face, size, _) = surface.TypedIn;

        return new FormattedText(digits, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, ink,
                                 VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}
