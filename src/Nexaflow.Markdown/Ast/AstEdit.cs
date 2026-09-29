namespace Nexaflow.Markdown.Ast;

/// <summary>
/// Changing content by changing its tree — the surgery every language's edits are built on.
///
/// <para>
/// <strong>What comes back is provisional.</strong> The stages between the parser and the builder do not
/// re-derive themselves when a tree is changed underneath them: a filled hole is still marked a hole, a
/// command whose name has just grown is still marked undrawable. So nothing here is ever built from —
/// print it, read the source back, and build from what that gives.
/// </para>
/// <para>
/// A <see cref="ContentPart.Derived"/> part is never a target: it stands for no source, so it has no
/// characters to rewrite. An edit landing in one is an edit to its nearest written ancestor, and reading
/// the result back derives the expansion again.
/// </para>
/// </summary>
public static class AstEdit
{
    /// <summary>The whole tree, with <paramref name="replacement"/> where <paramref name="at"/> stood.</summary>
    public static ContentNode Replace(ContentPart at, ContentNode replacement) => Swap(at, replacement);

    /// <summary>
    /// The whole tree, without <paramref name="part"/>. Emptying the whole content leaves an empty
    /// sequence rather than nothing at all, because content somebody has emptied is still content they
    /// are in the middle of writing.
    /// </summary>
    public static ContentNode Remove(ContentPart part)
    {
        if (part.Parent is not { } parent) return ContentNode.Branch(Kinds.Sequence, []);

        var children = parent.Node.Children.Where((_, index) => index != part.Order).ToArray();

        return Swap(parent, parent.Node.With(children));
    }

    /// <summary>The whole tree, with <paramref name="node"/> among <paramref name="into"/>'s parts at <paramref name="at"/>.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="into"/> stands for characters rather than for parts, and a piece cannot hold both:
    /// printing takes the parts of anything that has them and ignores its text, so a part put inside a
    /// leaf would quietly drop what the leaf said.
    /// </exception>
    public static ContentNode Insert(ContentPart into, int at, ContentNode node)
    {
        if (into.Node.IsLeaf && into.Node.Text.Length > 0)
            throw new ArgumentException(
                $"{into.Node.Kind} \"{into.Node.Text}\" stands for characters, so it holds no parts", nameof(into));

        var children = into.Node.Children.ToList();
        children.Insert(Math.Clamp(at, 0, children.Count), node);

        return Swap(into, into.Node.With(children));
    }

    /// <summary>
    /// The root of the tree <paramref name="at"/> belongs to, rebuilt to hold <paramref name="replacement"/>.
    /// Only the spine is rebuilt, so every subtree the edit did not touch comes back the object it was —
    /// which is what lets one row of a table move without the cells beside it being remade.
    /// </summary>
    private static ContentNode Swap(ContentPart at, ContentNode replacement)
    {
        var node = replacement;

        // Each part already knows which of its parent's parts it is, so the way up is a walk, not a search.
        for (var part = at; part.Parent is { } parent; part = parent)
        {
            var children = parent.Node.Children.ToArray();
            children[part.Order] = node;
            node = parent.Node.With(children);
        }

        return node;
    }
}
