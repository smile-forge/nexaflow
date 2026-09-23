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

    public ContentNode Run(ContentNode tree) => Read(tree, MarkdownDefinitions.Of(tree)?.Text);

    /// <summary>
    /// Which parser reads this block, where one reads it. A kind that is not here is a kind nothing can read
    /// yet, and what it holds stays exactly as it was typed.
    ///
    /// <para>
    /// Asked of the node rather than of its kind alone, because one kind is read two ways: a cell of a pipe
    /// table holds a run of words and a cell of a grid table can hold a whole document, and which it is was
    /// settled by the reader that took the table in.
    /// </para>
    /// </summary>
    /// <param name="besides">What the document defines, which the words of a block are read beside.</param>
    private static Func<string, ContentNode>? Reader(ContentNode node, string? besides) => node.Kind switch
    {
        MarkdownKinds.Cell when Holds(node) => source => MarkdownParser.Inside(source),

        MarkdownKinds.Paragraph or MarkdownKinds.Heading or MarkdownKinds.Cell
            or MarkdownKinds.Term or MarkdownKinds.Caption =>
            source => MarkdownInline.Read(source, besides: besides).As(Roles.Body),

        MarkdownKinds.Quote or MarkdownKinds.Alert or MarkdownKinds.Definition or MarkdownKinds.Described
            or MarkdownKinds.Figure or MarkdownKinds.Footer => source => MarkdownParser.Inside(source),

        MarkdownKinds.List => source => MarkdownList.Read(source),
        MarkdownKinds.Table => source => MarkdownTable.Read(source),

        _ => null,
    };

    /// <summary>Whether a stage said this piece holds blocks rather than words.</summary>
    private static bool Holds(ContentNode node)
    {
        foreach (var child in node.Children)
            foreach (var held in child.Children)
                if (held.Kind == MarkdownKinds.Blocks) return true;

        return false;
    }

    /// <summary>
    /// This piece with its body read, and everything under it the same.
    ///
    /// <para>
    /// A body that is already a tree is left alone rather than read again, which is what makes running this
    /// twice the same as running it once.
    /// </para>
    /// </summary>
    private static ContentNode Read(ContentNode node, string? besides)
    {
        if (node.IsLeaf) return node;

        if (Reader(node, besides) is { } reader && node.Part(Roles.Body) is { IsLeaf: true, Kind: Kinds.Verbatim } body
            && reader(body.Text) is { } read && !Circles(node, body, read))
            node = node.With([.. node.Children.Select(child => ReferenceEquals(child, body) ? read : child)]);

        var seen = new ContentNode[node.Children.Count];
        var moved = false;

        for (var at = 0; at < seen.Length; at++)
        {
            seen[at] = Read(node.Children[at], besides);
            moved |= !ReferenceEquals(seen[at], node.Children[at]);
        }

        return moved ? node.With(seen) : node;
    }

    /// <summary>
    /// Whether a reading came back as the very block it was asked to look inside.
    ///
    /// <para>
    /// Some blocks read as themselves when read alone: the <c>:</c> line of a definition is a definition item
    /// on its own, and a footer's line is a footer. Nothing was learned, and reading it once more would never
    /// stop — so the body stays the one stretch of source it was, which is what a kind with no reader gets
    /// anyway.
    /// </para>
    /// <para>
    /// Both halves are needed. The same kind alone is not enough, because a quotation inside a quotation is a
    /// quotation holding a quotation and that is exactly right — what says it went nowhere is that the block
    /// it found is the whole of what it was handed, character for character.
    /// </para>
    /// </summary>
    private static bool Circles(ContentNode node, ContentNode body, ContentNode read)
    {
        ContentNode? only = null;

        foreach (var child in read.Children)
        {
            if (child.Role == Roles.Trivia || child.IsDerived) continue;
            if (only is not null) return false;

            only = child;
        }

        return only is not null && only.Kind == node.Kind && only.Print() == body.Text;
    }
}
