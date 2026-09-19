using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Sequence.Stages;

/// <summary>
/// Says which box or frame each line is written in, and which one each line that opens one opens. How deep a message is drawn
/// and what is boxed round it depend on every <c>alt</c>, <c>box</c> and <c>end</c> written above it, and so are facts about the
/// whole block rather than about any one line (<see cref="MermaidNesting"/>).
///
/// <para>
/// A box and a frame are both closed by <c>end</c>, so the two are one nesting rather than two: an <c>end</c> closes whichever
/// was opened last, which is what Mermaid's own parser does with one stack.
/// </para>
/// </summary>
public sealed class ResolveFrames : IAstStage
{
    public string Name => "sequence:frames";

    public ContentNode Run(ContentNode tree) =>
        MermaidNesting.Inside(tree, [SequenceKinds.Frame, SequenceKinds.Box], SequenceKinds.Ends,
                              [SequenceKinds.Participant, SequenceKinds.Created, SequenceKinds.Destroyed,
                               SequenceKinds.Message, SequenceKinds.Note, SequenceKinds.Activation,
                               SequenceKinds.Numbering, SequenceKinds.Section, SequenceKinds.Link, SequenceKinds.Menu],
                              SequenceKinds.Fact, SequenceRoles.Inside, SequenceRoles.Opened,
                              stray: "This end closes a box or a frame, and none is open here.",
                              unclosed: "Nothing closes this: a box and a frame are closed by end.");
}
