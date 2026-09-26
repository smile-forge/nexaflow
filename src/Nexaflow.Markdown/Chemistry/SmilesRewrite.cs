using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Chemistry;

/// <summary>
/// The shapes a SMILES stage is: find each molecule, and change what it says about some of its atoms or ring
/// closures, picked out by the order they are written in — the number a <see cref="MoleculeNode"/>'s bonds name atoms by.
///
/// <para>
/// Every one returns the node it was given where nothing under it moved, so an untouched molecule is shared rather
/// than rebuilt — the same promise <see cref="Pipeline.AstRewrite"/> makes.
/// </para>
/// </summary>
internal static class SmilesRewrite
{
    /// <summary>The tree with every molecule in it handed to <paramref name="of"/>.</summary>
    public static ContentNode Molecules(ContentNode node, Func<ContentNode, ContentNode> of)
    {
        if (node.Kind == SmilesKinds.Molecule) return of(node);
        if (node.IsLeaf) return node;

        List<ContentNode>? rebuilt = null;

        for (var i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            var seen = Molecules(child, of);
            if (ReferenceEquals(seen, child) && rebuilt is null) continue;

            rebuilt ??= [.. node.Children.Take(i)];
            rebuilt.Add(seen);
        }

        return rebuilt is null ? node : node.With(rebuilt);
    }

    /// <summary>
    /// The molecule with each atom handed to <paramref name="of"/> with its index — the order atoms are written in,
    /// branches included.
    /// </summary>
    public static ContentNode Atoms(ContentNode molecule, Func<int, ContentNode, ContentNode> of)
    {
        var count = 0;
        return Each(molecule, SmilesKinds.Atom, ref count, of);
    }

    /// <summary>The molecule with each ring closure handed to <paramref name="of"/> with its index among them.</summary>
    public static ContentNode RingBonds(ContentNode molecule, Func<int, ContentNode, ContentNode> of)
    {
        var count = 0;
        return Each(molecule, SmilesKinds.RingBond, ref count, of);
    }

    private static ContentNode Each(ContentNode node, string kind, ref int count, Func<int, ContentNode, ContentNode> of)
    {
        if (node.Kind == kind) return of(count++, node);
        if (node.IsLeaf || node.Role == Roles.Derived) return node;

        List<ContentNode>? rebuilt = null;

        for (var i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            var seen = Each(child, kind, ref count, of);
            if (ReferenceEquals(seen, child) && rebuilt is null) continue;

            rebuilt ??= [.. node.Children.Take(i)];
            rebuilt.Add(seen);
        }

        return rebuilt is null ? node : node.With(rebuilt);
    }

    /// <summary>
    /// The same piece with <paramref name="trouble"/> added to whatever it already said — a later stage never
    /// quietly replaces an earlier one's reason.
    /// </summary>
    public static ContentNode Troubled(ContentNode node, string? trouble) =>
        trouble is null ? node : node.Saying(node.Trouble is null ? trouble : $"{node.Trouble} {trouble}");
}
