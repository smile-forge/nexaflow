using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Chemistry.Stages;

/// <summary>
/// Works out how many hydrogens each unbracketed atom carries, and says where an atom has more bonds than its
/// element can make.
///
/// <para>
/// SMILES leaves hydrogens out: <c>C</c> is methane and <c>CC</c> ethane, because an atom written without brackets
/// is filled up to the fewest bonds its element makes that its written bonds fit in. That is a fact about every
/// bond at the atom — ring closures included, which is why this runs after <see cref="ConnectAtoms"/>.
/// </para>
/// <para>
/// <b>An aromatic atom keeps one bond back</b> for the double bond its ring will give it: <c>c</c> with two ring
/// neighbours is CH, not CH₂. Where it has no bond to spare — a nitrogen in a five-membered ring written <c>n</c>
/// with three neighbours, an <c>o</c> — it simply has no hydrogen, and <see cref="Kekulize"/> finds it no double bond.
/// </para>
/// <para>
/// A bracket atom says its own hydrogens, so it is only checked: its bonds and its hydrogens have to fit its
/// element at its charge. The reason names the charge that would make it fit where one would, because a
/// four-bonded nitrogen is nearly always an ammonium somebody wrote without its <c>+</c>.
/// </para>
/// </summary>
public sealed class CountHydrogens : IAstStage
{
    public string Name => "smiles:hydrogens";

    public ContentNode Run(ContentNode tree) => SmilesRewrite.Molecules(tree, Count);

    private static ContentNode Count(ContentNode node)
    {
        if (node is not MoleculeNode molecule) return node;
        var said = new Dictionary<int, (int Hydrogens, string? Trouble)>();

        foreach (var atom in molecule.Atoms)
        {
            if (atom.Number == 0) continue;

            var used = molecule.Valence(atom.Index);
            var allowed = Oxidised(molecule, atom) ?? Elements.Allowed(atom.Number, atom.Charge);

            if (atom.Bracketed)
            {
                var total = used + (atom.Hydrogens ?? 0);
                if (allowed is not null && Elements.Fitting(allowed, total) is null)
                    said[atom.Index] = (0, TooMany(atom, total));
                continue;
            }

            if (allowed is null) continue;

            var valence = Elements.Fitting(allowed, used);
            if (valence is null)
            {
                said[atom.Index] = (0, TooMany(atom, used));
                continue;
            }

            var hydrogens = Math.Max(0, valence.Value - used - (atom.Aromatic ? 1 : 0));
            if (hydrogens > 0) said[atom.Index] = (hydrogens, null);
        }

        if (said.Count == 0) return node;

        return SmilesRewrite.Atoms(node, (index, atom) =>
        {
            if (!said.TryGetValue(index, out var fact)) return atom;

            var told = fact.Hydrogens > 0 && atom is AtomNode read ? read.Carrying(fact.Hydrogens) : atom;

            return SmilesRewrite.Troubled(told, fact.Trouble);
        });
    }

    /// <summary>
    /// The extra bonds a neutral nitrogen or halogen may make as double bonds to oxygen — a nitro group written
    /// <c>N(=O)=O</c>, a perchlorate written <c>Cl(=O)(=O)(=O)[O-]</c>. Both are the charge-separated forms written
    /// the short way, which every reader accepts and chemists write constantly; a neutral nitrogen with four single
    /// bonds is not, and still reads as the missing charge it nearly always is.
    /// </summary>
    private static IReadOnlyList<int>? Oxidised(MoleculeNode molecule, AtomNode atom)
    {
        if (atom.Charge != 0 || atom.Number is not (7 or 17 or 35 or 53)) return null;

        var toOxygen = molecule.BondsAt(atom.Index)
            .Select(bond => molecule.Bonds[bond])
            .Count(bond => bond.Order == BondOrder.Double && molecule.Atoms[bond.Other(atom.Index)].Number == 8);

        if (toOxygen == 0) return null;
        return atom.Number == 7 ? [3, 5] : [1, 3, 5, 7];
    }

    /// <summary>Why an atom cannot have the bonds it was written with, and the charge that would let it.</summary>
    private static string TooMany(AtomNode atom, int bonds)
    {
        var name = Elements.Name(atom.Number);
        var element = char.ToUpperInvariant(name[0]) + name[1..];
        var plural = bonds == 1 ? "bond" : "bonds";
        var said = $"{element}{Charged(atom.Charge)} cannot make {Count(bonds)} {plural}.";

        foreach (var charge in new[] { atom.Charge + 1, atom.Charge - 1 })
        {
            var fits = Elements.Allowed(atom.Number, charge);
            if (fits is null || Elements.Fitting(fits, bonds) is null) continue;

            var written = "[" + atom.Symbol + Hydrogens(atom) + Sign(charge) + "]";
            return $"{said} With a charge it could: {written}.";
        }

        return said;
    }

    private static string Charged(int charge) => charge switch
    {
        0 => "",
        > 0 => $" with a charge of +{charge}",
        _ => $" with a charge of −{-charge}",
    };

    private static string Hydrogens(AtomNode atom) => atom.Hydrogens switch
    {
        null or 0 => "",
        1 => "H",
        var n => $"H{n}",
    };

    private static string Sign(int charge) => charge switch
    {
        0 => "",
        1 => "+",
        -1 => "-",
        > 0 => $"+{charge}",
        _ => $"-{-charge}",
    };

    private static string Count(int n) => n switch
    {
        1 => "one", 2 => "two", 3 => "three", 4 => "four", 5 => "five", 6 => "six", 7 => "seven", 8 => "eight",
        _ => n.ToString(),
    };
}
