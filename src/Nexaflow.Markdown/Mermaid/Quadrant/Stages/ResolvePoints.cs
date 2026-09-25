using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Quadrant.Stages;

/// <summary>
/// Works out each point — where it stands, and how it is drawn — and whether the x-axis's words go over the chart, and says
/// where a point takes a class no <c>classDef</c> writes.
///
/// <para>
/// Mermaid lets a class be defined above the points taking it or below them, so what a point is drawn in is a fact about the
/// whole block rather than the point's line: its class's style, with its own laid over that. And the x-axis's words go over
/// the chart only where there are no points to cover, unless the front matter says where — a fact about every line at once.
/// </para>
/// </summary>
/// <param name="config">What the front matter asks for, which the block carries for its builder.</param>
public sealed class ResolvePoints(QuadrantConfig config) : IAstStage
{
    public string Name => "quadrant:points";

    public ContentNode Run(ContentNode tree)
    {
        // The last class written of a name is the one taken.
        var classes = new Dictionary<string, QuadrantStyle>(StringComparer.Ordinal);
        foreach (var node in tree.SelfAndDescendants())
            if (node.Kind == QuadrantKinds.Class && Named(node) is { Length: > 0 } name)
                classes[name] = QuadrantStyle.Of(Properties(node));

        var points = 0;
        tree = AstRewrite.Each(tree, node => node.Kind == QuadrantKinds.Point ? Point(node) : node);
        return new QuadrantBlockNode(tree, config, config.XAxisOnTop ?? points == 0);

        ContentNode Point(ContentNode point)
        {
            var style = QuadrantStyle.None;
            if (point.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name) is { } taken && Named(point) is { Length: > 0 } name)
            {
                if (classes.TryGetValue(name, out var inherited)) style = inherited;
                else point = point.With([.. point.Children.Select(child => ReferenceEquals(child, taken) ? child.Saying($"No classDef {name} is written.") : child)]);
            }

            // A point whose name cannot be read is no point: there is nothing to call it.
            if (!point.Children.Any(child => child.Kind == QuadrantKinds.Text && child.Role == QuadrantRoles.Name && child.Words() is not null)) return point;

            points++;
            var amounts = point.Children.FirstOrDefault(child => child.Kind == QuadrantKinds.Position)?.Children
                              .Where(child => child.Kind == MermaidKinds.Amount).Take(2).ToArray() ?? [];

            return new QuadrantPointNode(point, amounts.ElementAtOrDefault(0).Number(), amounts.ElementAtOrDefault(1).Number(),
                                         style.With(QuadrantStyle.Of(Properties(point))));
        }
    }

    /// <summary>The class a line names — a point's after <c>:::</c>, a <c>classDef</c>'s own.</summary>
    private static string? Named(ContentNode line) => line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words()?.Text;

    private static ContentNode? Properties(ContentNode line) => line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Properties);
}
