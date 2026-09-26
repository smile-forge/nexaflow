using Nexaflow.Markdown.Chemistry;
using Nexaflow.Markdown.Chemistry.Depiction;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Chemistry;

/// <summary>
/// The reader and the layout over five thousand molecules nobody here wrote, held to RDKit.
///
/// <para>
/// The construct list is the record of what is supported; this is what speaks for what nobody thought to write down.
/// The corpus is SmilesDB's <c>processedSmiles.txt</c>, and beside it <c>reference.tsv</c> — for every line, whether
/// RDKit read it, the total hydrogens RDKit puts on each atom, and how many overlapping atoms RDKit's own depiction
/// has. It is made by <c>make_reference.py</c> in the same folder, from the <c>molecular-string-renderer</c> package's
/// RDKit.
/// </para>
/// <para>
/// Opt-in, because it needs the corpus on disk: <c>NEXAFLOW_SMILES_CORPUS</c> points at the folder, and
/// <c>D:\Datasets\smiles</c> is tried when it does not. No fonts, no desktop — the whole sweep is seconds.
/// </para>
/// </summary>
[TestClass]
[CoversNode("smiles-chemistry")]
[CoversNode("smiles-structure-layout")]
public class SmilesCorpusTests
{
    private const string Default = @"D:\Datasets\smiles";

    [TestMethod]
    public void FiveThousandRealMoleculesReadBackExactly()
    {
        if (Corpus() is not { } rows) { Assert.Inconclusive(Missing); return; }

        var failures = new List<string>();
        foreach (var row in rows)
        {
            var tree = SmilesParser.Molecule(row.Smiles);
            if (tree.Print() != row.Smiles) failures.Add($"line {row.Line}: did not read back");

            foreach (var place in tree.Placed())
            {
                if (!place.Node.IsLeaf || row.Smiles.Substring(place.Start, place.Node.Width) == place.Node.Text) continue;
                failures.Add($"line {row.Line}: {place.Node.Kind} at {place.Start} invented text");
                break;
            }

            var read = tree;
            foreach (var stage in SmilesPipeline.Of().Stages)
            {
                read = stage.Run(read);
                if (read.Print() == row.Smiles) continue;
                failures.Add($"line {row.Line}: {stage.Name} changed the source");
                break;
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join("\n  ", failures.Take(10)));
    }

    [TestMethod]
    public void WhatReadsAndWhatDoesNotAgreesWithRDKit()
    {
        if (Corpus() is not { } rows) { Assert.Inconclusive(Missing); return; }

        var failures = new List<string>();
        foreach (var row in rows)
        {
            var troubles = SmilesPipeline.Of().Run(SmilesParser.Molecule(row.Smiles))
                .SelfAndDescendants().Select(node => node.Trouble).OfType<string>().Distinct().ToList();

            if ((troubles.Count == 0) != row.Read)
                failures.Add($"line {row.Line}: RDKit {(row.Read ? "reads it" : "refuses it")}, we say {(troubles.Count == 0 ? "nothing" : string.Join(" | ", troubles))}");
        }

        Assert.AreEqual(0, failures.Count, string.Join("\n  ", failures.Take(10)));
    }

    [TestMethod]
    public void EveryAtomCarriesTheHydrogensRDKitGivesIt()
    {
        if (Corpus() is not { } rows) { Assert.Inconclusive(Missing); return; }

        var failures = new List<string>();
        foreach (var row in rows.Where(row => row.Read))
        {
            var molecule = (MoleculeNode)SmilesPipeline.Of().Run(SmilesParser.Molecule(row.Smiles));
            var ours = string.Join(",", molecule.Atoms.Select(atom => atom.TotalHydrogens));
            if (ours != row.Hydrogens) failures.Add($"line {row.Line}: ours {ours}, RDKit {row.Hydrogens}");
        }

        Assert.AreEqual(0, failures.Count, string.Join("\n  ", failures.Take(10)));
    }

    [TestMethod]
    public void AndFewerOfThemOverlapThanInRDKitsOwnDrawings()
    {
        // RDKit's depictions overlap two atoms in about one molecule in thirty of this corpus. Ours is held to doing
        // better than that across the whole of it, with the bonds of any one molecule varying in length by no more
        // than a quarter — a macrocycle fused to a cage is allowed to strain a few, and nothing more. A cage drawn as a solid
        // is left out of that: its bonds are foreshortened on purpose, because that is what a picture of a solid is.
        if (Corpus() is not { } rows) { Assert.Inconclusive(Missing); return; }

        int ours = 0, theirs = 0;
        var failures = new List<string>();

        foreach (var row in rows.Where(row => row.Read))
        {
            var molecule = (MoleculeNode)SmilesPipeline.Of().Run(SmilesParser.Molecule(row.Smiles));
            var structure = molecule.Structure!;
            var at = structure.At;

            if (at.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) { failures.Add($"line {row.Line}: not a number"); continue; }

            var flat = molecule.Bonds.Where(bond => structure.Depth[bond.From] is null || structure.Depth[bond.To] is null).ToList();
            if (flat.Count > 0)
            {
                var lengths = flat.Select(bond => Vec.Distance(at[bond.From], at[bond.To])).ToList();
                var mean = lengths.Average();
                var spread = Math.Sqrt(lengths.Average(length => (length - mean) * (length - mean))) / mean;
                if (spread > 0.25) failures.Add($"line {row.Line}: its bond lengths vary by {spread:P0}");
            }

            var overlapping = false;
            for (var i = 0; i < at.Count && !overlapping; i++)
                for (var j = i + 1; j < at.Count; j++)
                    if (molecule.Between(i, j) is null && Vec.Distance(at[i], at[j]) < 0.4) { overlapping = true; break; }

            if (overlapping) ours++;
            if (row.Clashes > 0) theirs++;
        }

        Assert.AreEqual(0, failures.Count, string.Join("\n  ", failures.Take(10)));
        Assert.IsTrue(ours < theirs, $"{ours} of our drawings overlap atoms, against RDKit's {theirs}");
    }

    // ── The corpus ──────────────────────────────────────────────────────────

    private const string Missing =
        "Point NEXAFLOW_SMILES_CORPUS at a folder holding reference.tsv (see make_reference.py) to run this. "
        + "The same checks run over the hand-written construct list, which always runs.";

    private sealed record Row(int Line, string Smiles, bool Read, string Hydrogens, int Clashes);

    private static IReadOnlyList<Row>? Corpus()
    {
        var root = Environment.GetEnvironmentVariable("NEXAFLOW_SMILES_CORPUS");
        if (string.IsNullOrWhiteSpace(root)) root = Default;

        var reference = Path.Combine(root, "reference.tsv");
        if (!File.Exists(reference)) return null;

        return [.. File.ReadLines(reference).Skip(1).Select(line => line.Split('\t')).Select(cols => new Row(
            int.Parse(cols[0]), cols[1], cols[2] == "ok", cols[5], cols[9].Length == 0 ? 0 : int.Parse(cols[9])))];
    }
}
