using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.C4.Stages;

/// <summary>
/// Says which boundary each line is written inside, and which one each line that opens one opens.
///
/// <para>
/// What a boundary holds depends on every boundary and every <c>}</c> written above the line, and so is a fact about the
/// whole block rather than about any one line (<see cref="MermaidNesting"/>). Boundaries nest as deep as they are written,
/// and a <c>}</c> closes whichever was opened last — one stack, as C4-PlantUML's own reader has.
/// </para>
/// </summary>
public sealed class ResolveBoundaries : IAstStage
{
    public string Name => "c4:boundaries";

    public ContentNode Run(ContentNode tree) =>
        MermaidNesting.Inside(tree, [C4Kinds.Boundary], [C4Kinds.Ends], [C4Kinds.Macro, C4Kinds.Aside],
                              C4Kinds.Fact, C4Roles.Inside, C4Roles.Opened,
                              stray: "This closes a boundary, and none is open here.",
                              unclosed: "Nothing closes this: a boundary is closed by } or by Boundary_End().");
}
