using Nexaflow.Markdown.Chemistry;
using Nexaflow.Markdown.Chemistry.Depiction;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Chemistry;

/// <summary>
/// Where a molecule's atoms go: bonds one length, chains zig-zagging at a hundred and twenty degrees, rings regular,
/// triple bonds straight, nothing on top of anything, a double bond's substituents on the sides the string asked for,
/// and a wedge that means the handedness written.
/// </summary>
[TestClass]
[CoversNode("smiles-structure-layout")]
public class StructureLayoutTests
{
    private static readonly string[] Everyday =
    [
        "CCO", "CC(=O)O", "CCCCCCCC", "CC(C)(C)C", "c1ccccc1", "c1ccc2ccccc2c1", "c1ccc2cc3ccccc3cc2c1",
        "CC(=O)Oc1ccccc1C(=O)O", "Cn1cnc2c1c(=O)n(C)c(=O)n2C", "OC[C@H]1OC(O)[C@H](O)[C@@H](O)[C@@H]1O",
        "C1CCC2(CC1)CCCC2", "C1CC2CCC1C2", "CC(C)Cc1ccc(cc1)C(C)C(=O)O", "c1ccccc1c1ccccc1",
        "N[C@@H](Cc1ccccc1)C(=O)O", "CCN(CC)CC", "O=C(O)CC(O)(CC(=O)O)C(=O)O", "C1CCCCCCCCCCC1",
    ];

    [TestMethod]
    public void EveryBondIsOneLong()
    {
        foreach (var smiles in Everyday)
        {
            var (molecule, structure) = Lay(smiles);
            foreach (var bond in molecule.Bonds)
            {
                var length = Vec.Distance(structure.At[bond.From], structure.At[bond.To]);
                Assert.IsTrue(Math.Abs(length - 1) < 0.08, $"{smiles}: bond {bond.From}-{bond.To} is {length:F3} long");
            }
        }
    }

    [TestMethod]
    public void NothingLandsOnAnythingElse()
    {
        foreach (var smiles in Everyday)
        {
            var (molecule, structure) = Lay(smiles);
            for (var i = 0; i < structure.At.Count; i++)
                for (var j = i + 1; j < structure.At.Count; j++)
                {
                    if (molecule.Between(i, j) is not null) continue;
                    var apart = Vec.Distance(structure.At[i], structure.At[j]);
                    Assert.IsTrue(apart > 0.6, $"{smiles}: atoms {i} and {j} are {apart:F3} apart");
                }
        }
    }

    [TestMethod]
    public void AChainZigZags()
    {
        var (_, structure) = Lay("CCCCCC");
        for (var i = 1; i < 5; i++)
            Assert.AreEqual(120, Angle(structure, i - 1, i, i + 1), 1, $"the angle at atom {i}");

        // Trans all the way: each atom is on the far side of the bond before it from the one before that.
        for (var i = 2; i < 5; i++)
            Assert.AreNotEqual(Side(structure, i - 1, i, i - 2), Side(structure, i - 1, i, i + 1), $"at atom {i}");
    }

    [TestMethod]
    public void ARingIsRegular()
    {
        var (_, structure) = Lay("c1ccccc1");
        for (var i = 0; i < 6; i++)
            Assert.AreEqual(120, Angle(structure, (i + 5) % 6, i, (i + 1) % 6), 0.5, $"the angle at atom {i}");
    }

    [TestMethod]
    public void ATripleBondIsStraight()
    {
        var (_, structure) = Lay("CC#CC");
        Assert.AreEqual(180, Angle(structure, 0, 1, 2), 0.5);
        Assert.AreEqual(180, Angle(structure, 1, 2, 3), 0.5);
    }

    [TestMethod]
    public void ADoubleBondsSubstituentsGoOnTheSidesWritten()
    {
        var (_, trans) = Lay("C/C=C/C");
        Assert.AreNotEqual(Side(trans, 1, 2, 0), Side(trans, 1, 2, 3), "C/C=C/C is trans");

        var (_, cis) = Lay("C/C=C\\C");
        Assert.AreEqual(Side(cis, 1, 2, 0), Side(cis, 1, 2, 3), "C/C=C\\C is cis");

        var (_, branched) = Lay("C(\\F)=C/F");
        Assert.AreNotEqual(Side(branched, 0, 2, 1), Side(branched, 0, 2, 3), "C(\\F)=C/F is F/C=C/F, trans");
    }

