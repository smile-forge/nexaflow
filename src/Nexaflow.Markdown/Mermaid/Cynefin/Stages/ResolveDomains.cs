using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Cynefin.Stages;

/// <summary>
/// Says where an item is written before any domain is opened: it sits in none, and there is nowhere to draw it. Which domain
/// each of the others sits in is the order the lines are written in.
/// </summary>
public sealed class ResolveDomains : IAstStage
{
    public string Name => "cynefin:domains";

    public ContentNode Run(ContentNode tree) =>
        MermaidGrouping.Unopened(tree, CynefinKinds.Domain, CynefinKinds.Item,
            "An item sits in the domain opened above it: complex, then what is in it.");
}
