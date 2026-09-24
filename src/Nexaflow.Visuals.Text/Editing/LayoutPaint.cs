using System.Collections.Generic;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// How a piece is painted, beyond the marks it holds: turned, snapped to a pixel grid, and kept as a picture once painted.
///
/// <para>
/// Each is a scope rather than a drawing — it applies to everything inside the piece as well as to its own marks — which
/// is why they are here and not a kind of <see cref="LayoutMark"/>. A mark says what was drawn; this says what frame it
/// was drawn in, and whether what was drawn there is kept.
/// </para>
/// <para>
/// None of them is content-specific, whatever their first user is. A turned label, a turned diagram node and a typeset
/// accent are one thing: a piece drawn at an angle to whatever holds it. Snapping edges to device pixels is what any
/// content drawing hairlines wants — a stem or a fraction bar half a pixel wide is the difference between a crisp line
/// and a grey smudge. And a picture kept is what any piece holding a great deal that does not change wants.
/// </para>
/// <para>
/// Hardly anything carries one. It is a side table, null for every piece of every tune and every barcode and for all
/// but the blocks of a document, so the painter's ordinary walk pays nothing for its existence.
/// </para>
/// </summary>
/// <param name="Turn">
/// How the piece is turned inside whatever holds it, applied in order about the piece's own anchor.
/// Where the content needs it about somewhere else, that is what <see cref="RotateTransform.CenterX"/>
/// is for.
/// </param>
/// <param name="Snap">
/// The pixel grid the piece's edges land on, in the piece's own frame.
/// </param>
/// <param name="Kept">Where the piece's picture is kept once it has been painted — see <see cref="LayoutKept"/>.</param>
public sealed record LayoutPaint(IReadOnlyList<Transform>? Turn = null, GuidelineSet? Snap = null, LayoutKept? Kept = null)
{
    /// <summary>Whether it says anything at all — a piece with none of it need not be scoped.</summary>
    public bool Matters => Turn is { Count: > 0 } || Snap is not null || Kept is not null;
}
