using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Block.Stages;

/// <summary>
/// Works out what styles each block. A <c>class</c> or a <c>style</c> line naming a block laid out nowhere, or a class no
/// <c>classDef</c> declares, styles nothing, as in Mermaid. Mermaid allows the styling to be written above what it styles
/// or below it, so both are facts about the whole block (<see cref="MermaidStyling"/>): what styles a block is said on the name
/// first writing it.
/// </summary>
public sealed class ResolveStyles : IAstStage
{
    public string Name => "block:styles";

    public ContentNode Run(ContentNode tree)
    {
        var styling = BlockParser.Styling;



        return styling.Style(tree, [BlockKinds.Item, BlockKinds.Arrow], BlockKinds.ClassDef, BlockKinds.Class, BlockKinds.Style);
    }
}
