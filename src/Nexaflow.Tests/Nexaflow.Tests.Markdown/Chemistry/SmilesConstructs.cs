namespace Nexaflow.Tests.Markdown.Chemistry;

/// <summary>
/// The record of what a <c>smiles</c> block can say: every construct the reader knows, and the half-written and
/// wrong things it has to hold without losing. What a new construct is added to.
/// </summary>
internal static class SmilesConstructs
{
    /// <summary>Whole blocks, as a reader would write them.</summary>
    public static readonly (string What, string Source)[] Blocks =
    [
        ("the format's own example", "chemistry\nc1ccccc1 \"Benzene\"\nCC(=O)O \"Acetic acid\"\nCCO \"Ethanol\""),
        ("no keyword", "CCO"),
        ("comments and blank lines", "# alcohols\n\nCCO \"Ethanol\"\n  # the end  \n"),
        ("windows line endings", "chemistry\r\nCCO\r\nC\r\n"),
        ("a caption hard against its molecule", "CCO\"Ethanol\""),
        ("a caption never closed", "CCO \"Ethan"),
        ("an empty caption", "CCO \"\""),
        ("words after the caption", "CCO \"Ethanol\" and more"),
        ("a caption without quotes", "CCO Ethanol"),
        ("a caption with no molecule", "\"Benzene\""),
        ("the keyword twice", "chemistry\nchemistry"),
        ("the keyword after a molecule", "CCO\nchemistry"),
        ("nothing at all", ""),
        ("only space", "  \n \n"),
    ];

    /// <summary>One SMILES string each: the grammar, and what it looks like broken.</summary>
    public static readonly (string What, string Smiles)[] Molecules =
    [
        ("a chain", "CCCCCC"),
        ("a branch", "CC(C)CO"),
        ("nested branches", "CC(C(C)(C)C)O"),
        ("double and triple bonds", "C=CC#N"),
        ("a quadruple bond", "[Mo]$[Mo]"),
        ("an aromatic ring", "c1ccccc1"),
        ("fused rings", "c1ccc2ccccc2c1"),
        ("a ring number reused", "C1CC1C1CC1"),
        ("two-digit ring numbers", "C%10CCCCC%10"),
        ("a ring bond with its bond written", "C=1CCCCC=1"),
        ("bracket atoms, every part", "[13CH3+:2]"),
        ("stereocentres", "N[C@@H](C)C(=O)O"),
        ("stereo written longhand", "F[C@TH1](Cl)(Br)I"),
        ("double-bond geometry", "F/C=C\\F"),
        ("charges", "[NH4+].[Cl-].[Fe+2].[O--]"),
        ("aromatic bracket atoms", "c1cc[nH]c1"),
        ("selenophene", "c1cc[se]c1"),
        ("a wildcard", "*CC*"),
        ("two molecules", "[Na+].[Cl-]"),
        ("an explicit aromatic bond", "c1:c:c:c:c:c:1"),
        ("a nitro group written short", "CN(=O)=O"),
        // What nobody means to write.
        ("a branch never closed", "CC(C"),
        ("a branch closed twice", "CC(C))C"),
        ("an empty branch", "CC()C"),
        ("a branch before any atom", "(C)C"),
        ("a bracket never closed", "C[NH"),
        ("an element that is not one", "C[Xx]C"),
        ("an element that needs brackets", "CNaC"),
        ("a bond at the end", "CC="),
        ("a bond at the start", "=CC"),
        ("two bonds in a row", "C==C"),
        ("a ring never closed", "C1CCC"),
        ("a ring closing on its own atom", "C11"),
        ("a ring number cut short", "C%1CC"),
        ("a character that is not SMILES", "C!C"),
        ("lowercase that is not aromatic", "CxC"),
    ];
}
