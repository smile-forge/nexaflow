namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// A run of colours a number is read along: what a heat map is coloured by, and what a legend showing one
/// is a bar rather than a row of squares.
///
/// <para>
/// <see cref="DiagramInk.Series"/> answers the other colour question — which of a few colours a group
/// takes — and the two are genuinely different. A series colour says <em>which</em>; a ramp says
/// <em>how much</em>, and has to be read as an ordered quantity at a glance. That is why the ramps here
/// are the ones designed for it rather than anything pretty: viridis and its family stay ordered when
/// they are printed grey and when they are read by the eight percent of men who do not see red and green
/// apart.
/// </para>
/// <para>
/// A diverging ramp is the other half of it — two runs from a middle, for a number with a meaningful
/// nought. A correlation is exactly that, which is why one belongs here at all.
/// </para>
/// </summary>
internal enum DiagramRamp
{
    Viridis,
    Magma,
    Plasma,
    Inferno,

    Blues,
    Reds,
    Greens,

    /// <summary>Diverging: blue at the low end, through white, to red at the high one.</summary>
    BlueRed,
}
