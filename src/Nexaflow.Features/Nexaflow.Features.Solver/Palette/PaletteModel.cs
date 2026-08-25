namespace Nexaflow.Features.Solver.Palette;

/// <summary>
/// How a key's text meets what is already in the editor.
/// <para>
/// A palette is only worth having if pressing a key does the obvious thing. Typing raw text is the
/// obvious thing for a digit and the wrong thing for everything else: a function should take what
/// you have selected as its argument, a factorial belongs after a number rather than instead of it,
/// and a square makes no sense with nothing to square.
/// </para>
/// </summary>
public enum KeyInsert
{
    /// <summary>Types its text at the caret, replacing any selection. Digits, operators, constants.</summary>
    Literal,

    /// <summary>
    /// Brackets something: a selection becomes the argument, and with nothing selected a
    /// placeholder is inserted and left selected so the next keystroke replaces it.
    /// </summary>
    Wrapping,

    /// <summary>
    /// Follows an operand — <c>!</c>, <c>^2</c>, <c>%</c>. Does nothing at the start of a line or
    /// after an operator, because there is nothing there to apply it to.
    /// </summary>
    Postfix,
}

/// <summary>What a key is for, so the view can colour it without knowing what it says.</summary>
public enum PaletteKeyKind
{
    /// <summary>0–9 and the decimal point.</summary>
    Digit,

    /// <summary>Arithmetic and relations.</summary>
    Operator,

    /// <summary>A named function.</summary>
    Function,

    /// <summary>π, e and friends.</summary>
    Constant,

    /// <summary>Clear, backspace, and the palette's own page toggles.</summary>
    Action,

    /// <summary>Anything else — Greek letters, arrows, delimiters.</summary>
    Symbol,

    /// <summary>
    /// A held-open slot. A group whose content does not reach a whole row of eight is padded with
    /// these rather than left ragged, so the grid is the same shape whichever group is showing and
    /// the keys next to a given position do not move as you navigate.
    /// </summary>
    Blank,
}

/// <summary>One button on the palette.</summary>
/// <param name="Label">What the key shows. Kept short — this is a key cap, not a description.</param>
/// <param name="Insert">
/// The text inserted at the caret. Empty for a key the view handles itself (a page toggle, clear,
/// backspace).
/// </param>
/// <param name="Tooltip">What it inserts, spelled out — the point of the palette is not having to remember.</param>
/// <param name="Kind">Drives the key's colour.</param>
/// <param name="CaretBack">
/// How far to move the caret back after inserting, so <c>\frac{}{}</c> leaves you inside the
/// numerator rather than past the whole thing.
/// </param>
public sealed record PaletteKey(
    string Label,
    string Insert,
    string Tooltip = "",
    PaletteKeyKind Kind = PaletteKeyKind.Symbol,
    int CaretBack = 0)
{
    /// <summary>
    /// Set instead of <see cref="Insert"/> for a key that acts on the palette or the editor — swap
    /// page, clear, backspace, toggle degrees — rather than typing something.
    /// </summary>
    public string CommandId { get; init; } = string.Empty;

    /// <summary>How the key's text meets what is already there.</summary>
    public KeyInsert InsertKind { get; init; } = KeyInsert.Literal;

    /// <summary>
    /// The closing half of a <see cref="KeyInsert.Wrapping"/> key — the <c>)</c> of <c>sin(…)</c>.
    /// </summary>
    public string Close { get; init; } = string.Empty;

    /// <summary>A key the view acts on itself rather than inserting.</summary>
    public bool IsCommand => CommandId.Length > 0;

    /// <summary>A padding slot rather than a key — drawn as a gap, never pressable.</summary>
    public bool IsBlank => Kind == PaletteKeyKind.Blank;
}

