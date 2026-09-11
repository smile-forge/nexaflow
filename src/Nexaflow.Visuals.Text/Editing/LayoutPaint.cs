using System.Collections.Generic;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// How a piece is painted, beyond the marks it holds: turned, and snapped to a pixel grid.
///
/// <para>
/// Both are scopes rather than drawings — they apply to everything inside the piece as well as to its
/// own marks — which is why they are here and not a kind of <see cref="LayoutMark"/>. A mark says what
/// was drawn; this says what frame it was drawn in.
/// </para>
/// <para>
/// Neither is content-specific, whatever their first user is. A turned label, a turned diagram node and
/// a typeset accent are one thing: a piece drawn at an angle to whatever holds it. And snapping edges to
/// device pixels is what any content drawing hairlines wants — a stem or a fraction bar half a pixel wide
/// is the difference between a crisp line and a grey smudge.
/// </para>
/// <para>
/// Nothing carries one. It is a side table, null for every piece of every tune and every barcode, so the
/// painter's ordinary walk pays nothing for its existence.
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
public sealed record LayoutPaint(IReadOnlyList<Transform>? Turn = null, GuidelineSet? Snap = null)
{
    /// <summary>Whether it says anything at all — a piece with neither need not be scoped.</summary>
    public bool Matters => Turn is { Count: > 0 } || Snap is not null;
}
