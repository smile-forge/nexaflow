using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Block.Stages;

/// <summary>
/// Works out what styles each block, and says where a <c>class</c> or a <c>style</c> line names something nothing writes — a
/// block laid out nowhere, or a class no <c>classDef</c> declares. Mermaid allows the styling to be written above what it styles
/// or below it, so both are facts about the whole block (<see cref="MermaidStyling"/>): what styles a block is said on the name
/// first writing it.
/// </summary>
public sealed class ResolveStyles : IAstStage
{
    public string Name => "block:styles";

    public ContentNode Run(ContentNode tree)
    {
        var styling = BlockGrammar.Styling;

        tree = styling.Resolve(tree, [BlockKinds.Item, BlockKinds.Arrow], BlockKinds.ClassDef, BlockKinds.Class,
                               BlockKinds.Style, id => $"No block {id} is laid out in this diagram.");

        return styling.Style(tree, [BlockKinds.Item, BlockKinds.Arrow], BlockKinds.ClassDef, BlockKinds.Class, BlockKinds.Style);
    }
}
