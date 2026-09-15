using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Chemistry;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Chemistry;

/// <summary>
/// What a SMILES string amounts to: which atom each ring closure reaches, how many hydrogens each atom carries,
/// where an aromatic ring's double bonds go — and where the string asks for something no molecule can be.
///
/// <para>
/// The hydrogen counts are the ones RDKit gives. The corpus sweep holds the stages to RDKit over five thousand real
/// molecules; this is the hand-checked handful that always runs.
/// </para>
/// </summary>
[TestClass]
[CoversNode("smiles-chemistry")]
public class SmilesPipelineTests
{
    [TestMethod]
    public void EveryStageLeavesTheSourceAlone()
    {
        foreach (var (what, source) in SmilesConstructs.Blocks.Concat(SmilesConstructs.Molecules))
        {
            var tree = SmilesParser.Parse(source);
            foreach (var stage in SmilesPipeline.Of().Stages)
            {
                tree = stage.Run(tree);
                Assert.AreEqual(source, tree.Print(), $"{what}: {stage.Name} changed the source");
            }
        }
    }

    [TestMethod]
    public void HydrogensAreFilledInUpToTheElementsValence()
    {
        foreach (var (smiles, hydrogens) in new[]
                 {
                     ("C", "4"),
                     ("CCO", "3,2,1"),
                     ("C=C", "2,2"),
                     ("C#N", "1,0"),
                     ("O=C=O", "0,0,0"),
                     ("c1ccccc1", "1,1,1,1,1,1"),
                     ("c1ccc2ccccc2c1", "1,1,1,0,1,1,1,1,0,1"),
                     ("c1cc[nH]c1", "1,1,1,1,1"),
                     ("c1ccncc1", "1,1,1,0,1,1"),
                     ("O=c1cc[nH]cc1", "0,0,1,1,1,1,1"),
                     ("CS(=O)(=O)C", "3,0,0,0,3"),
                     ("CP(C)C", "3,0,3,3"),
                     ("[NH4+]", "4"),
                     ("[O-]C", "0,3"),
                     ("CN(=O)=O", "3,0,0,0"),
                     ("ClCBr", "0,2,0"),
                     ("*C", "0,3"),
                 })
        {
            var molecule = Read(smiles);
            Assert.AreEqual(hydrogens, string.Join(",", molecule.Atoms.Select(atom => atom.TotalHydrogens)), smiles);
        }
    }

    [TestMethod]
    public void ARingClosureIsABondToTheAtomThatClosesIt()
    {
        var cyclohexane = Read("C1CCCCC1");
        Assert.AreEqual(6, cyclohexane.Bonds.Count);
        Assert.IsNotNull(cyclohexane.Between(0, 5));

        var twoRings = Read("C1CC1C1CC1");
        Assert.IsNotNull(twoRings.Between(0, 2));
        Assert.IsNotNull(twoRings.Between(3, 5));
        Assert.AreEqual(7, twoRings.Bonds.Count);

        var written = Read("C=1CCCCC1");
        Assert.AreEqual(BondOrder.Double, written.Between(0, 5)!.Order);
    }

    [TestMethod]
    public void AromaticRingsGetAlternatingDoubleBonds()
    {
        foreach (var (smiles, doubles) in new[]
                 {
                     ("c1ccccc1", 3),
                     ("c1ccc2ccccc2c1", 5),
                     ("c1cc[nH]c1", 2),
                     ("c1ccoc1", 2),
                     ("c1ccncc1", 3),
                     ("Cn1cnc2c1c(=O)n(C)c(=O)n2C", 4),
                 })
        {
            var molecule = Read(smiles);
            Assert.AreEqual(doubles, molecule.Bonds.Count(bond => molecule.Drawn(bond) == BondOrder.Double), smiles);

            // And every atom that needed one got exactly one.
            foreach (var atom in molecule.Atoms.Where(atom => atom.DoubleTo is not null))
                Assert.AreEqual(atom.Index, molecule.Atoms[atom.DoubleTo!.Value].DoubleTo, $"{smiles}: atom {atom.Index}");
        }
    }

    [TestMethod]
    public void TheBondBetweenTwoRingsIsNotPartOfEitherRingsAlternation()
    {
        var biphenyl = Read("c1ccccc1c1ccccc1");
        Assert.AreEqual(BondOrder.Single, biphenyl.Drawn(biphenyl.Between(5, 6)!));
        Assert.AreEqual(6, biphenyl.Bonds.Count(bond => biphenyl.Drawn(bond) == BondOrder.Double));
    }

    [TestMethod]
    public void WhatNoMoleculeCanBe_IsSaidWhereItWasWritten()
    {
        foreach (var (smiles, at, reason) in new (string, string?, string)[]
                 {
                     ("C1CCC", "1", "never closed"),
                     ("C11", "1", "atom that opened it"),
                     ("C1C1", "1", "already bonded"),
                     ("C=1CCCC#1", "#1", "different bonds"),
                     ("C(C)(C)(C)(C)C", "C", "cannot make five bonds"),
                     ("CN(C)(C)C", "N", "[N+]"),
                     ("c1cccc1", "c", "alternating double bonds"),
                     ("c1ccnc1", null, "[nH]"),
                     ("cc", "c", "has to be in a ring"),
                     ("[OH3]", "[OH3]", "cannot make three bonds"),
                 })
        {
            var troubled = SmilesPipeline.Of().Run(SmilesParser.Molecule(smiles))
                .SelfAndDescendants()
                .Where(node => node.Trouble is not null && node.Trouble.Contains(reason))
                .ToList();

            Assert.IsTrue(troubled.Count > 0, $"{smiles}: nothing said '{reason}'");
            Assert.IsTrue(at is null || troubled.Any(node => node.Print() == at), $"{smiles}: '{reason}' was said of {string.Join(", ", troubled.Select(n => n.Print()))}, not {at}");
        }
    }

    [TestMethod]
    public void AWellFormedMoleculeSaysNothing()
    {
        foreach (var smiles in new[] { "CC(=O)Oc1ccccc1C(=O)O", "Cn1cnc2c1c(=O)n(C)c(=O)n2C", "[NH4+].[Cl-]", "CN(=O)=O", "OC[C@H]1OC(O)[C@H](O)[C@@H](O)[C@@H]1O" })
            Assert.IsFalse(SmilesPipeline.Read(smiles).SelfAndDescendants().Any(node => node.Trouble is not null), smiles);
    }

    [TestMethod]
    public void HandednessIsReadAgainstTheOrderTheNeighboursWereWritten()
    {
        var alanine = Read("N[C@@H](C)C(=O)O");
        CollectionAssert.AreEqual(new[] { 0, 2, 3 }, alanine.NeighboursAsWritten(1).ToArray());
        Assert.IsTrue(alanine.Preceded(1));
        Assert.IsFalse(alanine.Preceded(0));

        // A ring closure stands where its digit was written, before what follows the atom.
        var ring = Read("[C@H]1(F)CCC1");
        CollectionAssert.AreEqual(new[] { 4, 1, 2 }, ring.NeighboursAsWritten(0).ToArray());
    }

    private static Molecule Read(string smiles) => Molecule.Read(SmilesPipeline.Of().Run(SmilesParser.Molecule(smiles)));
}
