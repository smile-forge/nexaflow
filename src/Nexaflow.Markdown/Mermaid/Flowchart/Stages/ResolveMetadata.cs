using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Flowchart.Stages;

/// <summary>
/// Says what each <c>id@{ … }</c> line means (<see cref="FlowchartMetadataNode"/>): what it is about, and what it says of a node.
///
/// <para>
/// Mermaid lets such a line name a node or a link written below it as well as above, and makes a node of an id nothing else
/// writes — unless a subgraph is called that, which is the subgraph — so what it is about is a fact about the whole block. What it
/// says of a node is its properties read: the shape <c>shape:</c> names, the words <c>label:</c> or <c>title:</c> draw with their
/// quotes taken off, and the picture <c>img:</c> or the icon <c>icon:</c> is drawn as, framed and sized as it asks.
/// </para>
/// </summary>
public sealed class ResolveMetadata : IAstStage
{
    public string Name => "flowchart:metadata";

    public ContentNode Run(ContentNode tree)
    {
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        var groups = new HashSet<string>(StringComparer.Ordinal);
        var links = new HashSet<string>(StringComparer.Ordinal);
        var said = new List<ContentNode>();

        foreach (var node in tree.SelfAndDescendants())
        {
            switch (node.Kind)
            {
                case FlowchartKinds.Opens:
                    if (node.Inner(FlowchartKinds.Node)?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words()?.Text is { Length: > 0 } box)
                        groups.Add(box);
                    break;

                case FlowchartKinds.Nodes:
                    foreach (var piece in node.Children)
                    {
                        if (piece.Kind == FlowchartKinds.Node && piece.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words()?.Text is { Length: > 0 } id)
                            nodes.Add(id);

                        if (piece.Kind == FlowchartKinds.Link && piece.Children.FirstOrDefault(child => child.Role == FlowchartRoles.Link)?.Text is { Length: > 0 } link)
                            links.Add(link);
                    }

                    break;

                case FlowchartKinds.Said:
                    said.Add(node);
                    break;
            }
        }

        if (said.Count == 0) return tree;

        var meant = new Dictionary<ContentNode, FlowchartMetadataNode>(ReferenceEqualityComparer.Instance);

        foreach (var line in said)
        {
            if (Id(line) is not { Length: > 0 } id) continue;

            var about = groups.Contains(id) ? FlowchartSaid.Group
                        : nodes.Contains(id) ? FlowchartSaid.Node
                        : links.Contains(id) ? FlowchartSaid.Link
                        : FlowchartSaid.New;

            var properties = line.Inner(MermaidKinds.Properties);
            var label = Set(properties, "label") ?? Set(properties, "title");

            meant[line] = new FlowchartMetadataNode(
                line, about,
                Set(properties, "shape") is { } shape ? MermaidShapes.Named(shape.Text) ?? MermaidShape.Rectangle : null,
                label is null ? null : MermaidText.Decode(MermaidText.Bare(label.Text)),
                Pictured(properties));
        }

        return AstRewrite.Each(tree, node => meant.TryGetValue(node, out var said) ? said : node);
    }

    /// <summary>What an <c>id@{ … }</c> line names.</summary>
    internal static string? Id(ContentNode line) =>
        line.SelfAndDescendants().FirstOrDefault(node => node.Kind == MermaidKinds.Words && node.Role == FlowchartRoles.Id)?.Text;

    /// <summary>
    /// What a property of some metadata is set to, or null where the metadata does not set it — an icon's name as written, inside
    /// what says it names one.
    /// </summary>
    internal static ContentNode? Set(ContentNode? properties, string name) =>
        properties?.Children
            .Where(property => property.Kind == MermaidKinds.Property
                               && string.Equals(Role(property, Roles.Name)?.Text, name, StringComparison.OrdinalIgnoreCase))
            .Select(property => Role(property, MermaidRoles.Value) is { Kind: MermaidKinds.Icon } icon ? Role(icon, MermaidRoles.Value) : Role(property, MermaidRoles.Value))
            .FirstOrDefault(value => value is { Width: > 0 });

    /// <summary>The picture or icon some metadata names, and how it is asked to be drawn — or null where it names neither.</summary>
    private static FlowchartPicture? Pictured(ContentNode? properties)
    {
        var picture = Set(properties, "img");
        if ((picture ?? Set(properties, "icon")) is not { } named) return null;

        return new FlowchartPicture(picture is null ? Bared(named) : null,
                                    Bared(Set(properties, "form")) is { Length: > 0 } form ? form.ToLowerInvariant() : null,
                                    string.Equals(Bared(Set(properties, "pos")), "t", StringComparison.OrdinalIgnoreCase),
                                    Measured(Set(properties, "w")), Measured(Set(properties, "h")),
                                    string.Equals(Bared(Set(properties, "constraint")), "on", StringComparison.OrdinalIgnoreCase));

        static string? Bared(ContentNode? value) => value is null ? null : MermaidText.Bare(value.Text).Trim();

        static double? Measured(ContentNode? value) => MermaidNumber.Read(Bared(value)) is { } size && size > 0 ? size : null;
    }

    private static ContentNode? Role(ContentNode node, string role) => node.Children.FirstOrDefault(child => child.Role == role);
}
