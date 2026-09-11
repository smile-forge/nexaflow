using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.LilyPond;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

namespace Nexaflow.Visuals.Text.Markdown.Music;

/// <summary>
/// A piece of music on the page — its title and credits, the engraved music, and whatever is printed under it —
/// in whichever notation it was written.
///
/// <para>
/// <strong>All of it is one drawing, and that is what makes the words selectable.</strong> The prose used to be
/// real <see cref="TextBlock"/>s stacked around the score, on the reasoning that a title painted into a picture
/// is words nobody can reach. The reasoning was right and the remedy was not: an element embedded in a flow
/// document is selected whole or not at all. Engraved into the layout tree instead, a title names the field it
/// was written in, like every other piece, and one drag runs from the heading through the music to the last
/// verse.
/// </para>
/// <para>
/// What is left here is the page rather than the music: how much of the width the music takes, and the margins
/// either side of it. Which notation it is decides only which builder reads it.
/// </para>
/// </summary>
public sealed class MusicScore : StackPanel
{
    /// <param name="pageWidth">
    /// How much of the width it is given this block occupies — see <see cref="PageWidth"/>. A caller that is
    /// already handing it a page rather than a window passes 1.
    /// </param>
    public MusicScore(MusicDialect dialect, string source, MarkdownPalette palette, int sourceStart,
                      double zoom = 1.0, double pageWidth = 0.8)
    {
        PageWidth = pageWidth;
        Score = Engraved(dialect, source, palette, sourceStart, zoom);

        HorizontalAlignment = HorizontalAlignment.Stretch;
        Children.Add(Score);
    }

    /// <summary>
    /// The engraved music, as a plain <see cref="Editing.ContentElement"/> driven by its notation's builder.
    ///
    /// <para>
    /// What is genuinely the music's is all in the builders — how much air goes between things, how music that
    /// will not read is shown, how wide a system may run. Everything else a score needs it shares with
    /// everything else on the page.
    /// </para>
    /// </summary>
    public static Editing.ContentElement Engraved(MusicDialect dialect, string source, MarkdownPalette palette,
                                                  int sourceStart = 0, double zoom = 1.0, ScoreSpacing? spacing = null)
    {
        var ink = palette.Text;

        return new Editing.ContentElement(source, palette, (state, room, pixelsPerDip) =>
        {
            (int Start, int Length)? typed = state.Raw is { } raw ? (raw.Start, raw.End - raw.Start) : null;

            return dialect == MusicDialect.LilyPond
                ? LilyPondBuilder.Build(state.Source, room, ink, pixelsPerDip, typed, spacing)
                : AbcBuilder.Build(state.Source, room, ink, pixelsPerDip, typed, spacing);
        })
        {
            SourceStart = sourceStart,
            Zoom = zoom,

            // A quarter of the six-pixel margin a score's own wash once used. Six on every side overlapped the
            // line above and the line below.
            WashPad = 1.5,
        };
    }

    /// <summary>The engraved music — what the caret enters and what an edit changes.</summary>
    public Editing.ContentElement Score { get; }

    /// <summary>
    /// How much of the width this block is given it actually occupies, centred, with the rest left as margin.
    ///
    /// <para>
    /// A score set edge to edge across a wide window is a score nobody can read: the eye has to travel the whole
    /// width to follow one system, and the systems stop looking like lines of music. Printed music has margins
    /// for the same reason prose does.
    /// </para>
    /// <para>
    /// It lives here rather than in a builder because it is a page decision, not an engraving one. A builder's
    /// job is to fill the width it is handed as well as it can; how wide that is belongs to whatever is laying
    /// the block out — which is this.
    /// </para>
    /// </summary>
    public double PageWidth { get; }

    /// <summary>The margin either side, for a given width.</summary>
    private double Inset(double width) =>
        double.IsInfinity(width) || double.IsNaN(width) || width <= 0
            ? 0
            : width * (1 - Math.Clamp(PageWidth, 0.1, 1.0)) / 2;

    /// <summary>
    /// The width to lay this block out in — what it was given, or the room its host has where it was given none.
    ///
    /// <para>
    /// A block inside a document is sometimes measured with no width at all: a flow document works out its own
    /// column first and asks its embedded objects how big they would like to be. Answering with a fixed number
    /// engraves the music to that number and leaves the rest of the window empty. So where there is no width to
    /// be had, the nearest ancestor that has one is asked; the pass after layout finds the real width and
    /// re-engraves to it.
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
        var inside = base.MeasureOverride(new Size(Math.Max(0, constraint.Width - (2 * inset)), constraint.Height));

        // The whole width is taken, because the block IS the page — the margins are part of it. Taking only the
        // music would leave the two unable to line up with the prose around them.
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
