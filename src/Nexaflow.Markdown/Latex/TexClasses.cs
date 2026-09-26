namespace Nexaflow.Markdown.Latex;

/// <summary>
/// What kind of atom a piece of a formula is — TeX's class, which is what decides the room either side of it: a relation
/// is spaced wider than a binary operator, and an opening bracket takes none after it.
/// </summary>
public enum TexAtomType
{
    None = -1,

    /// <summary>An ordinary atom, like Ω or ℜ.</summary>
    Ordinary = 0,

    /// <summary>A large operator, like ∑ or ∬.</summary>
    BigOperator = 1,

    /// <summary>A binary operation, like ÷ or ×.</summary>
    BinaryOperator = 2,

    /// <summary>A relation, like ≌ or ≋.</summary>
    Relation = 3,

    /// <summary>An opening, like ⟦ or {.</summary>
    Opening = 4,

    /// <summary>A closing, like ⟧ or }.</summary>
    Closing = 5,

    /// <summary>Punctuation, like , or :.</summary>
    Punctuation = 6,

    /// <summary>A fraction or a fenced group, spaced as something inside a formula rather than beside it.</summary>
    Inner = 7,

    /// <summary>An accent, like the breve of X̆ or the diaeresis of Ö.</summary>
    Accent = 10,
}

/// <summary>The four sizes TeX sets maths in: a display, a line of text, a script, and a script's script.</summary>
public enum TexStyle
{
    Display = 0,
    Text = 2,
    Script = 4,
    ScriptScript = 6,
}

/// <summary>The units a length in a formula is written in.</summary>
public enum TexUnit
{
    Em = 0,
    Ex = 1,
    Pixel = 2,
    Point = 3,
    Pica = 4,

    /// <summary>A maths unit: an eighteenth of a quad.</summary>
    Mu = 5,
}

/// <summary>Which way something is pushed within the room it is given.</summary>
public enum TexAlignment
{
    Left = 0,
    Right = 1,
    Center = 2,
    Top = 3,
    Bottom = 4,
}

/// <summary>How the cells of a grid sit in their columns.</summary>
public enum MatrixCellAlignment
{
    Left,
    Center,

    /// <summary>An aligned block's: its columns are an equation and its parts, alternately right and left.</summary>
    Aligned,
}

/// <summary>What an arrow drawn over its shaft carries — <c>\overrightarrow</c>, <c>\xmapsto</c> and their families.</summary>
[Flags]
public enum ArrowDecoration
{
    None = 0,

    /// <summary>An arrowhead at the left end.</summary>
    HeadLeft = 1,

    /// <summary>An arrowhead at the right end.</summary>
    HeadRight = 2,

    /// <summary>Two parallel shafts instead of one, for the \Rightarrow family.</summary>
    DoubleShaft = 4,

    /// <summary>A vertical bar at the left end, for \mapsto.</summary>
    TailBarLeft = 8,
}

/// <summary>Which way what something is crossed out with runs: <c>\cancel</c>, <c>\bcancel</c>, <c>\xcancel</c>.</summary>
[Flags]
public enum StrokeMode
{
    None = 0,
    Normal = 1,
    Back = 2,
    Both = 3,
}
