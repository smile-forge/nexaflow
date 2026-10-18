using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// How a stroked outline is drawn beyond its brush, thickness and dashes: how its ends are capped, how it turns a corner,
/// and where along its dashes it starts.
///
/// <para>
/// A small record of its own rather than more properties on a mark, because it is a pen's state and content that strokes
/// many outlines keeps one: a drawing that says "round ends from here on" hands every outline after it the same one.
/// </para>
/// <para>
/// The defaults are a pen's own — flat ends, square dash ends, mitred corners cut off ten half-thicknesses out, dashes
/// from their start — so a mark with none of this draws as one with the defaults.
/// </para>
/// </summary>
/// <param name="StartCap">How the outline's first end is capped.</param>
/// <param name="EndCap">How the outline's last end is capped.</param>
/// <param name="DashCap">How each end of every dash is capped.</param>
/// <param name="Join">How the outline turns a corner.</param>
/// <param name="MiterLimit">
/// How far a mitred corner may reach past the outline, in multiples of half its thickness, before it is cut off square.
/// </param>
/// <param name="DashOffset">How far into its dashes the outline starts, in multiples of its thickness, as the dashes are.</param>
public sealed record LayoutPen(
    PenLineCap StartCap = PenLineCap.Flat,
    PenLineCap EndCap = PenLineCap.Flat,
    PenLineCap DashCap = PenLineCap.Square,
    PenLineJoin Join = PenLineJoin.Miter,
    double MiterLimit = 10,
    double DashOffset = 0);
