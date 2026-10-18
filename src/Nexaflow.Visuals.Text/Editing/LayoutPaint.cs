using System.Collections.Generic;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// How a piece is painted, beyond the marks it holds: turned, scaled, snapped to a pixel grid, clipped, faded, masked, and
/// kept as a picture once painted.///
/// <para>
/// Each is a scope rather than a drawing — it applies to everything inside the piece as well as to its own marks — which
/// is why they are here and not a kind of <see cref="LayoutMark"/>. A mark says what was drawn; this says what frame it
/// was drawn in, how much of it shows, and whether what was drawn there is kept.
/// </para>
/// <para>
/// None of them is content-specific, whatever their first user is. A turned label, a turned diagram node and a typeset
/// accent are one thing: a piece drawn at an angle to whatever holds it. A drawing set smaller is any content too wide for
/// the room it was given once it has reshaped itself to that room as far as it can — and only the content can say that it
/// has, so a piece is scaled because its builder asked, never because the layout decided. Snapping edges to device pixels is what any
/// content drawing hairlines wants — a stem or a fraction bar half a pixel wide is the difference between a crisp line
/// and a grey smudge. A clip, a fade and a mask are what any content drawn in layers wants — a page whose paths are cut to
/// a shape, a group laid over the rest at half strength. And a picture kept is what any piece holding a great deal that
/// does not change wants.
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
/// <param name="Scale">
/// How much larger than it was laid out the piece and everything in it is drawn, about the piece's own anchor — 1, the size it
/// was laid out at, for nearly everything. Applied inside any turn.
/// </param>
/// <param name="Clip">
/// The only part of the piece that shows, in the piece's own frame — measured from its anchor, inside however it is
/// turned and scaled. What it cuts away is neither painted nor pressed. Frozen, as everything a tree holds is, and a plain
/// path rather than a combination of shapes: a press asks whether it is inside, and a combination is worked out again on
/// every ask.
/// </param>
/// <param name="Opacity">How strongly the piece shows over whatever is under it, from 0 (not at all) to 1 (wholly).</param>
/// <param name="Mask">
/// How strongly each part of the piece shows, as the alpha of a brush laid over it — a gradient fading it out, a picture
/// letting it through in places. In the piece's own frame, as <paramref name="Clip"/> is; frozen.
/// </param>
public sealed record LayoutPaint(IReadOnlyList<Transform>? Turn = null, GuidelineSet? Snap = null, LayoutKept? Kept = null,
                                 double Scale = 1, Geometry? Clip = null, double Opacity = 1, Brush? Mask = null)
{
    /// <summary>Whether it says anything at all — a piece with none of it need not be scoped.</summary>
    public bool Matters => Turn is { Count: > 0 } || Snap is not null || Kept is not null || Scale != 1 || Clip is not null
                           || Opacity < 1 || Mask is not null;

    /// <summary>Whether it turns or scales what the piece holds — whether there is anything <see cref="Applied"/> to it.</summary>
    public bool Applies => Turn is { Count: > 0 } || Scale != 1;

    /// <summary>
    /// How what the piece holds is turned and scaled about its anchor, as one matrix — scaled first and then turned, the order
    /// the painter pushes them in — or null where it is neither, which is nearly always. What a press comes back through into
    /// the piece's own frame, and what a caret or a wash goes out through.
    /// </summary>
    public Matrix? Applied
    {
        get
        {
            if (!Applies) return null;

            var applied = Matrix.Identity;
            if (Scale != 1) applied.Scale(Scale, Scale);

            // The first turn is pushed first, so it is the outermost: what is drawn passes through the last of them first.
            if (Turn is { } turns)
                for (var at = turns.Count - 1; at >= 0; at--) applied *= turns[at].Value;

            return applied;
        }
    }}
