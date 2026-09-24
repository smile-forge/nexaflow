using System.Collections.Generic;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Prose.Stages;

/// <summary>
/// Gathers each term in a definition list with the blocks that explain it.
///
/// <para>
/// The reader hands back a flat run — a term, its paragraphs, the next term, its paragraphs — because a
/// definition item covers the whole of what it was asked to read and a reading that stopped there would
/// have learned nothing. What the list actually is, though, is pairs: a term and what it means. So they
/// are re-nested here, where saying it costs one pass and nothing downstream has to work it out again.
/// </para>
/// <para>
/// <strong>The tree says what the document is; the builder draws what the tree says.</strong> Without this
/// a builder would have to look at where a block sits among its siblings to know whether to set it in —
/// which is the builder inferring structure, once per drawing, from the order things happen to be in.
/// </para>
/// <para>
/// A stage only re-nests what is already side by side, so the characters coming out are the ones that went
/// in and a definition list prints back exactly as it was typed.
/// </para>
/// </summary>
public sealed class WithDefinitions : IAstStage
{
    public string Name => "markdown:definitions";

    public ContentNode Run(ContentNode tree) =>
        AstRewrite.Regrouping(tree, (node, children) => node.Role == Roles.Body && Defines(children) ? Paired(children) : null);

    /// <summary>Whether this run of blocks is a definition list's — which is to say, whether it holds a term.</summary>
    private static bool Defines(IReadOnlyList<ContentNode> children)
    {
        foreach (var child in children)
            if (child.Kind == MarkdownKinds.Term) return true;

        return false;
    }

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
}
