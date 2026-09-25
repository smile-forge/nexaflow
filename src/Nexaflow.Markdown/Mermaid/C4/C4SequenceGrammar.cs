using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Markdown.Mermaid.Sequence.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>
/// C4's macros written on a timeline — a <c>C4Sequence</c>, which is two languages in one block.
///
/// <para>
/// Every line that is not a macro is handed to <see cref="SequenceGrammar"/> itself rather than a second copy of it, which is
/// what lets <c>alt</c>, <c>loop</c>, <c>note over</c> and <c>activate</c> nest round the macros correctly. The two nestings
/// are one stack for the same reason: a <c>}</c> and an <c>end</c> each close whichever was opened last.
/// </para>
/// </summary>
public sealed class C4SequenceGrammar : C4Grammar
{
    private static readonly SequenceGrammar Sequences = new();

    /// <inheritdoc/>
    protected override IMermaidGrammar? Within => Sequences;

    /// <inheritdoc/>
    /// <remarks>
    /// The same stages a sequence diagram runs, with a boundary counted among the things that open a box and its <c>}</c>
    /// among the things that close one.
    /// </remarks>
    public override IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) =>
    [
        new ResolveFrames([C4Kinds.Boundary], [C4Kinds.Macro, C4Kinds.Aside], [C4Kinds.Ends]),
        new ResolveNumbers(SequenceConfig.Read(block.Config).Numbered),
    ];
}