/// <summary>
/// A named set of keys, optionally holding further sets. One level of nesting is what the sunburst
/// draws: the categories form its inner ring and their subgroups the outer one.
/// </summary>
/// <param name="Id">Stable id — what the sunburst reports when an arc is clicked.</param>
/// <param name="Label">Shown on the arc and the breadcrumb.</param>
/// <param name="Keys">The keys shown in the grid when this group is selected.</param>
/// <param name="Children">Subgroups, or empty for a leaf group.</param>
public sealed record PaletteGroup(
    string Id,
    string Label,
    IReadOnlyList<PaletteKey> Keys,
    IReadOnlyList<PaletteGroup> Children)
{
    /// <summary>Convenience for a leaf group.</summary>
    public PaletteGroup(string id, string label, IReadOnlyList<PaletteKey> keys)
        : this(id, label, keys, []) { }

    /// <summary>Every key in this group and everything under it, in order.</summary>
    public IReadOnlyList<PaletteKey> AllKeys =>
        Children.Count == 0 ? Keys : [.. Keys, .. Children.SelectMany(c => c.AllKeys)];

    /// <summary>Finds a group by id anywhere in this subtree.</summary>
    public PaletteGroup? Find(string id)
    {
        if (Id == id) return this;
        foreach (var child in Children)
            if (child.Find(id) is { } hit) return hit;
        return null;
    }
}

/// <summary>
/// One position on the navigator: a category to open, a key to type, or a held-open gap.
/// <para>
/// The navigator shows the tree one ring at a time and the symbols themselves are the last ring, so
/// a position is a group at some depths and a key at the deepest. Both are the same thing to look at
/// and to click, so they are the same thing to hold — which is what keeps the drill loop one code
/// path rather than two that have to agree.
/// </para>
/// </summary>
/// <param name="Id">What the navigator reports when this position is clicked. Empty for a gap.</param>
/// <param name="Label">What it reads — a category name, or a key's cap.</param>
/// <param name="Tooltip">Spelled out on hover.</param>
/// <param name="Opens">Whether clicking it goes a level deeper rather than typing.</param>
/// <param name="Key">The key to type, for a position that is one.</param>
public sealed record PaletteTile(string Id, string Label, string Tooltip, bool Opens, PaletteKey? Key)
{
    /// <summary>A held-open position — drawn as a gap, never clickable.</summary>
    public static PaletteTile Gap { get; } = new(string.Empty, string.Empty, string.Empty, false, null);

    /// <summary>Whether this position holds anything at all.</summary>
    public bool IsGap => Id.Length == 0;
}

/// <summary>
/// One step of the navigator's breadcrumb — and a way back to that level.
/// </summary>
/// <param name="Label">The category or group name, or the name of the tree's root.</param>
/// <param name="Depth">How deep this step is; clicking it truncates the path to exactly that.</param>
/// <param name="IsCurrent">Whether this is where the navigator already is, so it leads nowhere.</param>
public sealed record PaletteCrumb(string Label, int Depth, bool IsCurrent);

/// <summary>Shared constants for the palette's insertion behaviour.</summary>
public static class PaletteText
{
    /// <summary>
    /// What a wrapping key puts between its brackets when nothing is selected. Left selected, so the
    /// next keystroke replaces it — and chosen to be something that still parses if it is not, so a
    /// half-finished <c>sin(x)</c> is a formula rather than a syntax error.
    /// </summary>
    public const string Placeholder = "x";

    /// <summary>One held-open slot. See <see cref="PaletteKeyKind.Blank"/>.</summary>
    public static PaletteKey Blank { get; } = new(string.Empty, string.Empty, string.Empty, PaletteKeyKind.Blank);

    /// <summary>Pads a group's keys out to exactly <paramref name="slots"/>.</summary>
    public static IReadOnlyList<PaletteKey> Fill(IReadOnlyList<PaletteKey> keys, int slots)
    {
        if (keys.Count == slots) return keys;

        var padded = new List<PaletteKey>(keys);
        while (padded.Count < slots) padded.Add(Blank);
        return padded;
    }
}
