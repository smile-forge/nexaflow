using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using XamlMath.Atoms;
using XamlMath.Colors;
using XamlMath.Exceptions;
using XamlMath.Parsers;
using XamlMath.Rendering;

namespace XamlMath;

// TODO: Put all error strings into resources.
// TODO: Use TextReader for lexing.
public class TexFormulaParser
{
    // Special characters for parsing
    private const char escapeChar = '\\';

    internal const char leftGroupChar = '{';
    internal const char rightGroupChar = '}';

    private const char leftBracketChar = '[';
    private const char rightBracketChar = ']';

    private const char subScriptChar = '_';
    private const char superScriptChar = '^';
    private const char primeChar = '\'';
    private const char tieChar = '~';

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

    // TODO[#339]: Architectural solution to make this work faster.
    private readonly IReadOnlyDictionary<string, Func<SourceSpan, TexFormula?>> predefinedFormulas;

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

    internal static string GetDelimeterMapping(char character)
    {
        try
        {
            return delimeters[character];
        }
        catch (KeyNotFoundException)
        {
            throw new DelimiterMappingNotFoundException(character);
        }
    }

    internal static SymbolAtom? GetDelimiterSymbol(string? name, SourceSpan? source)
    {
        if (name == null)
            return null;

        var result = SymbolAtom.GetAtom(name, source);
        if (!result.IsDelimeter)
            return null;
        return result;
    }

    private static bool IsSymbol(char c) => !char.IsLetterOrDigit(c);

    private static bool IsWhiteSpace(char ch)
        => ch is ' ' or '\t' or '\n' or '\r';

    private static bool ShouldSkipWhiteSpace(string? style) => style == null || !rawTextStyles.Contains(style);

    /// <summary>A registry for additional commands.</summary>
    private readonly IReadOnlyDictionary<string, IColorParser> _colorModelParsers;

    /// <summary>A color parser for cases when the color model isn't specified.</summary>
    private readonly IColorParser _defaultColorParser;

    private readonly IBrushFactory _brushFactory;

    internal TexFormulaParser(

        IReadOnlyDictionary<string, IColorParser> colorModelParsers,
        IColorParser defaultColorParser,
        IBrushFactory brushFactory,
        IReadOnlyDictionary<string, Func<SourceSpan, TexFormula?>> predefinedFormulae)
    {

        _colorModelParsers = colorModelParsers;
        _defaultColorParser = defaultColorParser;
        _brushFactory = brushFactory;
        predefinedFormulas = predefinedFormulae;
    }

    public TexFormulaParser(
        IBrushFactory brushFactory,
        IReadOnlyDictionary<string, Func<SourceSpan, TexFormula?>> predefinedFormulae) : this(
        StandardColorParsers.Dictionary,
        PredefinedColorParser.Instance,
        brushFactory,
        predefinedFormulae)
    { }

    /// <summary>
    /// A big operator, set the way this operator is set: <c>\sum</c> and <c>\prod</c> stack their limits
    /// in display style, an integral never does whatever the style, and that is why <c>\int_0^\infty</c>
    /// reads the way it does in every published paper.
    /// <para>
    /// Which operators are which is a fact about TeX, and it lives here. <see cref="TexFormulaBuilder"/>
    /// asks rather than deciding, for the same reason it asks about a character's class.
    /// </para>
    /// </summary>
    internal static BigOperatorAtom BigOperatorOf(SymbolAtom symbol, SourceSpan? source) =>
        new(source, symbol, null, null, sideLimitOperators.Contains(symbol.Name) ? false : (bool?)null);

