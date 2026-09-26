using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Sequence.Stages;

/// <summary>
/// Gathers each box and frame with the lines written in it, so the tree says how deep a message is drawn and what is boxed round
/// it. That depends on every <c>alt</c>, <c>box</c> and <c>end</c> written above it, and so is a fact about the whole block
/// rather than about any one line (<see cref="MermaidNesting.Nest"/>).
///
/// <para>
/// A box and a frame are both closed by <c>end</c>, so the two are one nesting rather than two: an <c>end</c> closes whichever
/// was opened last, which is what Mermaid's own parser does with one stack. A C4 sequence adds its boundaries to the same
/// stack, since a <c>}</c> closes whichever was opened last just as an <c>end</c> does.
/// </para>
/// </summary>
/// <param name="opens">What else opens a box, beyond a sequence diagram's own.</param>
/// <param name="ends">And what else closes one, beyond <c>end</c>.</param>
public sealed class ResolveFrames(IReadOnlyList<string>? opens = null, IReadOnlyList<string>? ends = null) : IAstStage
{
    public string Name => "sequence:frames";

    public ContentNode Run(ContentNode tree) =>
        MermaidNesting.Nest(tree, [SequenceKinds.Frame, SequenceKinds.Box, .. opens ?? []], [SequenceKinds.Ends, .. ends ?? []],
                            stray: "This end closes a box or a frame, and none is open here.",
                            unclosed: "Nothing closes this: a box and a frame are closed by end.");
}
