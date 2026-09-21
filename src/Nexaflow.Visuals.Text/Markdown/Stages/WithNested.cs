using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Visuals.Text.Markdown.Stages;

/// <summary>
/// Says which language reads each piece of content written inside another — a fenced block of a document, a
/// tune on a flowchart node, a molecule in a song's lyrics.
///
/// <para>
/// A stage, because the answer is a fact about what the content <em>amounts to</em> rather than about the
/// characters, and because the table it is looked up in is the host's. It hangs the answer on the node and
/// nothing else: what the nested content is laid out against, and where it is put, stay with the builder that
/// is drawing the thing holding it (<see cref="ContentNesting"/>).
/// </para>
/// <para>
/// Nothing it adds is source. A derived part has no width and prints as nothing, so the one rule every stage
/// lives under — the characters coming out are the ones that went in — is kept whatever it hangs.
/// </para>
/// </summary>
/// <param name="style">What this showing of the content is drawn in.</param>
/// <param name="options">What the host said about diagrams, where it said anything.</param>
public sealed class WithNested(StyleFormat style, DiagramRenderOptions? options = null) : IAstStage
{
    public string Name => "content:nested";

    public ContentNode Run(ContentNode tree) => Read(tree);

    /// <summary>Whether what is written inside this piece is a whole other content, named by the writer.</summary>
    private static bool Nests(ContentNode node) =>
        (node.Kind == Kinds.Nested || node.Kind == MarkdownKinds.Fence)
        && node.Part(Roles.Name) is { Text.Length: > 0 }
        && node.Part(Roles.Body) is { Text.Length: > 0 };

    private ContentNode Read(ContentNode node)
    {
        if (node.IsLeaf) return node;

        // Already answered, which is what makes running this twice the same as running it once.
        if (Nests(node) && !node.Children.Any(child => child.Held is ContentNesting)
            && ContentLanguages.For(node.Part(Roles.Name)!.Text) is { } language)
            node = node.With([.. node.Children,
                              ContentNode.Holding(Kinds.Nested, Roles.Derived, new ContentNesting(language, style, options))]);

        var seen = new ContentNode[node.Children.Count];
        var moved = false;

        for (var at = 0; at < seen.Length; at++)
        {
            seen[at] = Read(node.Children[at]);
            moved |= !ReferenceEquals(seen[at], node.Children[at]);
        }

        return moved ? node.With(seen) : node;
    }
}
