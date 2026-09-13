using Nexaflow.Markdown.Ast;
using XamlMath;

using XamlMath.Boxes;
using XamlMath.Fonts;
using XamlMath.Utils;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// One character as TeX sets it: a letter in a face, a symbol the tables name, or a slot of a font chosen outright —
/// a ligature, which is two letters' worth of glyph found by asking the font.
/// <para>
/// Separate from the piece it is set as because a run asks it things before anything is set: whether two letters kern
/// or join, how far a script tucks under its lean, how an accent over it skews. Those are questions about the glyph
/// and the font, and the answer changes how the run is spaced rather than how the glyph is drawn.
/// </para>
/// </summary>
internal sealed record Glyph
{
    /// <summary>The name a fence is given for a side written open — <c>\left.</c> — which draws nothing.</summary>
    internal const string EmptyDelimiterName = "_emptyDelimiter";

    /// <summary>Every symbol the tables name, as the glyph it is.</summary>
    private static readonly System.Collections.Generic.IReadOnlyDictionary<string, Glyph> Symbols = new TexSymbolParser().GetSymbols();

    public char Character { get; init; }

    /// <summary>The face a letter is drawn from — <c>mathrm</c>, <c>text</c> — or null for the maths italic default.</summary>
    public string? TextStyle { get; init; }

    /// <summary>The symbol table's name for it, where it is a symbol rather than a letter.</summary>
    public string? SymbolName { get; init; }

    /// <summary>A font slot chosen outright, where it is neither.</summary>
    public CharFont? Fixed { get; init; }

    /// <summary>Its TeX class, which is the table's to say for a symbol and ordinary for anything else.</summary>
    public TexAtomType Type { get; init; } = TexAtomType.Ordinary;

    /// <summary>Whether the table allows it to grow as a delimiter.</summary>
    public bool IsDelimiter { get; init; }

    /// <summary>The part of the reading it was set from, when it was set from one.</summary>
    public ContentPart? Origin { get; init; }

    public static Glyph Letter(char character, string? textStyle = null) =>
        new() { Character = character, TextStyle = textStyle };

    /// <summary>The symbol the tables call this, or null where they call nothing that.</summary>
    public static Glyph? Symbol(string name) => Symbols.TryGetValue(name, out var symbol) ? symbol : null;

    /// <summary>The delimiter this name stands for, or null where it names none or what it names cannot grow.</summary>
    public static Glyph? Delimiter(string? name) =>
        name is not null && Symbol(name) is { IsDelimiter: true } symbol ? symbol : null;

    /// <summary>
    /// The delimiter a character stands for, or null. <c>.</c> is how a fence is written with one end left open —
    /// <c>\left. \right)</c> — so no delimiter is the right answer rather than a failure.
    /// </summary>
    public static Glyph? Delimiter(char character) =>
        character == '.' ? null : Delimiter(TexFormulaParser.DelimiterMapping(character));

    /// <summary>A symbol given its class outright — a bracket a command draws, whatever the table calls it.</summary>
    public static Glyph Named(string name, TexAtomType type, bool isDelimiter) =>
        new() { SymbolName = name, Type = type, IsDelimiter = isDelimiter };

    public static Glyph Slot(CharFont font) => new() { Fixed = font };

    /// <summary>The font it is drawn from: a <c>text</c> letter from the text face, everything else from the maths one.</summary>
    public ITeXFont FontFor(TexEnvironment environment) =>
        TextStyle == TexUtilities.TextStyleName ? environment.TextFont : environment.MathFont;

    public Result<CharInfo> Info(ITeXFont font, TexStyle style) =>
        SymbolName is { } name ? font.GetCharInfo(name, style)
        : Fixed is { } slot ? font.GetCharInfo(slot, style)
        : TextStyle is null ? font.GetDefaultCharInfo(Character, style)
        : font.GetCharInfo(Character, TextStyle, style);

    /// <summary>Which font slot it is, whatever the style — what kerning and ligatures are looked up by.</summary>
    public Result<CharFont> FontOf(ITeXFont font) =>
        Fixed is { } slot
            ? Result.Ok(slot)
            : Info(font, TexStyle.Display).Map(info => info.GetCharacterFont());

    public bool IsSupportedByFont(ITeXFont font, TexStyle style) => Info(font, style).IsSuccess;

    /// <summary>
    /// The glyph as a piece — from the bold companion of its font where the environment is bold and one exists, which
    /// is how <c>\boldsymbol</c> reaches Greek letters and symbols.
    /// </summary>
    public Set Set(TexEnvironment environment)
    {
        var font = FontFor(environment);
        var info = Info(font, environment.Style);

        if (environment.IsBold && info.IsSuccess)
        {
            var bold = font.GetBoldCharInfo(info.Value, environment.Style);
            if (bold.IsSuccess) info = bold;
        }

        return global::Nexaflow.Visuals.Text.Markdown.Latex.Set.Of(new CharBox(environment, info.Value)) with { Part = Origin };
    }
}
