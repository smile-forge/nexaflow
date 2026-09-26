using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.State.Stages;

/// <summary>
/// Gathers each composite state with the lines written in it, so the tree says which box every state is drawn in and which
/// composite state a <c>direction</c> line lays out. That depends on every brace written above it, and so is a fact about the
/// whole block rather than about any one line (<see cref="MermaidNesting.Nest"/>).
/// </summary>
public sealed class ResolveComposites : IAstStage
{
    public string Name => "state:composites";

    public ContentNode Run(ContentNode tree) =>
        MermaidNesting.Nest(tree, [StateKinds.Opens], [StateKinds.Ends],
                            stray: "This brace closes a composite state, and none is open here.",
                            unclosed: "This composite state is never closed: } closes it.");
}
