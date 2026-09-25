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
/// written; a line half typed is read as far as it goes; every place something can be written takes any character, escaped
/// where it has to be; a new line starts in a shape the grammar reads; and a name renamed where it is declared is written so
/// its uses still read. <see cref="MermaidKitRulesTests"/> fails for a grammar with no tests deriving from this.
/// </para>
/// </summary>
public abstract class MermaidGrammarContract
{
    /// <summary>What anybody might type into a place: the characters a diagram's syntax gives a meaning to, and space.</summary>
    private const string Typed = "\"\\ []() {}%,:;#|->";

    /// <summary>The diagram whose grammar this is.</summary>
    public abstract MermaidDiagram Diagram { get; }

    /// <summary>
    /// Every construct the grammar reads, and what nobody means to write — a line half typed, a quote never closed, a number
    /// that is no number. The record of what the grammar reads, and what a new construct is added to.
    /// </summary>
    protected abstract IEnumerable<(string What, string Source)> Blocks { get; }

    /// <summary>The blocks Mermaid's own documentation shows for the diagram, which read with nothing wrong with them.</summary>
    protected abstract IEnumerable<string> DocumentedBlocks { get; }

    /// <summary>
    /// The grammar a block is read by, where its fence's language names it rather than its first line — a nomnoml block,
    /// say. Null for a Mermaid diagram, whose header names its own.
    /// </summary>
    protected virtual IMermaidGrammar? Named => null;

    /// <summary>The grammar under test.</summary>
    protected IMermaidGrammar Grammar => Named ?? MermaidDiagrams.Grammar(Diagram)
                                         ?? throw new AssertFailedException($"{Diagram} has no grammar: MermaidDiagrams.Grammar names none.");

    /// <summary>A block parsed as this grammar reads it.</summary>
    private ContentNode Parsed(string source) => MermaidParser.Parse(source, Named);

    /// <summary>A block parsed and run through its stages, as this grammar reads it.</summary>
    private ContentNode Reading(string source, bool holes = false) => MermaidStaged.Read(source, holes, Named);

    [TestMethod]
    public void EveryBlockIsTheDiagramItsHeaderNames()
    {
        Assert.IsTrue(DocumentedBlocks.Any(), "a grammar is checked against at least one block its documentation shows");

        // A block read by the language its fence names has no header to name anything, so there is nothing here to hold.
        if (Named is not null) return;

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

    [TestMethod]
    public void WhateverIsTypedWhereSomethingIsWrittenTheLineStillReads()
    {
        foreach (var (what, source) in All)
        {
            var root = ContentReading.Of(Reading(source, holes: true)).Root;
            var held = Held(source);

            foreach (var place in root.SelfAndDescendants().Where(part => part.Kind is MermaidKinds.Words or Kinds.Hole))
                foreach (var character in Typed)
                {
                    var text = character.ToString();
                    var writing = Grammar.Escaping(place, place.End, text) ?? new MermaidWriting(place.End, place.End, text, place.End + 1);
                    var written = source[..writing.Start] + writing.Text + source[writing.End..];

                    Assert.IsTrue(writing.Caret >= writing.Start && writing.Caret <= writing.Start + writing.Text.Length,
                                  $"{what}: typing {text} into '{place.Text}' puts the caret in what was written");
                    Assert.IsTrue(Held(written) <= held,
                                  $"{what}: typing {text} into '{place.Text}' writes\n{written}\nwhich no longer reads — escape it (IMermaidGrammar.Escaping)");
                }
        }
    }

    [TestMethod]
    public void EveryNewLineIsOneTheGrammarReads()
    {
        foreach (var (what, source) in All)
        {
            if (Named is null && MermaidBlock.Read(source).Diagram != Diagram) continue;

            var reading = Parsed(source);
            var said = reading.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line).Select(line => line.Stated()).Prepend(null);

            foreach (var above in said)
            {
                if (Grammar.Blank(above) is not var (text, caret)) continue;

                Assert.IsTrue(caret >= 0 && caret <= text.Length, $"{what}: the caret in a new line under {above?.Kind ?? "nothing"} is in it");

                var written = source.TrimEnd() + "\n" + text;
                var line = Parsed(written).SelfAndDescendants().Last(node => node.Kind == MermaidKinds.Line).Stated();

                Assert.IsNotNull(line, $"{what}: a new line under {above?.Kind ?? "nothing"} says something");
                Assert.AreNotEqual(Kinds.Verbatim, line!.Kind, $"{what}: a new line under {above?.Kind ?? "nothing"} — '{text}' — is one the grammar reads");
            }
        }
    }

    [TestMethod]
    public void ANameRenamedIsWrittenSoEveryUseOfItStillReads()
    {
        foreach (var (what, source) in All)
        {
            var root = ContentReading.Of(Reading(source)).Root;
            var held = Held(source);

            foreach (var name in Grammar.Names(root))
            {
                Assert.AreEqual(name.Name, name.Declared.Words()?.Text ?? string.Empty, $"{what}: {name.Name} is declared as itself");
                foreach (var use in name.Uses)
                    Assert.AreEqual(name.Name, use.Words()?.Text ?? string.Empty, $"{what}: {name.Name} is used as itself");

                foreach (var renamed in new[] { "renamed", "two words", "with \"quote\"", "9lives" })
                {
                    var naming = Grammar.Naming(renamed.Replace("\"", "#quot;", StringComparison.Ordinal));
                    var written = new[] { name.Declared }.Concat(name.Uses).OrderByDescending(part => part.Start)
                        .Aggregate(source, (text, part) => text[..part.Start] + naming + text[part.End..]);

                    Assert.IsTrue(Held(written) <= held, $"{what}: {name.Name} renamed {renamed} writes\n{written}\nwhich no longer reads");
                }
            }
        }
    }

    /// <summary>The blocks, and the documented ones.</summary>
    private IEnumerable<(string What, string Source)> All => Blocks.Concat(DocumentedBlocks.Select(source => ("documented", source)));

    /// <summary>How many lines of a block the grammar holds as written, rather than reading them.</summary>
    private int Held(string source) =>
        Parsed(source).SelfAndDescendants().Count(node => node is { Kind: Kinds.Verbatim, Trouble: not null });
}