    /// <summary>The delimiter this character stands for, or null when it stands for none.</summary>
    internal static SymbolAtom? DelimiterOf(char character, SourceSpan? source)
    {
        // `.` is how a fence is written with one end left open — \left. \right) — so no delimiter is
        // the right answer rather than a failure.
        if (character == '.') return null;

        try
        {
            return GetDelimiterSymbol(GetDelimeterMapping(character), source);
        }
        catch (DelimiterMappingNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The delimiter this command names, or null when it names none.</summary>
    internal static SymbolAtom? DelimiterOf(string name, SourceSpan? source)
    {
        try
        {
            return GetDelimiterSymbol(name, source);
        }
        catch (SymbolNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// What each command expanded to, worked out once. <c>\,</c> is in a fifth of the formulas anyone
    /// writes and always means the same thing, so re-reading its definition for every one of them is
    /// work done a hundred thousand times to get the same answer.
    /// </summary>
    private readonly ConcurrentDictionary<string, Atom?> _expansions = new();

    /// <summary>
    /// The atom this command is shorthand for, when it is shorthand for exactly one — <c>\,</c> and the
    /// rest of TeX's written-down spaces.
    /// <para>
    /// These are macros, not commands: <c>\quad</c> is defined as a formula in
    /// <c>PredefinedTexFormulas.xml</c> and parsed from that definition — text nobody wrote. That used to
    /// matter, because the atoms came back carrying offsets into it, and an expansion of more than one
    /// atom would have put a point in the layout naming a document the reader has never seen. Nothing
    /// carries an offset now: what an atom came from is its <see cref="Atom.Origin"/>, and an expansion
    /// has none until whoever asked for it hangs one on.
    /// </para>
    /// <para>
    /// Which is what lets an expansion be more than one atom. <c>\cdots</c> is three dots and <c>\neq</c>
    /// is a slash over an equals; both used to be declined for fear of those offsets, and between them
    /// and the rest of their family that was some eighteen thousand formulas.
    /// </para>
    /// </summary>
    internal Atom? ExpansionOf(string command)
    {
        var expansion = _expansions.GetOrAdd(command, name =>
        {
            if (!predefinedFormulas.TryGetValue(name, out var factory)) return null;

            // The definition text stands in for the source, because the source is not this method's to
            // have and nothing that comes out of here keeps it anyway.
            return factory(new SourceSpan(name, name, 0, name.Length))?.RootAtom;
        });

        // A copy each time, never the one in the table: what comes back has a part hung on it by whoever
        // asked, and two formulas asking for the same space must not end up sharing one atom. Only the
        // root — what is under it is drawing and nothing points at it, so there is nothing to hang.
        return expansion is null ? null : expansion with { Source = null };
    }

    /// <summary>
    /// Whether anything here has a reading for a command at all — a command parser, a macro, a style or a
    /// symbol.
    ///
    /// <para>
    /// Asked by <see cref="TexFormulaBuilder"/> to tell two different things apart, both of which reach
    /// the same place in it. A command this knows but the builder has no drawing for — <c>\textrm</c>,
    /// <c>\bbox</c> — is a gap in the builder, and the reader should see their formula rather than a
    /// complaint about it. A command <em>nothing</em> knows is a mistake in what they typed, and saying so
    /// is the useful thing to do.
    /// </para>
    /// </summary>
    internal bool Knows(string command)
    {
        if (StandardCommands.Dictionary.ContainsKey(command)) return true;
        if (predefinedFormulas.ContainsKey(command)) return true;
        if (textStyles.Contains(command)) return true;
        if (embeddedCommands.Contains(command)) return true;

        try
        {
            SymbolAtom.GetAtom(command, null);
            return true;
        }
        catch (SymbolNotFoundException)
        {
            return false;
        }
    }

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

    /// <summary>
    /// One character, as the atom it is set as — which decides how much room is left around it.
    /// <para>
    /// The classification is the whole reason <see cref="TexFormulaBuilder"/> asks rather than deciding:
    /// TeX's spacing comes from what <em>class</em> an atom is, not from the space in the source, and the
    /// table saying a <c>+</c> is a binary operator and an <c>=</c> is a relation is here. Something
    /// building a formula out of its own reading knows which characters the writer wrote, and has no
    /// business knowing how they should be set.
    /// </para>
    /// </summary>
    internal static Atom CharacterOf(char character, SourceSpan? source, string? textStyle = null)
    {
        if (!IsSymbol(character) || textStyle == TexUtilities.TextStyleName)
            return new CharAtom(source, character, textStyle);

        var symbolName = symbols.ElementAtOrDefault(character);

        return string.IsNullOrEmpty(symbolName)
            ? new CharAtom(source, character, textStyle)
            : SymbolAtom.GetAtom(symbolName, source);
    }
}
