using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// What a diagram is drawn in: a colour as its source or front matter wrote it, the theme's colour for one of a series, a
/// colour faded, and ink that reads over a fill.
///
/// <para>
/// <strong>A builder asks for its colours here rather than making inks.</strong> A colour nobody wrote is always the
/// theme's, so every diagram follows the theme without knowing what it is. What <c>#ff6b6b</c>, <c>red</c> or
/// <c>rgb(1, 2, 3)</c> comes to is decided once, for all of them.
/// </para>
/// </summary>
internal sealed class PortableDiagramInk(PortableStyle style, ILayoutMedium medium)
{
    /// <summary>How much of the accent a box, a note, a box holding others and its band are washed with, and how much of it a holding box's outline is drawn in.</summary>
    private const double NodeWash = 0.25;
    private const double NoteWash = 0.2;
    private const double GroupWash = 0.13;
    private const double BandWash = 0.24;
    private const double GroupRim = 0.4;

    /// <summary>How much of the muted ink a quiet shape's outline is drawn in, and how much of its outline's ink a box's rules are.</summary>
    private const double QuietRim = 0.7;
    private const double RuleWash = 0.5;

    /// <summary>A colour as a style or the front matter wrote it, or null where it wrote none this understands.</summary>
    public LayoutInk? Written(string? colour) => PortableDiagramColour.ParseCss(colour) is { } parsed ? medium.Ink(parsed) : null;

    /// <summary>The theme's colour for the <paramref name="order"/>th of a series — a slice, a set, a line — round again past the last.</summary>
    public LayoutInk Series(int order) => style.Series[((order % style.Series.Count) + style.Series.Count) % style.Series.Count];

    /// <summary>The colour written for one of a series, and the theme's for its place where none is.</summary>
    public LayoutInk Series(int order, string? written) => Written(written) ?? Series(order);

    /// <summary>Ink that reads over <paramref name="fill"/>: the theme's dark ink over a light fill, and its light ink over a dark one.</summary>
    public LayoutInk Over(LayoutInk fill) =>
        PortableDiagramColour.OnColor(PortableDiagramColour.ColorOf(fill, LayoutColors.Gray), style.QrDark, style.QrLight);

    /// <summary>
    /// The colour a diagram's own surface comes to, opaque. A theme's surfaces are translucent — a light theme's are
    /// alpha-black — so anything painted to hide what is under it has to be painted in what they come to over the page, not
    /// in the surface ink itself.
    /// </summary>
    public static LayoutColor Under(PortableStyle style) =>
        PortableDiagramColour.Composite(
            PortableDiagramColour.ColorOf(style.CodeBg, LayoutColors.Black),
            PortableDiagramColour.Luminance(PortableDiagramColour.ColorOf(style.Text, LayoutColors.White)) > 140 ? LayoutColors.Black : LayoutColors.White);

    /// <summary>That colour as an ink — what a diagram paints on to cover what it has already drawn.</summary>
    public LayoutInk Surface => medium.Ink(Under(style));

    /// <summary>
    /// What a box a diagram joins by lines is filled with where nothing is written for it — a flowchart's node, a state, a class,
    /// an entity, a block: the accent washed in, so every one of a kind is drawn the same and all of them read as the things the
    /// lines join.
    /// </summary>
    public LayoutInk Node => Faded(style.Accent, NodeWash);

    /// <summary>What such a box is outlined in where nothing is written for it: the accent itself.</summary>
    public LayoutInk NodeEdge => style.Accent;

    /// <summary>What a line joining two of them is drawn in where nothing is written for it — the same accent as what it joins.</summary>
    public LayoutInk Link => style.Accent;

    /// <summary>What a note is filled with: the theme's warning washed in, so a note never reads as one of the things it is about.</summary>
    public LayoutInk Note => Faded(style.Warning, NoteWash);

    /// <summary>What a note is outlined in, and what holds it to what it is about.</summary>
    public LayoutInk NoteEdge => style.Warning;

    /// <summary>
    /// What a box holding others is filled with where nothing is written for it — a subgraph, a namespace, a composite: every one
    /// the same faint wash of the accent, and the boxes inside it stronger than it.
    /// </summary>
    public LayoutInk Group => Faded(style.Accent, GroupWash);

    /// <summary>What such a box is outlined in where nothing is written for it.</summary>
    public LayoutInk GroupEdge => Faded(style.Accent, GroupRim);

    /// <summary>
    /// The band a box holding others sets its name in, across the top of it: <paramref name="written"/> — the colour its outline is
    /// written in — or the accent, washed in more strongly than the box, so the name reads apart from what the box holds.
    /// </summary>
    public LayoutInk Band(LayoutInk? written) => Faded(written ?? style.Accent, BandWash);

    /// <summary>
    /// What a shape that is not one of the things a diagram joins is filled with — a block arrow pointing the way: the muted ink
    /// washed in, a shade apart from the boxes it points between.
    /// </summary>
    public LayoutInk Quiet => Faded(style.TextMuted, NodeWash);

    /// <summary>What such a shape is outlined in.</summary>
    public LayoutInk QuietEdge => Faded(style.TextMuted, QuietRim);

    /// <summary>What the rules dividing a box into its bands are drawn in: its outline's ink, fainter than the outline.</summary>
    public LayoutInk Ruled(LayoutInk edge) => Faded(edge, RuleWash);

    /// <summary>As <see cref="Ruled(LayoutInk)"/>, made in <paramref name="medium"/>.</summary>
    public static LayoutInk Ruled(ILayoutMedium medium, LayoutInk edge) => medium.Faded(edge, RuleWash);

    /// <summary>An ink at <paramref name="opacity"/> — itself, where that is whole.</summary>
    public LayoutInk Faded(LayoutInk ink, double opacity) => medium.Faded(ink, opacity);

    /// <summary>An ink at <paramref name="opacity"/>, made in <paramref name="medium"/> — for what draws without a diagram's own ink to hand.</summary>
    public static LayoutInk Faded(ILayoutMedium medium, LayoutInk ink, double opacity) => medium.Faded(ink, opacity);

    /// <summary>
    /// The colour a share from nought to one takes along a ramp. The other colour question: <c>Series</c>
    /// says which of a few a group takes, and this says how far along a run a number is.
    /// </summary>
    public LayoutInk Scale(DiagramRamp ramp, double share) => medium.Ink(PortableDiagramColours.At(ramp, share));

    /// <summary>
    /// The same along a run of colours read off a ramp or written out — the colours themselves, so a
    /// block that named them is not read again for every mark.
    /// </summary>
    public LayoutInk Scale(IReadOnlyList<LayoutColor> stops, double share) => medium.Ink(PortableDiagramColours.At(stops, share));

    /// <summary>
    /// The dashes a style's <c>stroke-dasharray</c> writes, as the lengths drawn and left — or null where it writes none this can
    /// read. Any diagram whose styling can ask for a dashed line reads it the same way.
    /// </summary>
    public static double[]? Dashes(string? said)
    {
        if (said is not { Length: > 0 }) return null;

        var lengths = said.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(MermaidNumber.Read)
            .OfType<double>()
            .Where(length => length > 0)
            .ToArray();

        return lengths.Length == 0 ? null : lengths;
    }
}
