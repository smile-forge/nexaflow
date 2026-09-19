namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Where pressing a piece leads. Held beside the pieces rather than in them, as a run of words is
/// (<see cref="LayoutWords"/>): almost nothing drawn is a link, and a piece that is not one costs a null.
///
/// What a link does is the host's — a press on one means the link rather than a place to put the caret, and the
/// pointer is a hand over it.
/// </summary>
/// <param name="Href">Where it leads, handed to whatever the host navigates with.</param>
/// <param name="Tip">What it says while pointed at, or null to say where it leads.</param>
public sealed record LayoutLink(string Href, string? Tip = null);
