using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Block.Stages;

/// <summary>
/// Says which composite each line is in, and which one each <c>block:</c> line opens. Where a block is drawn depends on the
/// grid it is in, and which grid that is depends on every <c>block:</c> and <c>end</c> above it — so it is a fact about the
/// whole block rather than about any one line (<see cref="MermaidNesting"/>).
/// </summary>
public sealed class ResolveBlocks : IAstStage
{
    public string Name => "block:nesting";

    public ContentNode Run(ContentNode tree) =>
        MermaidNesting.Inside(tree, [BlockKinds.Opens], BlockKinds.Ends, [BlockKinds.Items, BlockKinds.Columns],
                              BlockKinds.Fact, BlockRoles.Inside, BlockRoles.Opened,
                              stray: "This end closes a composite, and none is open here.",
                              unclosed: "This composite is never closed: end closes it.");
}
