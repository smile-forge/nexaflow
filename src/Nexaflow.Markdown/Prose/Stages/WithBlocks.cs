using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Prose.Stages;

/// <summary>
/// Reads each block's body with the parser its kind names.
///
/// <para>
/// The document parser splits and names and stops, which leaves every block holding one stretch of verbatim
/// source: its own. This is where that source is read — a paragraph's and a heading's into the constructs
/// they were spelled with, a table's into rows and cells, a list's into items, a quote's into the blocks
/// written inside it. Each reader goes one level and hands back blocks whose bodies are verbatim again, and
/// this keeps walking, so nesting costs nobody anything to write.
/// </para>
/// <para>
/// <strong>A stage rather than part of the parse, which is what keeps the builder from ever parsing.</strong>
/// A stage may replace a piece as long as the characters coming out are the ones that went in, and every
/// reader here checks that it did before handing anything back. So the builder is handed a tree that is
/// already read all the way down, and a block kind with no reader yet — a fence, indented code, front matter
/// — is still the one verbatim leaf it was, which is the fallback every builder already knows how to draw.
/// </para>
/// </summary>
public sealed class WithBlocks : IAstStage
{
    public string Name => "markdown:blocks";

    public ContentNode Run(ContentNode tree) => Read(tree);

    /// <summary>
    /// Which parser reads this kind of block, where one reads it. A kind that is not here is a kind nothing
    /// can read yet, and what it holds stays exactly as it was typed.
    /// </summary>
    private static Func<string, ContentNode>? Reader(string kind) => kind switch
    {
        MarkdownKinds.Paragraph or MarkdownKinds.Heading or MarkdownKinds.Cell =>
            source => MarkdownInline.Read(source).As(Roles.Body),

        MarkdownKinds.Quote or MarkdownKinds.Alert => source => MarkdownParser.Inside(source),

        MarkdownKinds.List => source => MarkdownList.Read(source),
        MarkdownKinds.Table => source => MarkdownTable.Read(source),

        _ => null,
    };

    /// <summary>
    /// This piece with its body read, and everything under it the same.
    ///
    /// <para>
    /// A body that is already a tree is left alone rather than read again, which is what makes running this
    /// twice the same as running it once — and is the same test that stops a reading whose own source reads
    /// back as itself from going round for ever.
    /// </para>
    /// </summary>
    private static ContentNode Read(ContentNode node)
    {
        if (node.IsLeaf) return node;

        if (Reader(node.Kind) is { } reader && node.Part(Roles.Body) is { IsLeaf: true, Kind: Kinds.Verbatim } body)
            node = node.With([.. node.Children.Select(child => ReferenceEquals(child, body) ? reader(body.Text) : child)]);

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
