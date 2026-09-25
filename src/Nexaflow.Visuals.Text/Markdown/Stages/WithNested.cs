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

    /// <summary>What <c>$$ … $$</c> is written in, which nobody writes after the delimiter.</summary>
    private const string Maths = "latex";

    /// <summary>How much bigger than the words around it a formula on its own line is set.</summary>
    private const double Display = 1.5;

    public ContentNode Run(ContentNode tree)
    {
        // Each reading hands the diagrams their states afresh, in the order they are written.
        options?.Views?.Rewind();

        return Read(tree);
    }

    /// <summary>Whether what is written inside this piece is a whole other content.</summary>
    private static bool Nests(ContentNode node) => Names(node) is not null && node.Part(Roles.Body) is not null;

    /// <summary>
    /// Which language a piece is written in: the one named after its fence, or the one its own kind implies.
    ///
    /// <para>
    /// Maths names none, because the <c>$$</c> is the name — a writer does not spell "latex" after it and
    /// never has. That is a fact about the delimiter rather than about the body, so it is settled here with
    /// every other question of which language reads what.
    /// </para>
    /// </summary>
    private static string? Names(ContentNode node) => node.Kind switch
    {
        MarkdownKinds.Math or MarkdownKinds.Formula => Maths,
        Kinds.Nested or MarkdownKinds.Fence => node.Part(Roles.Name) is { Text.Length: > 0 } name ? name.Text.Trim() : null,
        _ => null,
    };

    /// <summary>
    /// How big a piece is set, which is a fact about the content and not about the room it lands in: a formula
    /// on its own line is drawn half as big again as the words around it, and one in the middle of a sentence
    /// at the size of the sentence.
    /// </summary>
    private StyleFormat Drawn(ContentNode node) => node.Kind switch
    {
        MarkdownKinds.Math => style with { TextSize = style.TextSize * Display, InlineMath = false },
        MarkdownKinds.Formula => style with { InlineMath = true },
        _ => style,
    };

    /// <summary>
    /// What the reader has opened in a diagram, which is the diagram's by the order it is written in — the one thing a
    /// reading of the same document again keeps, where the source is exactly what may have changed.
    /// </summary>
    private StyleFormat Kept(ContentNode node, StyleFormat drawn) =>
        node.Kind is MarkdownKinds.Fence or Kinds.Nested && options?.Views is { } views ? drawn with { Expansion = views.Next() } : drawn;

    private ContentNode Read(ContentNode node)
    {
        // Already answered, which is what makes running this twice the same as running it once.
        if (!node.IsLeaf && Nests(node) && !node.Children.Any(child => child.Held is ContentNesting)
            && Names(node) is { } named && ContentLanguages.For(named) is { } language)
            node = node.With([.. node.Children,
                              ContentNode.Holding(Kinds.Nested, Roles.Derived,
                                                  new ContentNesting(language, named, Kept(node, Drawn(node)), options, Own(node.Part(Roles.Body)!)))]);

        if (node.IsLeaf) return node;

        var seen = new ContentNode[node.Children.Count];
        var moved = false;

        for (var at = 0; at < seen.Length; at++)
        {
            seen[at] = Read(node.Children[at]);
            moved |= !ReferenceEquals(seen[at], node.Children[at]);
        }

        return moved ? node.With(seen) : node;
    }

    /// <summary>
    /// What the language is handed to read: the body as written, less the line ending that closes its last line — which belongs
    /// to the line the closing delimiter stands on (<see cref="ContentNesting.Own"/>).
    /// </summary>
    private static string Own(ContentNode body)
    {
        var written = body.Print();
        return written.EndsWith("\r\n", System.StringComparison.Ordinal) ? written[..^2] : written.EndsWith('\n') ? written[..^1] : written;
    }
}
