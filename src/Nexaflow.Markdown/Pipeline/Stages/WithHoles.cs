using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Pipeline.Stages;

/// <summary>
/// The same tree with a hole put in every place a language says something belongs and nothing was
/// written.
///
/// <para>
/// This half only says one belongs there. What it looks like is the builder's, which turns it into
/// something drawable the same way it turns every other piece into something drawable — the reading says
/// what is true of the content, and the setting says what a reader sees.
/// </para>
/// <para>
/// A hole stands for nothing that was written, so it takes up none of the source and the tree still
/// prints as what it came from. It is the same kind of piece as a derived part and is there for the same
/// reason: to say something the characters do not. And nothing is put <em>inside</em> a derived part: none
/// of it was written, so none of it was left unwritten either — an empty group in a macro's definition is
/// how that macro draws, not a gap somebody has still to fill.
/// </para>
/// <para>
/// Asked for by a surface being written on, where the hole is how a reader sees there is something still
/// to write and how they aim at it. A page that is only being read wants none — a box in the middle of a
/// formula nobody is editing would simply be wrong, and reading is the commoner case.
/// </para>
/// </summary>
/// <param name="holds">
/// Whether a piece is somewhere content belongs — a braced argument, a table cell, an empty bar — given
/// what holds it (null for the whole content). Asked of the language, because which pieces those are is a
/// fact about it and nothing shared could enumerate them; and handed the holder, because whether an
/// argument may be left empty is often a fact about the construct it belongs to rather than about the
/// argument.
/// </param>
/// <param name="says">What the hole has to say for itself, shown when a reader asks.</param>
public sealed class WithHoles(Func<ContentNode?, ContentNode, bool> holds, string says = "Something still has to go here.")
    : IAstStage
{
    public string Name => "with-holes";

    public ContentNode Run(ContentNode tree) => this.Hollow(tree, null);

    private ContentNode Hollow(ContentNode node, ContentNode? holder)
    {
        if (node.IsLeaf || node.IsDerived) return node;

        var rebuilt = new List<ContentNode>(node.Children.Count + 1);
        var moved = false;

        foreach (var child in node.Children)
        {
            var seen = this.Hollow(child, node);
            moved |= !ReferenceEquals(seen, child);
            rebuilt.Add(seen);
        }

        // Nothing written between the brackets, or between one separator and the next. Machinery does not
        // count as something being there: `{}` is a hole and so is the cell after the last separator.
        var empty = !rebuilt.Any(child => child.Width > 0
                                          && child.Role is not (Roles.Open or Roles.Close or Roles.Separator));

        if (empty && holds(holder, node))
        {
            rebuilt.Insert(rebuilt.FindIndex(child => child.Role == Roles.Open) + 1,
                           ContentNode.Leaf(Kinds.Hole, string.Empty, Roles.Element, says));
            moved = true;
        }

        return moved ? node.With(rebuilt) : node;
    }
}
