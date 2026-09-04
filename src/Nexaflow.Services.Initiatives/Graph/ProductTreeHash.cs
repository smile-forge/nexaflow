using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Nexaflow.Services.Initiatives.Product.Model;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// A hash per tree node covering that node and everything under it, so a change can be found without
/// looking at every node.
/// <para>
/// Re-deriving the graph's product layer from scratch costs seconds on a tree this size, and almost every
/// edit touches one node. Hashing each node together with its children's hashes makes the difference
/// findable in a walk that stops the moment a subtree agrees: an edit to one leaf changes the hashes on its
/// ancestor path and nowhere else, so a diff visits the depth of the tree rather than the breadth of it.
/// </para>
/// <para>
/// It covers what the graph's product layer is <i>derived from</i> — title, status, description, the child
/// list, concerns and snaplinks — and nothing else. A field the graph does not read has no business
/// invalidating it, and a field it does read must be in here or an edit to it goes unnoticed, which is the
/// failure worth being careful about: everything the layer draws from is listed in one place below.
/// </para>
/// <para>
/// A line diff of the file was the alternative and is worse for the case that matters: a bulk move leaves
/// every node's own text intact while changing where it sits, which reads as no change at all.
/// </para>
/// </summary>
public static class ProductTreeHash
{
    /// <summary>The metadata key each product node carries its subtree hash under, so the comparison is
    /// against the graph as it stands rather than against a second copy of the tree kept beside it.</summary>
    public const string MetadataKey = "subtree_hash";

    /// <summary>
    /// What joins the fields before hashing: a character that cannot appear in a title, a path or an id.
    /// The failure an ordinary separator allows is silent — a node titled <c>a|b</c> and one titled
    /// <c>a</c> with a following field <c>b</c> would hash the same, and the diff would then skip a
    /// subtree that really had changed.
    /// </summary>
    private const char Sep = '\u001F';

    /// <summary>
    /// Every node's subtree hash, keyed by node id. Computed from the roots down so a child is always
    /// hashed before the parent that folds it in; a cycle (which the tree should not contain) is broken by
    /// treating the second visit as empty rather than recursing forever.
    /// </summary>
    public static Dictionary<string, string> Compute(ProductState state)
    {
        var hashes = new Dictionary<string, string>(state.Nodes.Count, StringComparer.Ordinal);
        var onPath = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in state.Nodes.Keys) Hash(state, id, hashes, onPath);
        return hashes;
    }

    private static string Hash(ProductState state, string id,
                               Dictionary<string, string> hashes, HashSet<string> onPath)
    {
        if (hashes.TryGetValue(id, out var done)) return done;
        if (!state.Nodes.TryGetValue(id, out var node)) return "";
        if (!onPath.Add(id)) return "";

        var text = new StringBuilder();
        text.Append(id).Append(Sep)
            .Append(node.Title).Append(Sep)
            .Append(node.Status).Append(Sep)
            .Append(node.Description).Append(Sep)
            .Append(node.Note).Append(Sep)
            .Append(node.Parent).Append(Sep);

        if (node.Snaplinks is { } links)
            foreach (var link in links) Append(text, link);

        if (node.Concerns is { } concerns)
            foreach (var concern in concerns)
            {
                text.Append(concern.Tag).Append(Sep).Append(concern.Status).Append(Sep);
                if (concern.Snaplinks is { } cl)
                    foreach (var link in cl) Append(text, link);
            }

        // Children fold in by hash, which is what makes an untouched branch answerable in one comparison.
        foreach (var child in node.Children)
            text.Append(child).Append(Sep).Append(Hash(state, child, hashes, onPath)).Append(Sep);

        onPath.Remove(id);
        return hashes[id] = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())), 0, 8);
    }

    private static void Append(StringBuilder text, Snaplink link)
    {
        text.Append(link.Type).Append(Sep)
            .Append(link.Status).Append(Sep)
            .Append(link.Doc).Append(Sep)
            .Append(link.Class).Append(Sep)
            .Append(link.Method).Append(Sep)
            .Append(link.Ast).Append(Sep)
            .Append(link.Target).Append(Sep);

        if (link.TitlePath is { } path)
            foreach (var segment in path) text.Append(segment).Append(Sep);

        text.Append(Sep);
    }
}
