using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// Building a layout tree from absolute rectangles, the way a typesetter reports one.
///
/// <para>
/// The trees these build were hand-made object graphs, and that is the thing worth changing about them:
/// a tree that cannot be built the way a real one is has been proving the wrong thing. What they state
/// is unchanged — the same rectangles, the same parts, the same nesting — but it now goes through the
/// builder every real tree goes through, so an invariant the builder keeps is one these cannot quietly
/// break.
/// </para>
/// <para>
/// Absolute in, relative out. A test reads far better as "this box is here on the page" than as a chain
/// of offsets, and <see cref="LayoutBuilder.Anchor"/> is the subtraction between the two — which is
/// exactly what the real builders do with the coordinates a typesetter or an engraver hands them.
/// </para>
/// </summary>
internal static class Built
{
    /// <summary>
    /// Opens a piece covering an absolute rectangle, and gives back where it went.
    ///
    /// <para>
    /// It states its own extent and never grows to fit what it holds, because that is what these trees
    /// are pictures of: a typeset box is the height and depth it reserves on its line, so a subscript
    /// hangs below the very piece that holds it.
    /// </para>
    /// </summary>
    public static int At(this LayoutBuilder build, Rect where, ISourcePart? part, string kind, bool isInk)
    {
        var anchor = build.Anchor;
        var at = build.Open(kind, part, new Point(where.X - anchor.X, where.Y - anchor.Y),
                            isInk, gathers: false);

        build.Covers(new Rect(0, 0, where.Width, where.Height));
        return at;
    }

    /// <summary>…and the same for a piece holding nothing.</summary>
    public static int Leaf(this LayoutBuilder build, Rect where, ISourcePart? part, string kind,
                           bool isInk = true)
    {
        var at = build.At(where, part, kind, isInk);
        build.Close();
        return at;
    }
}
