using System.Collections.Generic;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Prose.Stages;

/// <summary>
/// Gathers what a reading hands back side by side into the things it makes together — in one walk of the tree, each node
/// asked by its kind whether what it holds is one of them.
///
/// <para>
/// A reader cuts the text and says what each piece is, and stops there: what pieces mean together is in none of them.
/// A definition list is read back as a flat run — a term, its paragraphs, the next term — when what it is, is pairs. An
/// alert opens with a <c>[!</c>, a name and a <c>]</c>, which together are the marker saying which kind of alert it is.
/// </para>
/// <para>
/// <strong>The tree says what the document is; the builder draws what the tree says.</strong> Without this a builder
/// would work out the pairs from where a block sits among its siblings, and the kind of an alert from its characters —
/// inferring structure, once per drawing, from the order things happen to be in.
/// </para>
/// <para>
/// One stage rather than one per construct, because each grouping is a question asked of one node's children and the
/// walk is what asking costs, so every grouping markdown knows is asked on the same walk. A stage only re-nests what is
/// already side by side, so the characters coming out are the ones that went in.
/// </para>
/// </summary>
public sealed class WithGroups : IAstStage
{
    public string Name => "markdown:groups";

    public ContentNode Run(ContentNode tree) => AstRewrite.Regrouping(tree, Grouped);

    /// <summary>What a node's children make together, where they make anything; null where they stay as they are.</summary>
    private static IReadOnlyList<ContentNode>? Grouped(ContentNode node, IReadOnlyList<ContentNode> children) => node switch
    {
        { Kind: MarkdownKinds.Alert } => Marked(children),
        { Role: Roles.Body } when Defines(children) => Paired(children),
        _ => null,
    };

    // ── Definitions ─────────────────────────────────────────────────────────

    /// <summary>Whether this run of blocks is a definition list's — which is to say, whether it holds a term.</summary>
    private static bool Defines(IReadOnlyList<ContentNode> children)
    {
        foreach (var child in children)
            if (child.Kind == MarkdownKinds.Term) return true;

        return false;
    }

    /// <summary>Each term with the blocks that explain it.</summary>
    private static List<ContentNode> Paired(IReadOnlyList<ContentNode> children)
    {
        var paired = new List<ContentNode>(children.Count);

        for (var at = 0; at < children.Count;)
        {
            paired.Add(children[at]);

            if (children[at++].Kind != MarkdownKinds.Term) continue;

            // Everything up to the next term is what this one means — trivia included, because the blank
            // line between two paragraphs of an explanation belongs to the explanation.
            var means = new List<ContentNode>();

            while (at < children.Count && children[at].Kind != MarkdownKinds.Term) means.Add(children[at++]);

            if (means.Count > 0) paired.Add(ContentNode.Branch(MarkdownKinds.Described, means));
        }

        return paired;
    }

    // ── Alerts ──────────────────────────────────────────────────────────────

    /// <summary>An alert's parts with its body's opening marks gathered into its marker, or null where they are not there.</summary>
    private static List<ContentNode>? Marked(IReadOnlyList<ContentNode> children)
    {
        var marked = new List<ContentNode>(children.Count);
        var moved = false;

        foreach (var child in children)
        {
            var seen = child.Role == Roles.Body && !child.IsLeaf ? Gathered(child) : child;
            moved |= !ReferenceEquals(seen, child);
            marked.Add(seen);
        }

        return moved ? marked : null;
    }

    /// <summary>A body whose first parts past its trivia are the marks and name an alert opens with, with those as one.</summary>
    private static ContentNode Gathered(ContentNode body)
    {
        var parts = body.Children;
        var at = 0;

        while (at < parts.Count && parts[at].Role == Roles.Trivia) at++;

        if (at + 2 >= parts.Count
            || parts[at].Role != Roles.Open
            || parts[at + 1].Role != Roles.Name
            || parts[at + 2].Role != Roles.Close) return body;

        var gathered = new List<ContentNode>(parts.Count - 2);

        for (var before = 0; before < at; before++) gathered.Add(parts[before]);
        gathered.Add(ContentNode.Branch(MarkdownKinds.Marker, [parts[at], parts[at + 1], parts[at + 2]]));
        for (var after = at + 3; after < parts.Count; after++) gathered.Add(parts[after]);

        return body.With(gathered);
    }
}
