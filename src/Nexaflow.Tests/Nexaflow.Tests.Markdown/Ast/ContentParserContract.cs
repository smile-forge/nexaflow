using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Tests.Markdown.Ast;

/// <summary>
/// What every language's reading has to keep, checked the same way for each: whatever is typed, what its parser reads prints as
/// exactly what was written, the parser only ever copies, and every piece says where it was read from — and the same through
/// every stage the language offers the engine, half-typed blocks with their holes included. A language's parser tests derive
/// from this and hand it the blocks that are the record of what it reads; every check here then runs over them, and the tests
/// the class adds itself are only about what that language says.
///
/// <para>
/// The stages are the language entry's to offer, and the engine's to run; a test names them again (<see cref="Stages"/>) so they
/// are held here without a WPF reader, with no language table behind them. A test naming none holds its parser alone.
/// </para>
/// </summary>
public abstract class ContentParserContract
{
    /// <summary>A block read by the language's parser.</summary>
    protected abstract ContentNode Parse(string? source);

    /// <summary>
    /// The stages the language offers the engine for <paramref name="tree"/>, named as its language entry names them — none, where a
    /// test holds the parser alone.
    /// </summary>
    /// <param name="holes">Whether somebody is writing in the block.</param>
    protected virtual IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) => [];

    /// <summary>
    /// Every construct the parser reads, and what nobody means to write — a line half typed, a quote never closed, a number that
    /// is no number. The record of what the parser reads, and what a new construct is added to.
    /// </summary>
    protected abstract IEnumerable<(string What, string Source)> Blocks { get; }

    /// <summary>The blocks the language's own documentation shows, which read with nothing wrong with them.</summary>
    protected abstract IEnumerable<string> DocumentedBlocks { get; }

    /// <summary>A block read and worked over by <see cref="Stages"/>.</summary>
    protected ContentNode Worked(string? source, bool holes = false)
    {
        var tree = Parse(source);
        return new AstPipeline(Stages(tree, holes)).Run(tree);
    }

    [TestMethod]
    public void EveryBlockReadsBackAsItWasWritten()
    {
        foreach (var (what, source) in All)
        {
            Assert.AreEqual(source, Parse(source).Print(), what);
            Assert.AreEqual(source, Worked(source).Print(), $"{what}, after the stages");
            Assert.AreEqual(source, Worked(source, holes: true).Print(), $"{what}, with holes");
        }
    }

    [TestMethod]
    public void AndSoDoesEveryStageOnTheWayThere()
    {
        // The rule the whole design rests on, stated per stage rather than per pipeline: a stage may throw out and
        // rebuild every node it was handed, provided what comes back still prints as the source it was read from.
        // Asserting it only once every stage has run lets two stages cancel out one another's damage.
        foreach (var (what, source) in All)
            foreach (var holes in new[] { false, true })
            {
                var tree = Parse(source);

                foreach (var stage in Stages(tree, holes))
                {
                    tree = stage.Run(tree);
                    Assert.AreEqual(source, tree.Print(), $"{what}: after {stage.Name}{(holes ? ", with holes" : "")}");
                }
            }
    }

    [TestMethod]
    public void EveryPrefixOfEveryBlockReadsBackToo()
    {
        foreach (var (what, source) in All)
            for (var length = 0; length <= source.Length; length++)
            {
                var typed = source[..length];
                Assert.AreEqual(typed, Parse(typed).Print(), $"{what}: after {length} character(s)");
                Assert.AreEqual(typed, Worked(typed, holes: true).Print(), $"{what}: after {length} character(s), worked over");
            }
    }

    [TestMethod]
    public void TheParserOnlyEverCopies()
    {
        foreach (var (what, source) in All)
            foreach (var place in Parse(source).Placed())
            {
                if (!place.Node.IsLeaf) continue;
                Assert.AreEqual(source.Substring(place.Start, place.Node.Width), place.Node.Text, $"{what}: {place.Node.Kind} at {place.Start}");
            }
    }

    [TestMethod]
    public void EveryPieceSaysWhereItWasRead()
    {
        foreach (var (what, source) in All)
            foreach (var (stage, tree) in new[] { ("read", Parse(source)), ("after the stages", Worked(source)) })
            {
                var faults = AstOracle.Faults(source, tree).ToList();
                Assert.AreEqual(0, faults.Count, $"{what}, {stage}\n{string.Join("\n", faults)}");
            }
    }

    [TestMethod]
    public void AReversedTreeStillPrintsWhatWasWritten()
    {
        foreach (var (what, source) in All)
            Assert.AreEqual(source, AstOracle.Reversed(Parse(source)).Print(), what);
    }

    [TestMethod]
    public void TheDocumentedBlocksHaveNothingWrongWithThem()
    {
        Assert.IsTrue(DocumentedBlocks.Any(), "a language is checked against at least one block its documentation shows");

        foreach (var source in DocumentedBlocks)
        {
            var trouble = Worked(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>().ToList();
            Assert.AreEqual(0, trouble.Count, $"{source}\n{string.Join("\n", trouble)}");
        }
    }

    /// <summary>The blocks, and the documented ones.</summary>
    protected IEnumerable<(string What, string Source)> All => Blocks.Concat(DocumentedBlocks.Select(source => ("documented", source)));
}
