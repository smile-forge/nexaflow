namespace Nexaflow.Markdown.Chemistry;

/// <summary>
/// What a piece of a <c>smiles</c> block is.
/// </summary>
/// <remarks>
/// About syntax, not chemistry: an <see cref="Atom"/> is an element symbol with whatever was written round it,
/// whether or not it turns out to carry hydrogens, close a ring or share a double bond. Those are worked out by the
/// stages in <see cref="SmilesPipeline"/>, which make the atom their own <see cref="AtomNode"/>, because they are facts
/// about its neighbours as much as about the letter.
/// </remarks>
public static class SmilesKinds
{
    /// <summary>The whole body of the fence: its lines, in order.</summary>
    public const string Block = "smiles-block";

    /// <summary>One line and the characters that ended it — a molecule, the header, a comment, nothing, or what could not be read.</summary>
    public const string Line = "smiles-line";

    /// <summary>The <c>chemistry</c> keyword that names the format, on a line of its own before any molecule.</summary>
    public const string Header = "smiles-header";

    /// <summary>A molecule and the label written after it.</summary>
    public const string Entry = "smiles-entry";

    /// <summary>One SMILES string: every atom, bond, branch and ring closure in it, in the order written.</summary>
    public const string Molecule = "smiles-molecule";

    /// <summary>The caption in double quotes after a molecule.</summary>
    public const string Label = "smiles-label";

    /// <summary>
    /// One atom: a bare symbol from the organic subset (<c>C</c>, <c>Cl</c>, <c>c</c>, <c>*</c>) or a bracket atom
    /// (<c>[13CH3+:2]</c>) with its parts.
    /// </summary>
    public const string Atom = "smiles-atom";

    /// <summary>The element symbol of an atom — lowercase where it was written aromatic.</summary>
    public const string Symbol = "smiles-symbol";

    /// <summary>The mass number before a bracket atom's symbol.</summary>
    public const string Isotope = "smiles-isotope";

    /// <summary>A bracket atom's handedness: <c>@</c>, <c>@@</c>, <c>@TH1</c>, <c>@SP2</c>…</summary>
    public const string Chirality = "smiles-chirality";

    /// <summary>A bracket atom's hydrogen count: <c>H</c> or <c>H3</c>.</summary>
    public const string Hydrogens = "smiles-hydrogens";

    /// <summary>A bracket atom's charge: <c>+</c>, <c>--</c>, <c>+2</c>.</summary>
    public const string Charge = "smiles-charge";

    /// <summary>A bracket atom's class: <c>:12</c>.</summary>
    public const string Class = "smiles-class";

    /// <summary>A bond written between two atoms: <c>-</c> <c>=</c> <c>#</c> <c>$</c> <c>:</c> <c>/</c> <c>\</c>.</summary>
    public const string Bond = "smiles-bond";

    /// <summary>A ring-closure digit after an atom, with the bond written before it when there is one.</summary>
    public const string RingBond = "smiles-ring-bond";

    /// <summary>The number of a ring closure: <c>1</c> or <c>%12</c>.</summary>
    public const string RingNumber = "smiles-ring-number";

    /// <summary>A side chain in round brackets.</summary>
    public const string Branch = "smiles-branch";

    /// <summary>A <c>.</c> — what separates two molecules written in one string.</summary>
    public const string Dot = "smiles-dot";
}

/// <summary>What a piece of a <c>smiles</c> block is <em>to</em> the piece holding it.</summary>
public static class SmilesRoles
{
    public const string Molecule = "molecule";

    public const string Label = "label";

    public const string Atom = "atom";

    public const string Symbol = "symbol";

    public const string Isotope = "isotope";

    public const string Chirality = "chirality";

    public const string Hydrogens = "hydrogens";

    public const string Charge = "charge";

    public const string Class = "class";

    public const string Bond = "bond";

    /// <summary>A ring closure in a chain.</summary>
    public const string Ring = "ring";

    /// <summary>A side chain in a chain.</summary>
    public const string Branch = "branch";

    public const string RingNumber = "ring-number";
}
