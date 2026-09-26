using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.C4.Stages;

/// <summary>
/// Gathers each boundary with the lines written in it, so the tree says which boundary each element is drawn in.
///
/// <para>
/// What a boundary holds depends on every boundary and every <c>}</c> written above the line, and so is a fact about the
/// whole block rather than about any one line (<see cref="MermaidNesting.Nest"/>). Boundaries nest as deep as they are written,
/// and a <c>}</c> closes whichever was opened last — one stack, as C4-PlantUML's own reader has.
/// </para>
/// </summary>
public sealed class ResolveBoundaries : IAstStage
{
    public string Name => "c4:boundaries";

    public ContentNode Run(ContentNode tree) =>
        MermaidNesting.Nest(tree, [C4Kinds.Boundary], [C4Kinds.Ends],
                            stray: "This closes a boundary, and none is open here.",
                            unclosed: "Nothing closes this: a boundary is closed by } or by Boundary_End().");
}
