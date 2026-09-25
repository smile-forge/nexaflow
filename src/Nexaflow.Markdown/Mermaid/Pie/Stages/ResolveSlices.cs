using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Pie.Stages;

/// <summary>
/// Makes each slice say what the config says about it — the colour it is drawn in, and whether it is the one picked out —
/// and the block say what the front matter asks of the whole chart, Mermaid's defaults filled in.
///
/// <para>
/// A slice's colour is written nowhere near it. Mermaid keeps the palette in the front matter as <c>pie1</c>…
/// <c>pie12</c>, by position, so which colour a slice takes is a fact about where it stands in the order and what the
/// block's front matter says — neither of which is in the characters of the line. So it is worked out here and said on
/// the slice, naming the key it came from as well as the colour, which is what lets a restyle know where to write.
/// </para>
/// <para>
/// A slice the config does not colour says nothing at all, rather than saying which colour the theme would use: what a
/// palette's third colour is belongs to whoever is drawing, and a tree that decided it here would go stale the moment
/// the theme changed.
/// </para>
/// </summary>
public sealed class ResolveSlices(PieConfig config) : IAstStage
{
    public string Name => "pie:slices";

    public ContentNode Run(ContentNode tree)
    {
        // Depth first, so the slices are met in the order they were written — which is the order they are drawn.
        var order = 0;
        return new PieBlockNode(AstRewrite.Each(tree, node => node.Kind == PieKinds.Slice ? Said(node, order++) : node), config);
    }

    private PieSliceNode Said(ContentNode slice, int order)
    {
        // The palette repeats: a thirteenth slice takes pie1 again, as Mermaid's does.
        var number = (order % 12) + 1;
        var coloured = config.Swatches.TryGetValue(number, out var colour);

        return new PieSliceNode(slice)
        {
            Colour = coloured ? colour : null,
            Swatch = coloured ? number : null,
            Highlighted = !config.HighlightsOnHover && Named(slice) is { } name
                          && string.Equals(name, config.Highlight, StringComparison.Ordinal),
        };
    }

    /// <summary>What a slice is called, or null for one whose label could not be read.</summary>
    private static string? Named(ContentNode slice) => slice.Inner(MermaidKinds.Words)?.Text;
}
