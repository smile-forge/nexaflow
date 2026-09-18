using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Block.Stages;

/// <summary>
/// Says where a <c>class</c> or a <c>style</c> line names something nothing writes — a block laid out nowhere, or a class no
/// <c>classDef</c> declares. Mermaid allows the styling to be written above what it styles or below it, so whether what it
/// names exists is a fact about the whole block.
/// </summary>
public sealed class ResolveStyles : IAstStage
{
    public string Name => "block:styles";

    public ContentNode Run(ContentNode tree) =>
        BlockGrammar.Styling.Resolve(tree, [BlockKinds.Item, BlockKinds.Arrow], BlockKinds.ClassDef, BlockKinds.Class,
                                     BlockKinds.Style, id => $"No block {id} is laid out in this diagram.");
}
