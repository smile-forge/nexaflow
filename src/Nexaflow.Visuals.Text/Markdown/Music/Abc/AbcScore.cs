using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>
/// A whole tune on the page: its title and credits, the engraved music, and whatever is printed under it.
///
/// <para>
/// <strong>The prose is text and stays text.</strong> A title painted into the drawing is words nobody can
/// select, and the words around a tune — its name, who wrote it, where it was collected, the verses that
/// do not fit under the notes — are exactly the ones a reader wants to copy. So they are real
/// <see cref="TextBlock"/>s stacked around the score rather than marks inside it.
/// </para>
/// <para>
/// The syllables under the notes are the exception, and deliberately: they are glued to head positions, so
/// they are part of the drawing.
/// </para>
/// </summary>
public sealed class AbcScore : StackPanel
{
    /// <param name="pageWidth">
    /// How much of the width it is given this block occupies — see <see cref="PageWidth"/>. A caller that
    /// is already handing it a page rather than a window passes 1.
    /// </param>
    public AbcScore(string abc, MarkdownPalette palette, int sourceStart, double zoom = 1.0,
                    double pageWidth = 0.8)
    {
        PageWidth = pageWidth;
        Score = new AbcElement(abc, palette) { SourceStart = sourceStart, Zoom = zoom };

        var header = AbcHeader.Of(ContentReading.Of(AbcParser.Parse(abc)));
        HorizontalAlignment = HorizontalAlignment.Stretch;

        // The prose zooms with the music. A title set at full size over a score drawn at two thirds is not
        // a page anybody would print, and the zoom is a request about how big the whole thing is.
        foreach (var line in Above(header, palette, zoom)) Children.Add(line);
        Children.Add(Score);
        foreach (var line in Below(header, palette, zoom)) Children.Add(line);
    }

    /// <summary>The engraved music — what the caret enters and what an edit changes.</summary>
    public AbcElement Score { get; }

    /// <summary>
    /// How much of the width this block is given it actually occupies, centred, with the rest left as
    /// margin.
    ///
    /// <para>
    /// A score set edge to edge across a wide window is a score nobody can read: the eye has to travel the
    /// whole width to follow one system, and the systems stop looking like lines of music. Printed music
    /// has margins for the same reason prose does.
    /// </para>
    /// <para>
    /// It lives here rather than in the engraver because it is a page decision, not an engraving one. The
    /// engraver's job is to fill the width it is handed as well as it can; how wide that is belongs to
    /// whatever is laying the block out — which is this.
    /// </para>
    /// </summary>
    public double PageWidth { get; }

    /// <summary>The margin either side, for a given width.</summary>
    private double Inset(double width) =>
        double.IsInfinity(width) || double.IsNaN(width) || width <= 0
            ? 0
            : width * (1 - Math.Clamp(PageWidth, 0.1, 1.0)) / 2;

