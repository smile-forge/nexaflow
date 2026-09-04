using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Maths.Latex;

/// <summary>
/// Changing a formula by changing its tree.
///
/// <para>
/// Each of these takes a part of a reading and gives back a whole new root. The tree is immutable, so
/// what comes back shares every subtree the edit did not touch — only the spine from the root down to
/// the change is rebuilt, which is why moving one row of a matrix need not reformat the cells beside it.
/// </para>
/// <para>
/// <b>What comes back is provisional.</b> It prints, and printing it is the point: the source it prints
/// as is the document, and reading <em>that</em> is what produces a tree fit to build from. The stages
/// between the parser and the builder do not re-derive themselves when a tree is changed underneath
/// them — a filled hole is still marked a hole, a command whose name has just grown is still marked
/// undrawable, a gathered shape may no longer be the shape gathering would make of it, and a stretch
/// marked as being typed is pinned to a caret that has moved. So nothing is ever typeset from one of
/// these: print it, read the source back, and build from what that gives.
/// </para>
/// <para>
/// Which is also why nothing here consults the source or produces it. An edit expressed against the
/// tree knows what it touched; an edit expressed against the characters knows only that they changed,
/// and cannot afterwards say which unterminated brace is the new one.
/// </para>
/// </summary>
public static class TexEdit
{
    /// <summary>The whole tree, with <paramref name="replacement"/> where <paramref name="at"/> stood.</summary>
    public static TexNode Replace(TexPart at, TexNode replacement) => Swap(at, replacement);

    /// <summary>
    /// The whole tree, without <paramref name="part"/> — taken out of whatever held it.
    /// <para>
    /// Removing the whole formula leaves an empty sequence rather than nothing at all, because a formula
    /// somebody has emptied is still a formula they are in the middle of writing.
    /// </para>
    /// </summary>
    public static TexNode Remove(TexPart part)
    {
        if (part.Parent is not { } parent) return TexNode.Branch(TexKind.Sequence, []);

        var index = Index(parent, part);
        var children = parent.Node.Children.Where((_, i) => i != index).ToArray();

        return Swap(parent, parent.Node.With(children));
    }

    /// <summary>
    /// The whole tree, with <paramref name="node"/> among <paramref name="into"/>'s parts, at
    /// <paramref name="at"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="into"/> stands for characters rather than for parts. A piece cannot hold both:
    /// printing takes the parts of anything that has them and ignores its text, so putting a part inside
    /// a leaf would quietly drop what the leaf said.
    /// </exception>
    public static TexNode Insert(TexPart into, int at, TexNode node)
    {
        if (into.Node.IsLeaf && into.Node.Text.Length > 0)
            throw new ArgumentException(
                $"{into.Node.Kind} \"{into.Node.Text}\" stands for characters, so it holds no parts", nameof(into));

        var children = into.Node.Children.ToList();
        children.Insert(Math.Clamp(at, 0, children.Count), node);

        return Swap(into, into.Node.With(children));
    }

    /// <summary>
    /// The root of the tree <paramref name="at"/> belongs to, rebuilt so that it holds
    /// <paramref name="replacement"/> instead.
    /// </summary>
    private static TexNode Swap(TexPart at, TexNode replacement)
    {
        var node = replacement;

        for (var part = at; part.Parent is { } parent; part = parent)
        {
            var children = parent.Node.Children.ToArray();
            children[Index(parent, part)] = node;
            node = parent.Node.With(children);
        }

        return node;
    }

    /// <summary>Where <paramref name="child"/> sits among the parts of <paramref name="parent"/>.</summary>
    private static int Index(TexPart parent, TexPart child)
    {
        for (var i = 0; i < parent.Children.Count; i++)
            if (ReferenceEquals(parent.Children[i], child)) return i;

        throw new ArgumentException("that part is not one of this part's own", nameof(child));
    }
}
