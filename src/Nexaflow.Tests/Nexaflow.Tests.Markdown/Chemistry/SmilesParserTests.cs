using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Chemistry;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Chemistry;

/// <summary>
/// The tree a <c>smiles</c> block is read into: what was written prints back as it was written, whatever it was, and
/// a molecule is read to the character.
///
/// <para>
/// The constructs include what nobody means to write, because a block is read on every keystroke and most of what it
/// is handed is half-written.
/// </para>
/// </summary>
[TestClass]
[CoversNode("smiles-block-ast")]
public class SmilesParserTests
{
    [TestMethod]
    public void EveryBlockReadsBackAsItWasWritten()
    {
        foreach (var (what, source) in SmilesConstructs.Blocks)
            Assert.AreEqual(source, SmilesParser.Parse(source).Print(), what);
    }

    [TestMethod]
    public void EveryMoleculeReadsBackAsItWasWritten()
    {
        foreach (var (what, smiles) in SmilesConstructs.Molecules)
            Assert.AreEqual(smiles, SmilesParser.Molecule(smiles).Print(), what);
    }

    [TestMethod]
    public void EveryPrefixReadsBackToo()
    {
        foreach (var (what, source) in SmilesConstructs.Blocks.Concat(SmilesConstructs.Molecules))
            for (var length = 0; length <= source.Length; length++)
            {
                var typed = source[..length];
                Assert.AreEqual(typed, SmilesParser.Parse(typed).Print(), $"{what}: after {length} character(s)");
            }
    }

    [TestMethod]
    public void TheParserOnlyEverCopies()
    {
        foreach (var (what, source) in SmilesConstructs.Blocks.Concat(SmilesConstructs.Molecules))
            foreach (var place in SmilesParser.Parse(source).Placed())
            {
                if (!place.Node.IsLeaf) continue;
                Assert.AreEqual(source.Substring(place.Start, place.Node.Width), place.Node.Text,
                                $"{what}: {place.Node.Kind} at {place.Start}");
            }
    }

    [TestMethod]
    public void AMoleculeIsReadToTheCharacter()
    {
        var molecule = SmilesParser.Molecule("CC(=O)O");

        CollectionAssert.AreEqual(
            new[] { SmilesKinds.Atom, SmilesKinds.Atom, SmilesKinds.Branch, SmilesKinds.Atom },
            molecule.Children.Select(child => child.Kind).ToArray());

        var branch = molecule.Children[2];
        CollectionAssert.AreEqual(
            new[] { Kinds.Token, SmilesKinds.Bond, SmilesKinds.Atom, Kinds.Token },
            branch.Children.Select(child => child.Kind).ToArray());
        Assert.AreEqual(Roles.Open, branch.Children[0].Role);
        Assert.AreEqual(Roles.Close, branch.Children[^1].Role);
    }

    [TestMethod]
    public void ABracketAtomIsItsParts()
    {
        var atom = SmilesParser.Molecule("[13CH3+:2]").Children.Single();

        var parts = atom.Children.Where(child => child.Kind != Kinds.Token).ToDictionary(child => child.Kind, child => child.Text);
        Assert.AreEqual("13", parts[SmilesKinds.Isotope]);
        Assert.AreEqual("C", parts[SmilesKinds.Symbol]);
        Assert.AreEqual("H3", parts[SmilesKinds.Hydrogens]);
        Assert.AreEqual("+", parts[SmilesKinds.Charge]);
        Assert.AreEqual(":2", parts[SmilesKinds.Class]);

        Assert.AreEqual("@@", SmilesParser.Molecule("N[C@@H](C)C").Children[1].Children
                                          .Single(child => child.Kind == SmilesKinds.Chirality).Text);
    }

    [TestMethod]
    public void TwoLetterSymbols_ReadAsOneAtom()
    {
        var atoms = SmilesParser.Molecule("ClCBr[Na+][se]").Children
            .Select(child => child.Children.Single(part => part.Kind == SmilesKinds.Symbol).Text)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Cl", "C", "Br", "Na", "se" }, atoms);
    }

    [TestMethod]
    public void ARingClosureIsItsBondAndItsNumber()
    {
        var rings = SmilesParser.Molecule("C=1CCCCC%10").SelfAndDescendants()
            .Where(node => node.Kind == SmilesKinds.RingBond)
            .Select(ring => string.Join("|", ring.Children.Select(child => child.Text)))
            .ToArray();

        CollectionAssert.AreEqual(new[] { "=|1", "%10" }, rings);
    }

    [TestMethod]
    public void AnEntryIsItsMoleculeAndItsCaption()
    {
        var block = SmilesParser.Parse("chemistry\n# a comment\nCCO \"Ethanol\"");
        var lines = block.Children;

        Assert.AreEqual(SmilesKinds.Header, lines[0].Children[0].Kind);
        Assert.AreEqual(Kinds.Comment, lines[1].Children[0].Kind);

        var entry = lines[2].Children.Single(child => child.Kind == SmilesKinds.Entry);
        Assert.AreEqual("CCO", entry.Part(SmilesRoles.Molecule)!.Print());
        Assert.AreEqual("Ethanol", entry.Part(SmilesRoles.Label)!.Part(SmilesRoles.Label)!.Text);
    }

    [TestMethod]
    public void TheKeywordOnlyNamesTheFormatBeforeAnyMolecule()
    {
        var late = SmilesParser.Parse("CCO\nchemistry");
        Assert.IsFalse(late.SelfAndDescendants().Any(node => node.Kind == SmilesKinds.Header));

        var twice = SmilesParser.Parse("chemistry\nchemistry");
        Assert.AreEqual(1, twice.SelfAndDescendants().Count(node => node.Kind == SmilesKinds.Header));
    }

    [TestMethod]
    public void WhatWillNotReadIsHeldWithTheReason()
    {
        foreach (var (smiles, reason) in new[]
                 {
                     ("CC(C", "never closed"),
                     ("CC(C))C", "never opened"),
                     ("CC()C", "empty"),
                     ("C[NH", "never closed"),
                     ("C[Xx]C", "not an element"),
                     ("CNaC", "brackets"),
                     ("CC=", "followed by an atom"),
                     ("=CC", "follow an atom"),
                     ("C%1CC", "two digits"),
                     ("C!C", "not part of SMILES"),
                 })
        {
            var troubles = SmilesParser.Molecule(smiles).SelfAndDescendants().Select(node => node.Trouble).OfType<string>().ToList();
            Assert.IsTrue(troubles.Any(trouble => trouble.Contains(reason)),
                          $"{smiles}: expected a reason saying '{reason}', got: {string.Join(" | ", troubles)}");
        }
    }

    [TestMethod]
    public void WellFormedMoleculesSayNothing()
    {
        foreach (var (what, smiles) in SmilesConstructs.Molecules.Take(21))
            Assert.IsFalse(SmilesParser.Molecule(smiles).SelfAndDescendants().Any(node => node.Trouble is not null), what);
    }
}
