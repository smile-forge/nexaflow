using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Pie.Stages;

/// <summary>
/// What each slice comes to, said on the slice: its share of the whole, where it comes among the slices whose value
/// counts, whether it has a row in the legend, and whether that row shows its value.
///
/// <para>
/// None of it is in the slice's own characters. A share is its value against every other slice's; its place among the
/// slices that count is how many before it had a value of nought or more, which is what gives a slice worth nought its
/// colour in the legend though it has no wedge, as Mermaid's does; and whether it is listed and what its row shows
/// depend on whether somebody is writing the chart and on the <c>showData</c> word in its header. So a builder is
/// handed the answers and only lays them out.
/// </para>
/// </summary>
/// <param name="writing">
/// Whether somebody is writing in the chart: then every slice written has a row, drawn or not — one still waiting for its
/// value is where the reader is typing, and a row that went away would take the caret with it — and a row shows its value
/// wherever the value is still to be written.
/// </param>
public sealed class ResolveShares(bool writing) : IAstStage
{
    public string Name => "pie:shares";

    public ContentNode Run(ContentNode tree)
    {
        var showsData = false;
        var worths = new List<double?>();

        foreach (var node in tree.SelfAndDescendants())
        {
            if (node.Kind == PieKinds.ShowData) showsData = true;
            else if (node.Kind == PieKinds.Slice) worths.Add(Worth(node));
        }

        var total = worths.Sum(worth => worth ?? 0);
        var (at, counted) = (0, 0);

        return AstRewrite.Each(tree, node => node.Kind == PieKinds.Slice ? Said(node, worths[at++]) : node);

        PieSliceNode Said(ContentNode slice, double? worth)
        {
            var value = slice.Inner(Kinds.Number);
            var unwritten = writing && slice.Inner(MermaidKinds.Amount) is { Width: 0 };

            return new PieSliceNode(slice)
            {
                Share = worth is > 0 && total > 0 ? worth.Value / total : 0.0,
                Order = worth is not null ? counted++ : -1,
                Listed = worth is not null || writing,
                ValueShown = value is not null && (showsData || unwritten || value.Trouble is not null),
            };
        }
    }

    /// <summary>What a slice's value comes to — null where none is written, or it is not a number of nought or more.</summary>
    private static double? Worth(ContentNode slice) =>
        slice.Inner(Kinds.Number) is { Trouble: null, Width: > 0 } number ? MermaidNumber.Read(number.Text) : null;
}
