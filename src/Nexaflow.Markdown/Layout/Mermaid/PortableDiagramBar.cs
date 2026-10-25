using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// The key a run of colours gets: a bar of the colours themselves with numbers beside it.
///
/// <para>
/// Not a <see cref="PortableDiagramLegend"/> with very many rows, which is what a continuous scale would be if it
/// were squeezed into one. A legend answers "which of these is it", a bar answers "how far along is it",
/// and a reader looking at a heat map is asking the second. It shares the legend's measurements so the
/// two sit alike beside a chart.
/// </para>
/// <para>
/// A bar stands for nothing anybody wrote — nobody typed a colour ramp — so nothing in it takes a caret.
/// </para>
/// </summary>
internal sealed class PortableDiagramBar
{
    /// <param name="stops">The colours, from the low end to the high one.</param>
    /// <param name="marks">Where each number sits along it, from nought at the foot to one at the head.</param>
    public PortableDiagramBar(IReadOnlyList<LayoutColor> stops, IReadOnlyList<(double At, PortableDiagramWords Words)> marks,
                              LayoutInk outline)
    {
        this._stops = stops;
        this._marks = marks;
        this._outline = outline;
    }

    private readonly IReadOnlyList<LayoutColor> _stops;
    private readonly IReadOnlyList<(double At, PortableDiagramWords Words)> _marks;
    private readonly LayoutInk _outline;

    /// <summary>How wide the bar itself is drawn.</summary>
    public double Thickness { get; init; } = PortableDiagramLegend.SwatchSize;

    /// <summary>How tall it is drawn.</summary>
    public double Tall { get; init; } = 120;

    /// <summary>The clear air between the bar and its numbers.</summary>
    public const double Gap = 6;

    /// <summary>What the whole key takes, bar and numbers together.</summary>
    public LayoutSize Size => this._stops.Count == 0
        ? new LayoutSize(0, 0)
        : new LayoutSize(this.Thickness + Gap + this._marks.Select(mark => mark.Words.Width).DefaultIfEmpty(0).Max(),
                         Math.Max(this.Tall, this._marks.Select(mark => mark.Words.Height).DefaultIfEmpty(0).Max()));

    /// <summary>Draws the bar with its top left corner at <paramref name="at"/>.</summary>
    public void Draw(PortableBuilder build, LayoutPoint at)
    {
        if (this._stops.Count == 0) return;

        build.Open(MermaidPiece.Legend, part: null, at: at, stops: PortableStops.None);

        var bar = new LayoutRect(0, 0, this.Thickness, this.Tall);

        // Down the page is the high end at the top, so the ramp runs from the foot up.
        var stops = new (LayoutColor, double)[this._stops.Count];
        for (var at2 = 0; at2 < this._stops.Count; at2++)
            stops[at2] = (this._stops[at2], this._stops.Count == 1 ? 0 : (double)at2 / (this._stops.Count - 1));

        var ink = build.Medium.Gradient(stops, new LayoutPoint(0, 1), new LayoutPoint(0, 0));

        build.Draw(new PortableRuleMark(bar, ink));
        build.Draw(PortableDiagramMarks.Outlined(build.Medium, Outline(build.Medium, bar), null, this._outline, 1));
        build.Covers(bar);

        foreach (var (along, words) in this._marks)
            words.Set(build, new LayoutPoint(this.Thickness + Gap, this.Tall - (along * this.Tall) - (words.Height / 2)),
                      MermaidPiece.Key);

        build.Close();
    }

    private static LayoutShape Outline(ILayoutMedium medium, LayoutRect bar)
    {
        var outline = medium.Rectangle(bar);
        return outline;
    }
}