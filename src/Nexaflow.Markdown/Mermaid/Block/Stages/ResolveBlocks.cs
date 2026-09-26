using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Block.Stages;

/// <summary>
/// Gathers each composite with the lines written in it, so the tree says which grid every block is laid out in. That depends on
/// every <c>block:</c> and <c>end</c> above it, and so is a fact about the whole block rather than about any one line
/// (<see cref="MermaidNesting.Nest"/>).
/// </summary>
public sealed class ResolveBlocks : IAstStage
{
    public string Name => "block:nesting";

    public ContentNode Run(ContentNode tree) =>
        MermaidNesting.Nest(tree, [BlockKinds.Opens], [BlockKinds.Ends],
                            stray: "This end closes a composite, and none is open here.",
                            unclosed: "This composite is never closed: end closes it.");
}