    [TestMethod]
    public void MirrorImagesGetOppositeWedgesOnTheSameBond()
    {
        var (left, one) = Lay("N[C@@H](C)C(=O)O");
        var (_, other) = Lay("N[C@H](C)C(=O)O");

        var wedged = Enumerable.Range(0, left.Bonds.Count).Where(i => one.Wedges[i].Kind != Wedge.None).ToList();
        Assert.AreEqual(1, wedged.Count, "one wedge for one stereocentre");

        var bond = wedged[0];
        Assert.AreEqual(1, one.Wedges[bond].Narrow, "the narrow end is the stereocentre");
        Assert.AreNotEqual(one.Wedges[bond].Kind, other.Wedges[bond].Kind);
        Assert.AreNotEqual(Wedge.None, other.Wedges[bond].Kind);
    }

    [TestMethod]
    public void TwoMoleculesInOneStringStandSideBySide()
    {
        var (molecule, structure) = Lay("CCO.c1ccccc1");
        var rightOfEthanol = Enumerable.Range(0, 3).Max(i => structure.At[i].X);
        var leftOfBenzene = Enumerable.Range(3, 6).Min(i => structure.At[i].X);

        Assert.AreEqual(9, molecule.Atoms.Count);
        Assert.IsTrue(leftOfBenzene - rightOfEthanol >= 1.4, $"only {leftOfBenzene - rightOfEthanol:F2} apart");
    }

    [TestMethod]
    public void TheSmallestRingsAreTheRings()
    {
        var (_, naphthalene) = Lay("c1ccc2ccccc2c1");
        CollectionAssert.AreEqual(new[] { 6, 6 }, naphthalene.Rings.Select(ring => ring.Length).ToArray());

        var (_, norbornane) = Lay("C1CC2CCC1C2");
        CollectionAssert.AreEquivalent(new[] { 5, 5 }, norbornane.Rings.Select(ring => ring.Length).ToArray());
    }

    [TestMethod]
    public void ACageIsDrawnAsTheSolidItIs_AndABicycleThatDrawsFlatStaysFlat()
    {
        foreach (var smiles in new[] { "C1C2CC3CC1CC(C2)C3", "C12C3C4C1C5C2C3C45", "C1N2CN3CN1CN(C2)C3", "O=P12OP3(=O)OP(=O)(O1)OP(=O)(O2)O3" })
        {
            var (molecule, structure) = Lay(smiles);
            var cage = MoleculeRings.RingBonds(molecule);
            foreach (var bond in molecule.Bonds.Where(bond => cage[bond.Index]))
                Assert.IsTrue(structure.Depth[bond.From] is not null && structure.Depth[bond.To] is not null, $"{smiles}: bond {bond.From}-{bond.To} is on the flat page");
        }

        foreach (var smiles in new[] { "C1CC2CCC1C2", "c1ccc2ccccc2c1", "C1CCC2(CC1)CCCC2" })
            Assert.IsTrue(Lay(smiles).Structure.Depth.All(depth => depth is null), $"{smiles} reads as it is flat");
    }

    [TestMethod]
    public void NothingInADrawnCageLandsOnAnythingElse()
    {
        foreach (var smiles in new[] { "C1C2CC3CC1CC(C2)C3", "C12C3C4C1C5C2C3C45", "C1N2CN3CN1CN(C2)C3", "O=P12OP3(=O)OP(=O)(O1)OP(=O)(O2)O3",
                                       "P12OP3OP(O1)OP(O2)O3", "CC12CC3CC(C)(C1)CC(N)(C3)C2", "CN1C2CCC1CCC2", "C1CN2CCN1CC2" })
        {
            var (molecule, structure) = Lay(smiles);
            for (var i = 0; i < structure.At.Count; i++)
                for (var j = i + 1; j < structure.At.Count; j++)
                {
                    if (molecule.Between(i, j) is not null) continue;
                    var apart = Vec.Distance(structure.At[i], structure.At[j]);
                    Assert.IsTrue(apart > 0.45, $"{smiles}: atoms {i} and {j} are {apart:F3} apart");
                }
        }
    }

