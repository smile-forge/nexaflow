using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Cynefin.Stages;

/// <summary>
/// Says which domain each item sits in: the one opened by the nearest domain line above it. An item written before any
/// domain is opened sits in none, and there is nowhere to draw it, so it says so.
/// </summary>
public sealed class ResolveDomains : IAstStage
{
    public string Name => "cynefin:domains";

    public ContentNode Run(ContentNode tree) =>
        MermaidGrouping.Under(tree, CynefinKinds.Domain, CynefinKinds.Item, CynefinKinds.Fact, CynefinRoles.In,
            named: Opened, alone: "An item sits in the domain opened above it: complex, then what is in it.");

    /// <summary>The domain a line opens, as the word it is opened by says it, however it is written.</summary>
    private static string Opened(ContentNode domain) =>
        domain.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Key)?.Text.ToLowerInvariant() ?? string.Empty;
}
