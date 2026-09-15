namespace Nexaflow.Visuals.Text.Markdown.Chemistry;

/// <summary>The pieces a <c>smiles</c> block's layout is made of.</summary>
public static class MoleculePiece
{
    /// <summary>The whole block: every entry in it, in rows.</summary>
    public const string Block = "Block";

    /// <summary>One entry: a molecule, its caption, and the reason beneath when it did not read.</summary>
    public const string Entry = "Entry";

    /// <summary>A structure: its bonds and its atoms.</summary>
    public const string Molecule = "Molecule";

    /// <summary>An atom — its label where it has one, the corner of its bonds where it does not.</summary>
    public const string Atom = "Atom";

    /// <summary>A bond, however many lines it is drawn with.</summary>
    public const string Bond = "Bond";

    /// <summary>The caption under a structure.</summary>
    public const string Caption = "Caption";

    /// <summary>A string with no atom in it that could be read, shown as written and struck through.</summary>
    public const string StandIn = "StandIn";

    /// <summary>Why an entry did not draw as itself.</summary>
    public const string Trouble = "Trouble";
}