    [TestMethod]
    public void ASubstituentLeavesACageOnTheOutside()
    {
        // Memantine's amine and methyls hang off three bridgeheads; each has to be further from the cage's middle than
        // the atom it hangs from.
        var (molecule, structure) = Lay("CC12CC3CC(C)(C1)CC(N)(C3)C2");
        var cage = Enumerable.Range(0, molecule.Atoms.Count).Where(i => structure.Depth[i] is not null).ToList();
        var middle = cage.Aggregate(Vec.Zero, (sum, i) => sum + structure.At[i]) / cage.Count;

        foreach (var outside in Enumerable.Range(0, molecule.Atoms.Count).Where(i => structure.Depth[i] is null))
        {
            var bridgehead = molecule.BondsAt(outside).Select(b => molecule.Bonds[b].Other(outside)).Single();
            Assert.IsTrue(Vec.Distance(structure.At[outside], middle) > Vec.Distance(structure.At[bridgehead], middle),
                          $"atom {outside} points into the cage");
        }
    }

    [TestMethod]
    public void ACageIsFramedTheWayTheTextbookFramesIt()
    {
        // Wikipedia's adamantane and hexamine, which read as solids where other framings of the same view read inside
        // out: a family of parallel bonds standing straight up the page, their tops nearer the reader, the nearest of them
        // left of the middle, and the topmost atom a peak with everything it bonds to below it.
        foreach (var smiles in new[] { "C1C2CC3CC1CC(C2)C3", "C1N2CN3CN1CN(C2)C3", "C12C3C4C1C5C2C3C45" })
        {
            var (molecule, structure) = Lay(smiles);
            var at = structure.At;
            var depth = structure.Depth;

            var uprights = molecule.Bonds
                .Where(bond => depth[bond.From] is not null && depth[bond.To] is not null)
                .Where(bond => Math.Abs((at[bond.To] - at[bond.From]).Unit.Y) > 0.97)
                .ToList();

            Assert.IsTrue(uprights.Count >= 3, $"{smiles}: {uprights.Count} upright bond(s)");

            foreach (var bond in uprights)
            {
                var (bottom, top) = at[bond.To].Y > at[bond.From].Y ? (bond.From, bond.To) : (bond.To, bond.From);
                Assert.IsTrue(depth[top] > depth[bottom], $"{smiles}: upright {bottom}-{top} leans away, so it is seen from below");
            }

            var front = uprights.OrderByDescending(bond => depth[bond.From]!.Value + depth[bond.To]!.Value).First();
            var middle = Enumerable.Range(0, at.Count).Where(i => depth[i] is not null).Average(i => at[i].X);
            Assert.IsTrue((at[front.From].X + at[front.To].X) / 2 < middle, $"{smiles}: the nearest upright is right of the middle");

            var peak = Enumerable.Range(0, at.Count).MaxBy(i => at[i].Y);
            foreach (var bond in molecule.BondsAt(peak))
                Assert.IsTrue(at[molecule.Bonds[bond].Other(peak)].Y < at[peak].Y, $"{smiles}: the topmost atom is not a peak");
        }
    }

    [TestMethod]
    public void TheSameMoleculeIsLaidTheSameWayEveryTime()
    {
        foreach (var smiles in Everyday)
            CollectionAssert.AreEqual(Lay(smiles).Structure.At.ToArray(), Lay(smiles).Structure.At.ToArray(), smiles);
    }

    private static (Molecule Molecule, Structure Structure) Lay(string smiles)
    {
        var molecule = Molecule.Read(SmilesPipeline.Of().Run(SmilesParser.Molecule(smiles)));
        return (molecule, StructureLayout.Of(molecule));
    }

    private static double Angle(Structure structure, int a, int centre, int b)
    {
        var u = structure.At[a] - structure.At[centre];
        var v = structure.At[b] - structure.At[centre];
        return Math.Acos(Math.Clamp(Vec.Dot(u.Unit, v.Unit), -1, 1)) * 180 / Math.PI;
    }

    /// <summary>Which side of the line from <paramref name="a"/> to <paramref name="b"/> atom <paramref name="c"/> is on.</summary>
    private static int Side(Structure structure, int a, int b, int c) =>
        Math.Sign(Vec.Cross(structure.At[b] - structure.At[a], structure.At[c] - structure.At[a]));
}
