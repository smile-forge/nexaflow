using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.State.Stages;

/// <summary>
/// Says which composite state each line is written in, and which one each <c>state … {</c> line opens. Which box a state is drawn
/// in depends on every brace written above it, and so is a fact about the whole block rather than about any one line
/// (<see cref="MermaidNesting"/>) — and a <c>direction</c> line lays out the composite state it is in, which is the same fact.
/// </summary>
public sealed class ResolveComposites : IAstStage
{
    public string Name => "state:composites";

    public ContentNode Run(ContentNode tree) =>
        MermaidNesting.Inside(tree, StateKinds.Opens, StateKinds.Ends,
                              [StateKinds.State, StateKinds.Transition, StateKinds.Direction, StateKinds.Concurrent,
                               StateKinds.Note, StateKinds.NoteOpens, StateKinds.ClassDef, StateKinds.Class, StateKinds.Style],
                              StateKinds.Fact, StateRoles.Inside, StateRoles.Opened,
                              stray: "This brace closes a composite state, and none is open here.",
                              unclosed: "This composite state is never closed: } closes it.");
}