    /// <summary>
    /// The width to lay this block out in — what it was given, or the room its host has where it was
    /// given none.
    ///
    /// <para>
    /// A block inside a document is sometimes measured with no width at all: a flow document works out its
    /// own column first and asks its embedded objects how big they would like to be. Answering with a
    /// fixed number, which is what happened, engraves the tune to that number and leaves the rest of the
    /// window empty — the music looks cut off, and resizing the window does nothing.
    /// </para>
    /// <para>
    /// So where there is no width to be had, the nearest ancestor that has one is asked. It is
    /// self-correcting: the first pass may find nothing and fall back, and the pass after layout finds the
    /// real width and re-engraves to it.
    /// </para>
    /// </summary>
    private double Room(double given)
    {
        if (!double.IsInfinity(given) && !double.IsNaN(given) && given > 1) return given;

        for (DependencyObject? at = this; at is not null; at = VisualTreeHelper.GetParent(at))
            if (at is FrameworkElement host && host.ActualWidth > 1)
                return host.ActualWidth;

        return 900;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        constraint = new Size(Room(constraint.Width), constraint.Height);

        var inset = Inset(constraint.Width);
        var inside = base.MeasureOverride(new Size(Math.Max(0, constraint.Width - (2 * inset)),
                                                   constraint.Height));

        // The whole width is taken, because the block IS the page — the margins are part of it. Taking
        // only the music would leave the two unable to line up with the prose around them.
        return new Size(double.IsInfinity(constraint.Width) ? inside.Width : constraint.Width, inside.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var inset = Inset(finalSize.Width);
        var inside = Math.Max(0, finalSize.Width - (2 * inset));
        var y = 0.0;

        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(inset, y, inside, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }

        return finalSize;
    }

    /// <summary>Title, subtitles, and the credit row that sits over the first system.</summary>
    private static IEnumerable<FrameworkElement> Above(AbcHeader header, MarkdownPalette palette, double zoom)
    {
        if (header.Title is { Length: > 0 } title)
            yield return Line(title, ScoreMetrics.TitleSize * zoom, palette, TextAlignment.Center,
                              FontWeights.SemiBold);

        foreach (var subtitle in header.Subtitles)
            yield return Line(subtitle, ScoreMetrics.SubtitleSize * zoom, palette, TextAlignment.Center);

        if (header.Rhythm is null && header.Credit is null) yield break;

        // The rhythm at the left and the composer at the right, on one line — which is where an engraver
        // puts them, and why they are a grid rather than two stacked blocks.
        var credits = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        credits.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        credits.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (header.Rhythm is { Length: > 0 } rhythm)
        {
            var left = Line(rhythm, ScoreMetrics.CreditSize * zoom, palette, TextAlignment.Left,
                            style: FontStyles.Italic);
            Grid.SetColumn(left, 0);
            credits.Children.Add(left);
        }

        if (header.Credit is { Length: > 0 } credit)
        {
            var right = Line(credit, ScoreMetrics.CreditSize * zoom, palette, TextAlignment.Right);
            Grid.SetColumn(right, 1);
            credits.Children.Add(right);
        }

        yield return credits;
    }

    /// <summary>
    /// Whatever is printed under the last system: the verses, and any note the writer left.
    ///
    /// <para>
    /// A blank line among them is the gap between two stanzas, so it becomes a gap rather than an empty
    /// <see cref="TextBlock"/> — which has no height and would run four verses into one block of text.
    /// </para>
    /// </summary>
    private static IEnumerable<FrameworkElement> Below(AbcHeader header, MarkdownPalette palette, double zoom)
    {
        if (header.Footer.Count == 0) yield break;

        // Set in from the music rather than flush with it. Verses are prose under a picture, and prose that
        // starts exactly where the staff starts reads as another system rather than as the words to it —
        // which is why engravers indent them, and why the reference does.
        var indent = new Thickness(ScoreMetrics.S * 2 * zoom, 1, 0, 1);

        var split = ColumnBreak(header.Footer);
        if (split is null)
        {
            foreach (var line in header.Footer) yield return Verse(line, palette, zoom, indent);
            yield break;
        }

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var side = 0; side < 2; side++)
        {
            var stack = new StackPanel();
            var from = side == 0 ? 0 : split.Value;
            var to = side == 0 ? split.Value : header.Footer.Count;

            for (var at = from; at < to; at++) stack.Children.Add(Verse(header.Footer[at], palette, zoom, indent));

            Grid.SetColumn(stack, side);
            columns.Children.Add(stack);
        }

        yield return columns;
    }

    /// <summary>
    /// Where to break the verses into two columns, or null to leave them in one.
    ///
    /// <para>
    /// Four stanzas of a hymn set one under another is a column of text as tall as the music it belongs to,
    /// and a reader has to scroll away from the tune to read the words to it. Engravers set them in two,
    /// and abcm2ps does it by default — which is why the corpus's own pages are so much shorter than ours
    /// were. The test is the same one it uses: the lines have to be short enough that two columns of them
    /// are still comfortable, and there have to be enough of them to be worth splitting.
    /// </para>
    /// <para>
    /// The break goes at a blank line where there is one near the middle, so a stanza is never cut in half.
    /// </para>
    /// </summary>
    private static int? ColumnBreak(IReadOnlyList<string> footer)
    {
        const int Enough = 6;
        const int Short = 44;

        if (footer.Count < Enough) return null;
        if (footer.Any(line => line.Length > Short)) return null;

        var middle = (footer.Count + 1) / 2;
        var best = middle;

        for (var away = 0; away <= footer.Count / 4; away++)
            foreach (var at in new[] { middle - away, middle + away })
                if (at > 0 && at < footer.Count && footer[at - 1].Trim().Length == 0)
                    return at;

        return best;
    }

    /// <summary>One line of the verses — or the gap between two stanzas, which is a line that is empty.</summary>
    private static FrameworkElement Verse(string line, MarkdownPalette palette, double zoom, Thickness indent) =>
        line.Trim().Length == 0
            ? new Border { Height = ScoreMetrics.FooterSize * zoom * 0.7 }
            : Line(line, ScoreMetrics.FooterSize * zoom, palette, TextAlignment.Left, margin: indent);

    private static TextBlock Line(string text, double size, MarkdownPalette palette, TextAlignment align,
                                  FontWeight? weight = null, FontStyle? style = null,
                                  Thickness? margin = null) =>
        new()
        {
            Text = text,
            FontFamily = BlockRenderer.BodyFont,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            FontStyle = style ?? FontStyles.Normal,
            Foreground = palette.Text,
            TextAlignment = align,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin ?? new Thickness(0, 1, 0, 1),
        };
}
