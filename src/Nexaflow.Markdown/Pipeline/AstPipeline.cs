using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Pipeline;

/// <summary>
/// Reading a piece of content, in stages: the parser makes a tree of what was written, and each stage
/// after it takes a tree and returns a tree.
///
/// <para>
/// <strong>The pipeline is the architecture, not the stages in it.</strong> Which actors a language runs,
/// and in what order, is that language's business and changes as it learns to read more; what is fixed is
/// that they are actors, that they compose, and that each one leaves the source alone. Writing the order
/// out longhand inside one method — as the first version of this did — reads as economy and is really a
/// missing seam: it puts every language's stages in one file, gives no name to what a stage is, and gives
/// nothing to hang the invariant off.
/// </para>
/// <para>
/// So the invariant lives here, checked on every run in a debug build and asserted over the corpus in
/// tests: <c>stage.Run(t).Print() == t.Print()</c>. A stage that breaks it is named in the failure, which
/// is the difference between a bug found where it was written and a round-trip test failing somewhere
/// downstream a week later.
/// </para>
/// <para>
/// A parser is not a stage. What a name is shorthand for, and where one token stops and the next begins,
/// are facts about the text that no later stage can change — so they happen as the text is read. Every
/// stage here is about what the text <em>amounts to</em>.
/// </para>
/// </summary>
public sealed class AstPipeline
{
    private readonly IAstStage[] _stages;

    public AstPipeline(params IAstStage[] stages) => this._stages = stages;

    public AstPipeline(IEnumerable<IAstStage> stages) => this._stages = [.. stages];

    /// <summary>The stages, in the order they run.</summary>
    public IReadOnlyList<IAstStage> Stages => this._stages;

    /// <summary>The same pipeline with more actors on the end.</summary>
    public AstPipeline Then(params IAstStage[] stages) => new([.. this._stages, .. stages]);

    /// <summary>
    /// The same pipeline with <paramref name="stage"/> on the end, or unchanged when there is nothing to
    /// add. For the stages a caller asks for conditionally — a hole only where somebody is writing, a
    /// verbatim stretch only where a caret is in one.
    /// </summary>
    public AstPipeline Then(IAstStage? stage) => stage is null ? this : this.Then([stage]);

    /// <summary>
    /// Runs every stage, in order, and hands back what they made of the tree.
    ///
    /// <para>
    /// The source is checked between stages in a debug build only. It is a whole-tree print per stage,
    /// which is exactly the cost a release build should not pay on every keystroke — and the corpus sweep
    /// makes the same check over a quarter of a million real inputs, where a defect would show up long
    /// before a reader met it.
    /// </para>
    /// </summary>
    public ContentNode Run(ContentNode tree)
    {
#if DEBUG
        var source = tree.Print();
#endif
        foreach (var stage in this._stages)
        {
            tree = stage.Run(tree);
#if DEBUG
            var printed = tree.Print();
            if (printed != source)
                throw new AstStageException(stage.Name, source, printed);
#endif
        }

        return tree;
    }
}

/// <summary>
/// A stage changed what the tree prints as, which no stage may do.
/// <para>
/// Thrown rather than reported, and only in a debug build. It is not a fault a reader can cause or an
/// author can fix — it is a stage that is wrong, and the sooner it stops the shorter the walk back to it.
/// </para>
/// </summary>
public sealed class AstStageException(string stage, string before, string after)
    : Exception($"The stage '{stage}' changed the source. It must not.\n  before: {Show(before)}\n  after:  {Show(after)}")
{
    /// <summary>The stage that broke the rule.</summary>
    public string Stage { get; } = stage;

    /// <summary>What the tree printed as before it ran.</summary>
    public string Before { get; } = before;

    /// <summary>What it printed as after.</summary>
    public string After { get; } = after;

    private static string Show(string text) =>
        text.Length <= 120 ? text : text[..120] + "…";
}
