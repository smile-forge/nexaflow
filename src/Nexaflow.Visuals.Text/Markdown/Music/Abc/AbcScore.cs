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
/// <strong>All of it is one drawing, and that is what makes the words selectable.</strong> The prose used
/// to be real <see cref="TextBlock"/>s stacked around the score, on the reasoning that a title painted
/// into a picture is words nobody can reach. The reasoning was right and the remedy was not: an element
/// embedded in a flow document is selected whole or not at all, so the title was exactly as unreachable
/// as it would have been painted. Engraved into the layout tree instead it names the field it was written
/// in, like every other piece, and one drag runs from the heading through the music to the last verse.
/// </para>
/// <para>
/// What is left here is the page rather than the tune: how much of the width the music takes, and the
/// margins either side of it.
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
        Score = Engraved(abc, palette, sourceStart, zoom);

        HorizontalAlignment = HorizontalAlignment.Stretch;
        Children.Add(Score);
    }

    /// <summary>
    /// The engraved music, as a plain <see cref="ContentElement"/> driven by the engraver.
    ///
    /// <para>
    /// There used to be an <c>AbcElement</c> here: three hundred and fifty lines that measured a layout,
    /// painted it, hit-tested it and held a selection — the same three hundred and fifty the formula and the
    /// barcode each had their own copy of, drifting apart as one or another was fixed. Its selection wash
    /// went over the leaves rather than the bands, which is the visible difference and was the wrong one:
    /// washing a run of source as one band is what stops a selection reading as a row of disconnected
    /// blocks.
    /// </para>
    /// <para>
    /// What is genuinely the music's is all in the engraver — how much air goes between things, how a tune
    /// that will not read is shown, how wide a system may run. Everything else a score needs it now shares
    /// with everything else on the page.
    /// </para>
    /// </summary>
    public static Editing.ContentElement Engraved(string abc, MarkdownPalette palette, int sourceStart = 0,
                                                  double zoom = 1.0, ScoreSpacing? spacing = null)
    {
        var ink = palette.Text;

        return new Editing.ContentElement(abc, palette,
            (state, room, pixelsPerDip) => AbcBuilder.Build(
                state.Source, room, ink, pixelsPerDip,
                shownAsWritten: state.Raw is { } raw ? (raw.Start, raw.End - raw.Start) : null,
                spacing: spacing))
        {
            SourceStart = sourceStart,
            Zoom = zoom,

            // What the score's own wash used before it was shared: a quarter of its six-pixel margin. Six on
            // every side — which is what was first carried over — overlapped the line above and the line below.
            WashPad = 1.5,
        };
    }

    /// <summary>The engraved music — what the caret enters and what an edit changes.</summary>
    public Editing.ContentElement Score { get; }

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
}
