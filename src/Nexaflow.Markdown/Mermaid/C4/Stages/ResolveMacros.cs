using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.C4.Stages;

/// <summary>
/// Says what each macro means (<see cref="C4ElementNode"/>, <see cref="C4RelationNode"/>, <see cref="C4BoundaryNode"/>,
/// <see cref="C4LegendNode"/>), and what each of its arguments is to what is drawn (<see cref="C4ArgumentNode"/>).
///
/// <para>
/// A C4 block says most of what it means across its lines rather than on any one: an <c>UpdateElementStyle</c> or an
/// <c>AddElementTag</c> may be written under the elements it paints, a <c>HIDE_STEREOTYPE()</c> anywhere hides every card's
/// stereotype, and a relationship's number is wherever the <c>Index()</c>, <c>SetIndex()</c> and <c>increment()</c> above it have
/// moved the count. So this reads the whole block first (<see cref="C4Said"/>), and then says what each line comes to. The two
/// diagrams C4 draws — a graph and a timeline — read the same meaning off the same lines.
/// </para>
/// </summary>
/// <param name="numbered">Whether the block numbers its relationships without being asked, as a <c>C4Dynamic</c> does.</param>
public sealed class ResolveMacros(bool numbered) : IAstStage
{
    public string Name => "c4:macros";

    public ContentNode Run(ContentNode tree)
    {
        var said = C4Said.Read(tree);
        var counter = new C4Counter();
        var numbering = said.Numbered || numbered;

        var lines = new Dictionary<ContentNode, ContentNode>(ReferenceEqualityComparer.Instance);
        var arguments = new Dictionary<ContentNode, C4Means>(ReferenceEqualityComparer.Instance);

        foreach (var stated in tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line).Select(line => line.Stated()).OfType<ContentNode>())
        {
            if (stated.Kind == C4Kinds.Boundary)
            {
                Bounded(stated, said, lines, arguments);
                continue;
            }

            if (stated.Kind != C4Kinds.Macro) continue;

            var macro = C4Macro.Of(stated);
            var word = macro.Name.ToLowerInvariant();

            if (C4Grammar.Elemental(macro.Name)) Standing(stated, macro, said, lines, arguments);
            else if (word.StartsWith("relindex", StringComparison.Ordinal)) Related(stated, macro, said, counter, numbering, lines, arguments, 1, false, false);
            else if (word.StartsWith("rel_back", StringComparison.Ordinal)) Related(stated, macro, said, counter, numbering, lines, arguments, 0, true, false);
            else if (word.StartsWith("birel", StringComparison.Ordinal)) Related(stated, macro, said, counter, numbering, lines, arguments, 0, false, true);
            else if (word.StartsWith("rel", StringComparison.Ordinal)) Related(stated, macro, said, counter, numbering, lines, arguments, 0, false, false);
            else if (word == "increment") counter.Increment(C4Macro.Number(macro.Said(0, "offset")) ?? 1);
            else if (word == "setindex" && C4Macro.Number(macro.Said(0, "new_index")) is { } at) counter.Set(at);
            else if (word is "show_legend" or "layout_with_legend" && said.Legended) lines[stated] = new C4LegendNode(stated, said.Legend);
        }

        if (lines.Count == 0) return tree;

