using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Exceptions;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Parsers;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Rendering;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Visuals.Text.Markdown.Latex.Tex;

// TODO: Put all error strings into resources.
// TODO: Use TextReader for lexing.
public class TexFormulaParser
{
    internal const char leftGroupChar = '{';
    internal const char rightGroupChar = '}';

        /// <summary>
    /// A set of names of the commands that are embedded in the parser itself, <see cref="ProcessCommand"/>.
    /// These're not the additional commands that may be supplied via <see cref="_commandRegistry"/>.
    /// </summary>
    private static readonly HashSet<string> embeddedCommands = new()
    {
        "color",
        "colorbox",
        "frac",
        "left",
        "overline",
        "right",
        "sqrt",
        "textcolor"
    };

    private static readonly IReadOnlyList<string> symbols;
    private static readonly IReadOnlyList<string> delimeters;
    private static readonly HashSet<string> textStyles;

    /// <summary>
    /// Text styles whose argument is ordinary text rather than a formula: the spaces in it are kept and the
    /// characters are not treated as math symbols. <c>\text</c> and the <c>\text*</c> font-switching family.
    /// </summary>
    /// <summary>
    /// The big operators whose limits go beside them rather than above and below, in every style:
    /// the integrals. TeX gives <c>\intop</c> <c>\nolimits</c> by default and <c>\sum</c>
    /// <c>\limits</c>, which is why an integral's bounds sit at its side in every published paper.
    /// <c>\limits</c> after one still stacks them.
    /// </summary>
    private static readonly HashSet<string> sideLimitOperators = new()
    {
        "int",
        "intop",
        "iint",
        "iiint",
        "iiiint",
        "idotsint",
        "oint",
        "oiint",
        "oiiint",
    };

    private static readonly HashSet<string> rawTextStyles = new()
    {
        TexUtilities.TextStyleName,
        "mbox",
        "textbf",
        "textit",
        "textrm",
        "textsc",
        "textsf",
        "texttt",
    };

    private static readonly IReadOnlyList<IReadOnlyList<string>> delimiterNames = new[]
    {
        new[] { "lbrace", "rbrace" },
        new[] { "(", ")" },
        new[] { "lbrack", "rbrack" },
        new[] { "downarrow", "downarrow" },
        new[] { "uparrow", "uparrow" },
        new[] { "updownarrow", "updownarrow" },
        new[] { "Downarrow", "Downarrow" },
        new[] { "Uparrow", "Uparrow" },
        new[] { "Updownarrow", "Updownarrow" },
        new[] { "vert", "vert" },
        new[] { "Vert", "Vert" }
    };

    static TexFormulaParser()
    {
        var formulaSettingsParser = new TexPredefinedFormulaSettingsParser();
        symbols = formulaSettingsParser.GetSymbolMappings();
        delimeters = formulaSettingsParser.GetDelimiterMappings();
        textStyles = new HashSet<string>(formulaSettingsParser.GetTextStyles());
    }

    internal static IReadOnlyList<IReadOnlyList<string>> DelimiterNames => delimiterNames;

    /// <summary>
    /// The command a delimiter character stands for, or null when it stands for none.
    ///
    /// <para>
    /// The table is an array the width of the font's character codes, with a null wherever nothing is
    /// mapped — so a miss was never the KeyNotFoundException this used to catch, and a character past the
    /// end of it threw an IndexOutOfRangeException that nothing caught at all.
    /// </para>
    /// </summary>
    internal static string? DelimiterMapping(char character) =>
        character < delimeters.Count ? delimeters[character] : null;

    private static bool IsSymbol(char c) => !char.IsLetterOrDigit(c);

    /// <summary>
    /// Whether anything here has a reading for a command at all — a command parser, a macro, a style or a
    /// symbol.
    ///
    /// <para>
    /// Asked by <see cref="Nexaflow.Visuals.Text.Markdown.Latex.LatexBuilder"/> to tell two different things apart, both of which reach
    /// the same place in it. A command this knows but the builder has no drawing for — <c>\textrm</c>,
    /// <c>\bbox</c> — is a gap in the builder, and the reader should see their formula rather than a
    /// complaint about it. A command <em>nothing</em> knows is a mistake in what they typed, and saying so
    /// is the useful thing to do.
    /// </para>
    /// </summary>
    internal bool Knows(string command) =>
        StandardCommands.Dictionary.ContainsKey(command)
        || textStyles.Contains(command)
        || embeddedCommands.Contains(command)
        || Glyph.Symbol(command) is not null;

    /// <summary>
    /// Whether there is a drawing for this command, given its name as it was written, backslash and
    /// all. What a reader hands to <c>TexPipeline</c> so that the reading can say what cannot be set,
    /// without the reader having to know anything about setting.
    /// </summary>
    internal bool Draws(string written) =>
        written.Length > 1 && written[0] == '\\' && Knows(written[1..]);

    /// <summary>
    /// The style this command sets its contents in — <c>mathrm</c>, <c>mathbf</c> — or null when it sets
    /// none. Which commands those are is read from the settings file rather than listed, so a build that
    /// learns a new one teaches every reader of LaTeX here at once.
    /// </summary>
    internal static string? TextStyleOf(string command) =>
        textStyles.Contains(command) ? command : null;

    /// <summary>
    /// Whether the contents are read as words rather than as maths — <c>\text</c>, <c>\mbox</c> and the
    /// <c>\text…</c> family. Every character in one is set as written, spaces included, so what is inside
    /// is not a formula and is not parsed as one.
    /// </summary>
    internal static bool IsRawTextStyle(string command) => rawTextStyles.Contains(command);

    /// <summary>One character, as the glyph it is set as — a symbol by the table, or a letter.</summary>
    internal static Glyph GlyphOf(char character, string? textStyle = null)
    {
        if (!IsSymbol(character) || textStyle == TexUtilities.TextStyleName)
            return Glyph.Letter(character, textStyle);

        var symbolName = symbols.ElementAtOrDefault(character);

        return string.IsNullOrEmpty(symbolName) ? Glyph.Letter(character, textStyle) : Glyph.Symbol(symbolName)!;
    }

    /// <summary>Whether an operator sets its limits beside it in every style — the integrals.</summary>
    internal static bool SetsLimitsBeside(string name) => sideLimitOperators.Contains(name);
}
