using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Flowchart.Stages;

/// <summary>
/// Works out what styles each node and each subgraph. A <c>style</c> line naming a node written nowhere makes that node, which it
/// styles; a <c>class</c> line naming one, or a class no <c>classDef</c> declares, styles nothing — both as in Mermaid. Mermaid lets the styling be written above what it
/// styles or below it, and a node take a class wherever it is written (<c>A:::blue</c>), so both are facts about the whole block
/// (<see cref="MermaidStyling"/>): what styles something is said on the name first writing it.
/// </summary>
public sealed class ResolveStyles : IAstStage
{
    public string Name => "flowchart:styles";

    public ContentNode Run(ContentNode tree)
    {
        var styling = FlowchartParser.Styling;

        return styling.Style(tree, [FlowchartKinds.Node, FlowchartKinds.Opens, FlowchartKinds.Said],
                             FlowchartKinds.ClassDef, FlowchartKinds.Class, FlowchartKinds.Style, Given, making: [FlowchartKinds.Style]);
    }

    /// <summary>The classes a node is given where it is written — the <c>:::chosen</c> after it — each with its id.</summary>
    private static IEnumerable<(string Id, string Class)> Given(ContentNode node)
    {
        if (node.Kind != FlowchartKinds.Node) return [];
        if (node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words()?.Text is not { Length: > 0 } id) return [];

        return node.SelfAndDescendants()
                   .Where(inner => inner.Kind == Kinds.Words && inner.Role == FlowchartRoles.Class && inner.Text.Length > 0)
                   .Select(inner => (id, inner.Text));
    }
}
