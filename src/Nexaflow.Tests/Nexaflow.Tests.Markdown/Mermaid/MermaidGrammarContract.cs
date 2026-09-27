using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// What every diagram's grammar has to keep, checked the same way for each. A diagram's grammar tests derive from this and
/// hand it the blocks that are the record of what the grammar reads; every check here then runs over them, and the tests
/// the class adds itself are only about what that diagram says.
///
/// <para>
/// The checks are the ones a reader writing in a diagram relies on: whatever is typed, the block still prints as what was
/// written; and a line half typed is read as far as it goes. What typing into a place writes is the diagram's edit handler's,
/// and is checked where that is. <see cref="MermaidKitRulesTests"/> fails for a grammar with no tests deriving from this.
/// </para>
/// </summary>
public abstract class MermaidGrammarContract
{
    /// <summary>The diagram whose grammar this is.</summary>
    public abstract MermaidDiagram Diagram { get; }

    /// <summary>
    /// Every construct the grammar reads, and what nobody means to write — a line half typed, a quote never closed, a number
    /// that is no number. The record of what the grammar reads, and what a new construct is added to.
    /// </summary>
    protected abstract IEnumerable<(string What, string Source)> Blocks { get; }

    /// <summary>The blocks Mermaid's own documentation shows for the diagram, which read with nothing wrong with them.</summary>
    protected abstract IEnumerable<string> DocumentedBlocks { get; }

    /// <summary>The grammar under test.</summary>
    protected IMermaidGrammar Grammar => MermaidDiagrams.Grammar(Diagram)
                                         ?? throw new AssertFailedException($"{Diagram} has no grammar: MermaidDiagrams.Grammar names none.");

    /// <summary>A block parsed as this grammar reads it.</summary>
    private ContentNode Parsed(string source) => MermaidParser.Parse(source);

    /// <summary>A block parsed and run through its stages, as this grammar reads it.</summary>
    private ContentNode Reading(string source, bool holes = false) => MermaidStaged.Read(source, holes);

    [TestMethod]
    public void EveryBlockIsTheDiagramItsHeaderNames()
    {
        Assert.IsTrue(DocumentedBlocks.Any(), "a grammar is checked against at least one block its documentation shows");


        foreach (var source in DocumentedBlocks)
            Assert.AreEqual(Diagram, MermaidBlock.Read(source).Diagram, source);
    }

    [TestMethod]
    public void EveryBlockReadsBackAsItWasWritten()
    {
        foreach (var (what, source) in All)
        {
            Assert.AreEqual(source, Parsed(source).Print(), what);
            Assert.AreEqual(source, Reading(source).Print(), $"{what}, after the stages");
            Assert.AreEqual(source, Reading(source, holes: true).Print(), $"{what}, with holes");
        }
    }

    [TestMethod]
    public void EveryPrefixOfEveryBlockReadsBackToo()
    {
        foreach (var (what, source) in All)
            for (var length = 0; length <= source.Length; length++)
            {
                var typed = source[..length];
                Assert.AreEqual(typed, Reading(typed, holes: true).Print(), $"{what}: after {length} character(s)");
            }
    }

    [TestMethod]
    public void TheGrammarOnlyEverCopies()
    {
        foreach (var (what, source) in All)
            foreach (var place in Parsed(source).Placed())
            {
                if (!place.Node.IsLeaf) continue;
                Assert.AreEqual(source.Substring(place.Start, place.Node.Width), place.Node.Text, $"{what}: {place.Node.Kind} at {place.Start}");
            }
    }

    [TestMethod]
    public void TheDocumentedBlocksHaveNothingWrongWithThem()
    {
        foreach (var source in DocumentedBlocks)
        {
            var trouble = Reading(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>().ToList();
            Assert.AreEqual(0, trouble.Count, $"{source}\n{string.Join("\n", trouble)}");
        }
    }

    /// <summary>The blocks, and the documented ones.</summary>
    private IEnumerable<(string What, string Source)> All => Blocks.Concat(DocumentedBlocks.Select(source => ("documented", source)));
}
