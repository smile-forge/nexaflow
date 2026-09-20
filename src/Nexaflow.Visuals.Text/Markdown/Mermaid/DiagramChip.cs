using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// The little square on a node saying there is more behind it — <c>+3</c> while it is folded away, <c>−</c> while it
/// is open — which answers to a press by folding it the other way.
///
/// <para>
/// A piece of its own, sitting over the corner of the node rather than inside it, which is what makes the body and the
/// chip two separate things to press: a host whose nodes open a tab of their own needs the node itself to go on
/// meaning that, and the folding to be something else.
/// </para>
/// </summary>
internal static class DiagramChip
{
    /// <summary>How far across a chip is, and how tall.</summary>
    public const double Size = 15;

    /// <summary>How big the sign in it is.</summary>
    public const double TextSize = 10;

    /// <summary>How round its corners are.</summary>
    private const double Corner = 3;

    /// <summary>Where a chip sits on a node filling <paramref name="bounds"/>: over its top right-hand corner.</summary>
    public static Rect On(Rect bounds) => new(bounds.Right - (Size / 2), bounds.Top - (Size / 2), Size, Size);

    /// <summary>What a chip says: how much is behind it while it is folded, and a bare sign while it is open.</summary>
    public static string Says(DiagramFold fold) => fold.Open ? "−" : fold.Hidden > 0 ? $"+{fold.Hidden}" : "+";

    /// <summary>
    /// Draws the chip, a press on which means the node <paramref name="key"/> names folds the other way.
    /// </summary>
    /// <param name="part">What the chip stands for — the node's own part, the chip itself being written nowhere.</param>
    /// <param name="id">The node, as the diagram names it.</param>
    /// <param name="label">What the node says, for a host that wants to name it in a message.</param>
    public static void Draw(LayoutBuilder build, Rect where, ISourcePart? part, DiagramFold fold, string id,
                            string? label, DiagramWords said, Brush fill, DiagramStroke stroke)
    {
        build.Open(MermaidPiece.Chip, part, stops: Stops.None);
        build.Acts(new LayoutActions
        {
            Click = new LayoutIntent(fold.Open ? LayoutVerbs.Collapse : LayoutVerbs.Expand, id, label),
        });

        var box = new RectangleGeometry(where, Corner, Corner);
        box.Freeze();

        // The drawing is a leaf of its own, as a shape's is: only what draws is pressed.
        build.Open(MermaidPiece.Shape, part, stops: Stops.None);
        build.Draw(new GeometryMark(box, fill, stroke.Ink, stroke.Thickness));
        build.Occupies(box);
        build.Close();

        said.Set(build,
                 new Point(where.X + ((where.Width - said.Width) / 2), where.Y + ((where.Height - said.Height) / 2)),
                 MermaidPiece.Words);

        build.Close();
    }
}