        // Each line first, while its arguments are still the ones read, then each argument — the line keeping what it was said to be.
        tree = AstRewrite.Each(tree, node => lines.TryGetValue(node, out var meant) ? meant : node);
        return AstRewrite.Each(tree, node => arguments.TryGetValue(node, out var means) ? new C4ArgumentNode(node, means) : node);
    }

    /// <summary>An element: what its name makes it, and the card that says so.</summary>
    private static void Standing(ContentNode stated, C4Macro macro, C4Said said, Dictionary<ContentNode, ContentNode> lines,
                                 Dictionary<ContentNode, C4Means> arguments)
    {
        if (macro.Argument(0, "alias") is not { Value: { Width: > 0 } alias } named) return;

        var (level, shape, external) = C4Elements.Sorted(macro.Name);

        // A Person and a System take (alias, label, descr); a Container and a Component put what they are built with at 2
        // and push the description to 3. That asymmetry is C4-PlantUML's.
        var built = level is C4Level.Container or C4Level.Component;
        var technology = macro.Argument(built ? 2 : -1, "techn");
        var tags = C4Macro.Tagged(macro.Said(built ? 5 : 4, "tags"));
        var style = said.Painted(level, shape, external, alias.Text, tags);

        lines[stated] = new C4ElementNode(stated, alias.Text, level, C4Elements.Shaped(shape, style.Shape), external,
                                          C4Elements.Stereotyped(level, external, technology?.Value?.Text, macro.Named("type"), said.Hidden),
                                          style, macro.Named("link"), said.Described);

        Means(arguments, named, C4Means.Alias);
        Means(arguments, macro.Argument(1, "label"), C4Means.Label);
        Means(arguments, technology, C4Means.Technology);
        Means(arguments, macro.Argument(built ? 3 : 2, "descr"), C4Means.Description);
    }

    /// <summary>A boundary or a deployment node: the box it draws round what is written in it.</summary>
    private static void Bounded(ContentNode stated, C4Said said, Dictionary<ContentNode, ContentNode> lines, Dictionary<ContentNode, C4Means> arguments)
    {
        var macro = C4Macro.Of(stated);
        if (macro.Argument(0, "alias") is not { Value: { Width: > 0 } alias } named) return;

        var word = macro.Name.ToLowerInvariant();
        var physical = word is "deployment_node" or "node" or "node_l" or "node_r";

        var type = word switch
        {
            "enterprise_boundary" => "Enterprise",
            "system_boundary" => "System",
            "container_boundary" => "Container",
            _ => macro.Said(2, "type"),
        };

        lines[stated] = new C4BoundaryNode(stated, alias.Text,
                                           physical
                                               ? type is { Length: > 0 } runs ? $"[Deployment Node: {runs}]" : "[Deployment Node]"
                                               : type is { Length: > 0 } kind ? $"[{kind}]" : null,
                                           physical, said.Bounded(macro, alias.Text), macro.Named("link"));

        Means(arguments, named, C4Means.Alias);
        Means(arguments, macro.Argument(1, "label"), C4Means.Label);
    }

    /// <summary>A relationship: the ends it joins, the line it is drawn with, and its number where the block counts them.</summary>
    private static void Related(ContentNode stated, C4Macro macro, C4Said said, C4Counter counter, bool numbering,
                                Dictionary<ContentNode, ContentNode> lines, Dictionary<ContentNode, C4Means> arguments, int offset, bool back, bool both)
    {
        if (macro.Argument(offset, "from") is not { Value: { Width: > 0 } one } leaves) return;
        if (macro.Argument(offset + 1, "to") is not { Value: { Width: > 0 } other } reaches) return;

        // Rel_Back declares from→to and points the other way, so the line is simply built reversed.
        var style = said.Relating(one.Text, other.Text, C4Macro.Tagged(macro.Said(offset + 6, "tags")));
        var number = offset == 1 ? counter.Resolve(macro.Said(0, "index")) : counter.Resolve(macro.Named("index"));

        lines[stated] = new C4RelationNode(stated, back ? other.Text : one.Text, back ? one.Text : other.Text, both, style,
                                           numbering ? (number ?? counter.Next()).ToString(CultureInfo.InvariantCulture) : null);

        Means(arguments, back ? reaches : leaves, C4Means.From);
        Means(arguments, back ? leaves : reaches, C4Means.To);
        Means(arguments, macro.Argument(offset + 2, "label"), C4Means.Label);
        Means(arguments, macro.Argument(offset + 3, "techn"), C4Means.Technology);
        Means(arguments, macro.Argument(offset + 4, "descr"), C4Means.Description);
    }

    private static void Means(Dictionary<ContentNode, C4Means> arguments, C4Argument? argument, C4Means means)
    {
        if (argument is { Value.Width: > 0 }) arguments[argument.Property] = means;
    }
}
