using System;
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

    /// <summary>
    /// What is left to read, as the part of the input to blame. At the very end there is nothing left, so
    /// the last character stands for it — an unclosed brace is the reader's problem wherever it opened.
    /// </summary>
    private static SourceSpan Rest(SourceSpan value, int position)
    {
        var at = Math.Max(0, Math.Min(position, value.Length));
        if (at < value.Length) return value.Segment(at, value.Length - at);
        return value.Length > 0 ? value.Segment(value.Length - 1, 1) : value;
    }

    /// <summary>The stretch a command occupies, as the part of the input to blame.</summary>
    private static SourceSpan Named(SourceSpan value, int start, int position)
    {
        var from = Math.Max(0, Math.Min(start, value.Length));
        return value.Segment(from, Math.Max(0, Math.Min(position - from, value.Length - from)));
    }

    private static bool IsSymbol(char c)
    {
        return !char.IsLetterOrDigit(c);
    }

    private static bool IsWhiteSpace(char ch)
        => ch is ' ' or '\t' or '\n' or '\r';

    private static bool ShouldSkipWhiteSpace(string? style) => style == null || !rawTextStyles.Contains(style);

    /// <summary>A registry for additional commands.</summary>
    private readonly IReadOnlyDictionary<string, ICommandParser> _commandRegistry;

    private readonly IReadOnlyDictionary<string, IColorParser> _colorModelParsers;

    /// <summary>A color parser for cases when the color model isn't specified.</summary>
    private readonly IColorParser _defaultColorParser;

    private readonly IBrushFactory _brushFactory;

    internal TexFormulaParser(
        IReadOnlyDictionary<string, ICommandParser> commandRegistry,
        IReadOnlyDictionary<string, IColorParser> colorModelParsers,
        IColorParser defaultColorParser,
        IBrushFactory brushFactory,
        IReadOnlyDictionary<string, Func<SourceSpan, TexFormula?>> predefinedFormulae)
    {
        _commandRegistry = commandRegistry;
        _colorModelParsers = colorModelParsers;
        _defaultColorParser = defaultColorParser;
        _brushFactory = brushFactory;
        predefinedFormulas = predefinedFormulae;
    }

    public TexFormulaParser(
        IReadOnlyDictionary<string, IColorParser> colorModelParsers,
        IColorParser defaultColorParser,
        IBrushFactory brushFactory,
        IReadOnlyDictionary<string, Func<SourceSpan, TexFormula?>> predefinedFormulae)
        : this(
            StandardCommands.Dictionary,
            colorModelParsers,
            defaultColorParser,
            brushFactory,
            predefinedFormulae)
    { }

    public TexFormulaParser(
        IBrushFactory brushFactory,
        IReadOnlyDictionary<string, Func<SourceSpan, TexFormula?>> predefinedFormulae) : this(
        StandardColorParsers.Dictionary,
        PredefinedColorParser.Instance,
        brushFactory,
        predefinedFormulae)
    { }

    public TexFormula Parse(string value, string? textStyle = null) =>
        Parse(new SourceSpan("User input", value, 0, value.Length), textStyle);

    public TexFormula Parse(SourceSpan value, string? textStyle = null)
    {
        var position = 0;
        return Parse(value, ref position, false, textStyle, DefaultCommandEnvironment.Instance);
    }

    /// <summary>
    /// Parses as much as it can, showing whatever it cannot read rather than giving up on the whole
    /// formula, and reporting each such stretch in <see cref="TexFormula.Diagnostics"/>.
    /// <para>
    /// For anything being edited this is what you want. Text under a caret is wrong far more often than
    /// it is right — every command is invalid until its last letter — and a formula that vanishes while
    /// you write it tells the reader nothing about where the trouble is. What comes back is a formula in
    /// the ordinary sense, drawn and laid out, with the parts that were not understood standing as the
    /// characters that were actually typed. Those parts carry no meaning: they were shown, not read, so
    /// anything working structurally should trust them no further than the diagnostics allow.
    /// </para>
    /// </summary>
    /// <param name="shownAsWritten">
    /// A stretch to set as the characters written rather than read as maths — see
    /// <see cref="ICommandEnvironment.ShownAsWritten"/>. Null reads all of it.
    /// </param>
    /// <param name="placeholders">
    /// Whether an empty argument or table cell is given a hole to stand in - see
    /// <see cref="ICommandEnvironment.Placeholders"/>. Off unless asked for: a hole is an editing
    /// affordance, and setting a formula to be read is what this library is mostly for.
    /// </param>
    public TexFormula ParseWithRecovery(
        SourceSpan value, string? textStyle = null, (int Start, int Length)? shownAsWritten = null,
        bool placeholders = false)
    {
        var environment = new RecoveringCommandEnvironment(shownAsWritten, placeholders);
        var position = 0;
        var formula = Parse(value, ref position, false, textStyle, environment);
        formula.Diagnostics = environment.Collected;
        return formula;
    }

    /// <inheritdoc cref="ParseWithRecovery(SourceSpan, string?, ValueTuple{int,int}?)"/>
    public TexFormula ParseWithRecovery(
        string value, string? textStyle = null, (int Start, int Length)? shownAsWritten = null,
        bool placeholders = false) =>
        ParseWithRecovery(
            new SourceSpan("User input", value, 0, value.Length), textStyle, shownAsWritten, placeholders);

    internal TexFormula Parse(SourceSpan value, string? textStyle, ICommandEnvironment environment)
    {
        int localPostion = 0;
        return Parse(value, ref localPostion, false, textStyle, environment);
    }

    private DelimiterInfo ParseUntilDelimiter(
        SourceSpan value,
        ref int position,
        string? textStyle,
        ICommandEnvironment environment)
    {
        var embeddedFormula = Parse(value, ref position, true, textStyle, environment);
        if (embeddedFormula.RootAtom == null)
            throw new TexParseException("Cannot find closing delimiter", Rest(value, position));

        var source = embeddedFormula.RootAtom.Source;
        var bodyRow = embeddedFormula.RootAtom as RowAtom;
        var lastAtom = bodyRow?.Elements.LastOrDefault() ?? embeddedFormula.RootAtom;
        var lastDelimiter = lastAtom as SymbolAtom;
        if (lastDelimiter == null || !lastDelimiter.IsDelimeter)
            throw new TexParseException($"Cannot find closing delimiter; got {lastDelimiter} instead", Rest(value, position));

        Atom bodyAtom = CreateForRow(bodyRow, source);

        return new DelimiterInfo(bodyAtom, lastDelimiter);
    }

    private TexFormula Parse(
        SourceSpan value,
        ref int position,
        bool allowClosingDelimiter,
        string? textStyle,
        ICommandEnvironment environment)
    {
        var formula = new TexFormula { Source = value, TextStyle = textStyle };
        var closedDelimiter = false;
        var skipWhiteSpace = ShouldSkipWhiteSpace(textStyle);
        var initialPosition = position;
        while (position < value.Length && !(allowClosingDelimiter && closedDelimiter))
        {
            var resumeFrom = position;
            try
            {
            // Asked for as written, so it is set as written — the same treatment recovery gives input it
            // could not read, before anything tries to read this. Checked as "covers here" rather than
            // "starts here" so a stretch whose first character the parser stepped over on its way in is
            // still shown from wherever reading actually resumed, instead of quietly typesetting.
            if (Shown(environment, value, position) is { } written)
            {
                var shownAtom = ConvertRawText(written, TexUtilities.TextStyleName).RootAtom;
                position += written.Length;
                if (shownAtom is not null)
                    formula.Add(shownAtom, value.Segment(initialPosition, position - initialPosition));
                continue;
            }

            char ch = value[position];
            var source = value.Segment(position, 1);
            if (IsWhiteSpace(ch))
            {
                if (!skipWhiteSpace)
                {
                    // The second argument is the span of the *row* being built, not of the atom being
                    // added to it. Passing the space's own span gave the whole row that span instead, so
                    // the row claimed a single character while holding everything parsed so far — and
                    // every one of its children then named text the row did not contain.
                    formula.Add(new SpaceAtom(source), value.Segment(initialPosition, position + 1 - initialPosition));
                }

                position++;
            }
            else if (ch == escapeChar)
            {
                ProcessEscapeSequence(
                    formula,
                    value,
                    ref position,
                    allowClosingDelimiter,
                    ref closedDelimiter,
                    environment,
                    initialPosition);
            }
            else if (ch == leftGroupChar)
            {
                ProcessLeftGroupChar(
                    value,
                    ref position,
                    textStyle,
                    environment,
                    formula,
                    initialPosition);
            }
            else if (ch == rightGroupChar)
            {
                throw new TexParseException("Found a closing '" + rightGroupChar
                    + "' without an opening '" + leftGroupChar + "'!", source);
            }
            else if (ch == superScriptChar || ch == subScriptChar || ch == primeChar)
            {
                if (position == 0)
                    throw new TexParseException("Every script needs a base: \""
                        + superScriptChar + "\", \"" + subScriptChar + "\" and \""
                        + primeChar + "\" can't be the first character!", source);
                else
                {
                    // An empty base, standing where the script does. Handing it the whole of `value`
                    // instead made it claim every character from the start of the input, which the
                    // script's own span is then taken from — so a nested `\left(…\right)^{2}` reported a
                    // row reaching back outside the fence holding it.
                    var scriptsAtom = this.AttachScripts(
                        formula, value, ref position, new RowAtom(value.Segment(position, 0)), true, environment);
                    formula.Add(scriptsAtom, value.Segment(initialPosition, position - initialPosition));
                }
            }
            else if (ch == tieChar)
            {
                // '~' is a tie: a non-breaking inter-word space. As above, the row's span rather than the
                // tie's own — this is the one that made `\mathrm { ~ f o r ~ }` claim a single `~`.
                formula.Add(new SpaceAtom(source), value.Segment(initialPosition, position + 1 - initialPosition));
                position++;
            }
            else
            {
                var character = ConvertCharacter(formula, ref position, source, environment);
                if (character != null)
                {
                    var scriptsAtom = AttachScripts(
                        formula,
                        value,
                        ref position,
                        character,
                        skipWhiteSpace,
                        environment);
                    formula.Add(scriptsAtom, value.Segment(initialPosition, position - initialPosition));
                }
            }
            }
            catch (TexParseException error) when (environment.Diagnostics is not null)
            {
                position = Recover(formula, value, resumeFrom, initialPosition, error, environment.Diagnostics);
            }
        }

        return formula;
    }

    /// <summary>
    /// The part of <see cref="ICommandEnvironment.ShownAsWritten"/> that starts here, or null when
    /// nothing is being written at this position. Clipped to the span being parsed, so a stretch running
    /// past the end of a group is shown as far as the group goes and the rest is met again outside it.
    /// </summary>
    private static SourceSpan? Shown(ICommandEnvironment environment, SourceSpan value, int position)
    {
        if (environment.ShownAsWritten is not { Length: > 0 } zone) return null;

        var here = value.Start + position;
        if (here < zone.Start || here >= zone.Start + zone.Length) return null;

        var length = System.Math.Min(zone.Start + zone.Length - here, value.Length - position);
        return length > 0 ? value.Segment(position, length) : null;
    }

    /// <summary>
    /// Gives up on the stretch the parser could not read, shows it as written, and carries on after it.
    /// </summary>
    /// <remarks>
    /// How far to give up is the whole question, and it is what the position on the exception is for: the
    /// parser knows which characters defeated it far better than anything downstream could guess. Failing
    /// that — a fault that named nothing — the rest of the input goes, because there is no way to tell
    /// where the trouble ends. Either way it must move at least one character, or the loop that called it
    /// would meet the same fault forever.
    /// </remarks>
    private static int Recover(
        TexFormula formula,
        SourceSpan value,
        int from,
        int rowStart,
        TexParseException error,
        ICollection<TexParseDiagnostic> diagnostics)
    {
        var blamed = error.At is { } at
                     && string.Equals(at.Source, value.Source, StringComparison.Ordinal)
                     && at.End > value.Start + from
            ? at.End - value.Start
            : value.Length;

        var to = Math.Max(from + 1, Math.Min(Math.Max(blamed, from + 1), value.Length));
        var unread = value.Segment(from, to - from);
        diagnostics.Add(new TexParseDiagnostic(error.Message, unread));

        // Shown as the characters the reader wrote, so the formula keeps its shape around the hole rather
        // than disappearing. Deliberately not interpreted — this is the part that could not be.
        var shown = ConvertRawText(unread, TexUtilities.TextStyleName).RootAtom;
        if (shown is not null) formula.Add(shown, value.Segment(rowStart, to - rowStart));

        return to;
    }

    private void ProcessLeftGroupChar(SourceSpan value, ref int position, string? textStyle, ICommandEnvironment environment, TexFormula formula, int initialPosition)
    {
        var afterGroup = ReadElement(value, position);
        position = afterGroup.position;
        var groupValue = afterGroup.source;
        var parsedGroup = Parse(groupValue, textStyle, environment.CreateChildEnvironment());
        var innerGroupAtom = parsedGroup.RootAtom ?? new RowAtom(groupValue);
        var groupAtom = new TypedAtom(
            innerGroupAtom.Source,
            innerGroupAtom,
            TexAtomType.Ordinary,
            TexAtomType.Ordinary);
        var scriptsAtom = this.AttachScripts(formula, value, ref position, groupAtom, true, environment);
        formula.Add(scriptsAtom, value.Segment(initialPosition, position - initialPosition));
    }

    private static TexFormula ConvertRawText(SourceSpan value, string textStyle)
    {
        var formula = new TexFormula { Source = value, TextStyle = textStyle };

        var position = 0;
        var initialPosition = position;
        while (position < value.Length)
        {
            var ch = value[position];
            var source = value.Segment(position, 1);
            var atom = IsWhiteSpace(ch)
                ? (Atom)new SpaceAtom(source)
                : new CharAtom(source, ch, textStyle);
            position++;
            formula.Add(atom, value.Segment(initialPosition, position - initialPosition));
        }

        return formula;
    }

    /// <summary>Reads a char-delimited element group if it exists; returns <c>null</c> if it isn't.</summary>
    internal static SourceSpan? ReadElementGroupOptional(
        SourceSpan value,
        ref int position,
        char openChar,
        char closeChar)
    {
        position = WithSkippedWhiteSpace(value, position);
        if (position == value.Length || value[position] != openChar)
            return null;

        var afterGroupRead = ReadElementGroup(value, position, openChar, closeChar);
        position = afterGroupRead.position;
        return afterGroupRead.source;
    }

    internal static SymbolAtom ParseDelimiter(SourceSpan value, int start, ref int position)
    {
        var afterDelimiter = ReadElement(value, position);
        position = afterDelimiter.position;
        var delimiterSource = value.Segment(start, position - start); // maps the whole "\left(" to the delimiter atom
        return GetDelimiterAtom(afterDelimiter.source, delimiterSource);
    }

    /// <summary>
    /// Resolves the text of a delimiter argument - <c>(</c>, <c>\{</c>, <c>\lVert</c> - to its symbol.
    /// </summary>
    /// <param name="delimiter">The argument itself.</param>
    /// <param name="delimiterSource">What to record as the resulting atom's source.</param>
    internal static SymbolAtom GetDelimiterAtom(SourceSpan delimiter, SourceSpan delimiterSource)
    {
        string delimiterName;
        if (delimiter.Length == 1)
            delimiterName = GetDelimeterMapping(delimiter[0]);
        else
        {
            if (delimiter[0] != escapeChar)
                throw new TexParseException($"A delimiter should start from {escapeChar}, but got {delimiter}", delimiter);

            // Here goes the fancy business: for non-alphanumeric commands (e.g. \{, \\ etc.) we need to pass them
            // through GetDelimeterMapping, but for alphanumeric ones, we don't.
            delimiterName = delimiter.Segment(1).ToString(); // skip an escape character
            if (delimiterName.Length == 1 && !char.IsLetterOrDigit(delimiterName[0]))
                delimiterName = GetDelimeterMapping(delimiterName[0]);
        }

        if (delimiterName == null || !SymbolAtom.TryGetAtom(delimiterName, delimiterSource, out var atom) || !atom.IsDelimeter)
            throw new TexParseException($"Cannot find delimiter {delimiter}", delimiter);

        return atom;
    }

    /// <summary>
    /// Reads an element: typically, a curly brace-enclosed value group, a singular value or a character sequence
    /// prefixed by a backslash.
    /// </summary>
    /// <exception cref="TexParseException">Will be thrown for ill-formed groups.</exception>
    internal static AfterReadingInfo ReadElement(SourceSpan value, int position)
    {
        position = WithSkippedWhiteSpace(value, position);

        if (position == value.Length)
            throw new TexParseException("An element is missing", Rest(value, position));

        switch (value[position])
        {
            case leftGroupChar:
                var afterGroupRead = ReadElementGroup(value, position, leftGroupChar, rightGroupChar);
                position = afterGroupRead.position;
                return new(afterGroupRead.source, position);
            case escapeChar:
                var afterSequenceRead = ReadEscapeSequence(value, position);
                position = afterSequenceRead.position;
                return new(afterSequenceRead.source, position);
            default:
                return new(value.Segment(position++, 1), position);
        }
    }

    private TexFormula ReadScript(
        TexFormula formula,
        SourceSpan value,
        ref int position,
        ICommandEnvironment environment)
    {
        var start = WithSkippedWhiteSpace(value, position);
        var afterScript = ReadElement(value, position);

        // A font command as a script takes its argument with it. TeX reads a single token after ^ or _,
        // and the macro then grabs its own argument, so `m_\mathrm{el}` sets "el" in roman - whereas
        // reading only the token leaves \mathrm as the whole script with its argument stranded after
        // it, and a \mathrm with nothing to style is an error.
        if (afterScript.source.Length > 1
            && afterScript.source[0] == escapeChar
            && textStyles.Contains(afterScript.source.Segment(1).ToString()))
        {
            var afterArgument = ReadElement(value, afterScript.position);
            afterScript = new AfterReadingInfo(
                value.Segment(start, afterArgument.position - start),
                afterArgument.position);
        }

        position = afterScript.position;
        return WithPlaceholderIfEmpty(
            Parse(afterScript.source, formula.TextStyle, environment.CreateChildEnvironment()),
            value.Segment(start, position - start),
            environment);
    }

    /// <remarks>May return <c>null</c> for commands that produce no atoms.</remarks>
    private Tuple<AtomAppendMode, Atom?> ProcessCommand(
        TexFormula formula,
        SourceSpan value,
        ref int position,
        string command,
        bool allowClosingDelimiter,
        ref bool closedDelimiter,
        ICommandEnvironment environment)
    {
        int start = position - command.Length;

        SourceSpan source;
        switch (command)
        {
            case "frac":
                {
                    var numeratorFormula = ReadArgumentFormula(formula, value, ref position, environment);
                    var denominatorFormula = ReadArgumentFormula(formula, value, ref position, environment);
                    source = value.Segment(start, position - start);
                    return new Tuple<AtomAppendMode, Atom?>(
                        AtomAppendMode.Add,
                        new FractionAtom(
                            source,
                            numeratorFormula.RootAtom,
                            denominatorFormula.RootAtom,
                            true));
                }
            case "left":
                {
                    position = WithSkippedWhiteSpace(value, position);
                    if (position == value.Length)
                        throw new TexParseException("`left` command should be passed a delimiter", Rest(value, start));

                    var opening = ParseDelimiter(value, start, ref position);
                    var internals = ParseUntilDelimiter(value, ref position, formula.TextStyle, environment);
                    var closing = internals.ClosingDelimiter;
                    source = value.Segment(start, position - start);
                    return new Tuple<AtomAppendMode, Atom?>(
                        AtomAppendMode.Add,
                        new FencedAtom(source, internals.Body, opening, closing));
                }
            case "overline":
                {
                    var afterOverline = ReadElement(value, position);
                    position = afterOverline.position;
                    var overlineFormula = Parse(
                        afterOverline.source,
                        formula.TextStyle,
                        environment.CreateChildEnvironment());
                    source = value.Segment(start, position - start);
                    return new Tuple<AtomAppendMode, Atom?>(
                        AtomAppendMode.Add,
                        new OverlinedAtom(source, overlineFormula.RootAtom));
                }
            case "right":
                {
                    if (!allowClosingDelimiter)
                        throw new TexParseException("`right` command is not allowed without `left`", Named(value, start, position));

                    position = WithSkippedWhiteSpace(value, position);
                    if (position == value.Length)
                        throw new TexParseException("`right` command should be passed a delimiter", Rest(value, start));

                    var closing = ParseDelimiter(value, start, ref position);

                    closedDelimiter = true;
                    return new Tuple<AtomAppendMode, Atom?>(AtomAppendMode.Add, closing);
                }
            case "sqrt":
                {
                    // Command is radical.
                    position = WithSkippedWhiteSpace(value, position);

                    TexFormula? degreeFormula = null;
                    if (value.Length > position && value[position] == leftBracketChar)
                    {
                        // Degree of radical is specified.

                        var afterGroupRead = ReadElementGroup(value, position, leftBracketChar, rightBracketChar);
                        position = afterGroupRead.position;

                        degreeFormula = Parse(
                            afterGroupRead.source,
                            formula.TextStyle,
                            environment.CreateChildEnvironment());
                    }

                    var sqrtFormula = ReadArgumentFormula(formula, value, ref position, environment);

                    source = value.Segment(start, position - start);
                    return new Tuple<AtomAppendMode, Atom?>(
                        AtomAppendMode.Add,
                        new Radical(source, sqrtFormula.RootAtom ?? new NullAtom(), degreeFormula?.RootAtom));
                }
            case "color":
            case "textcolor":
                {
                    var color = ReadColorModelData(value, ref position);

                    var afterValue = ReadElement(value, position);
                    position = afterValue.position;
                    var bodyValue = afterValue.source;
                    var bodyFormula = Parse(bodyValue, formula.TextStyle, environment.CreateChildEnvironment());
                    source = value.Segment(start, position - start);

                    return new Tuple<AtomAppendMode, Atom?>(
                        AtomAppendMode.Add,
                        new StyledAtom(source, bodyFormula.RootAtom, null, _brushFactory.FromColor(color)));
                }
            case "colorbox":
                {
                    var color = ReadColorModelData(value, ref position);

                    var afterValue = ReadElement(value, position);
                    position = afterValue.position;
                    var bodyValue = afterValue.source;
                    var bodyFormula = Parse(bodyValue, formula.TextStyle, environment.CreateChildEnvironment());
                    source = value.Segment(start, position - start);

                    return new Tuple<AtomAppendMode, Atom?>(
                        AtomAppendMode.Add,
                        new StyledAtom(source, bodyFormula.RootAtom, _brushFactory.FromColor(color), null));
                }
        }

        if (environment.AvailableCommands.TryGetValue(command, out var parser)
            || _commandRegistry.TryGetValue(command, out parser))
        {
            var context = new CommandContext(this, formula, environment, value, start, position);
            var parseResult = parser.ProcessCommand(context);
            if (parseResult.NextPosition < position)
                throw new TexParseException(
                    $"Incorrect parser behavior for command {command}: NextPosition = {parseResult.NextPosition}, position = {position}. Parser did not made any progress.",
                    Named(value, start, position));

            position = parseResult.NextPosition;
            return Tuple.Create(parseResult.AppendMode, parseResult.Atom);
        }

        throw new TexParseException("Invalid command.", Named(value, start, position));
    }

    /// <summary>Reads an optional square braced color model name, and then a color name.</summary>
    /// <returns>Returns a color parsed.</returns>
    /// <exception cref="TexParseException">Gets thrown in case of nonexistent color model or color.</exception>
    private RgbaColor ReadColorModelData(SourceSpan value, ref int position)
    {
        var colorModelName = ReadElementGroupOptional(
            value,
            ref position,
            leftBracketChar,
            rightBracketChar)?.ToString();
        var afterColor = ReadElement(value, position);
        position = afterColor.position;
        var colorDefinition = afterColor.source.ToString();
        var colorComponents = colorDefinition.Split(',').Select(c => c.Trim()).ToArray();

        var colorParser = string.IsNullOrEmpty(colorModelName)
            ? _defaultColorParser
            : _colorModelParsers.TryGetValue(colorModelName, out var currentColorParser)
                ? currentColorParser
                : throw new TexParseException($"Unknown color model name: {colorModelName}", afterColor.source);

        var color = colorParser.Parse(colorComponents);
        if (color == null)
            throw new TexParseException(
                $"Color {colorDefinition} could not be parsed by the {colorModelName ?? "default"} color model.",
                afterColor.source);

        return color.Value;
    }

    private void ProcessEscapeSequence(TexFormula formula,
        SourceSpan value,
        ref int position,
        bool allowClosingDelimiter,
        ref bool closedDelimiter,
        ICommandEnvironment environment,
        int rowStart)
    {
        // A row spans everything parsed into it so far. Passing only the command's own span here gave
        // the whole row that span instead, so a subscript such as {x 	o infty} reported itself as
        // infty — the row and its last element claiming the same characters at different places.
        SourceSpan RowSource(int at) => value.Segment(rowStart, at - rowStart);

        var initialSrcPosition = position;
        var afterEscapeRead = ReadEscapeSequence(value, position);
        position = afterEscapeRead.position;
        var commandSpan = afterEscapeRead.source.Segment(1);
        var command = commandSpan.ToString();
        // SourceSpan's fourth argument is a length, and commandSpan.End is an absolute offset.
        var commandStart = value.Start + initialSrcPosition;
        var formulaSource = new SourceSpan(
            value.SourceName, value.Source, commandStart, commandSpan.End - commandStart);

        if (SymbolAtom.TryGetAtom(commandSpan, out SymbolAtom? symbolAtom))
        {
            // Symbol was found.

            if (symbolAtom.Type == TexAtomType.Accent)
            {
                TexFormula accentFormula = ReadScript(formula, value, ref position, environment);

                // A script after an accent belongs to the accented atom: \dot{C}^\mu is a superscript
                // on the accented C, so it clears the dot. Without attaching it here the script falls
                // through to the parser's "no base to hand" path, which hangs it off an empty box and
                // sets it at the height of nothing at all.
                // Spanning the argument as well, now that it has been read. `formulaSource` covers the
                // command alone, which was set before there was an argument to include — so `\vec{F}`
                // claimed the four characters of `\vec` while drawing all seven, and a node ended up
                // naming less than the child inside it.
                Atom accented = new AccentedAtom(
                    value.Segment(initialSrcPosition, position - initialSrcPosition),
                    accentFormula.RootAtom,
                    symbolAtom.Name);
                formula.Add(
                    AttachScripts(formula, value, ref position, accented, true, environment),
                    RowSource(position));
            }
            else if (symbolAtom.Type == TexAtomType.BigOperator)
            {
                // \sum and \prod stack their limits in display style; an integral never does, whatever
                // the style, which is why \int_0^\infty reads the way it does in every paper. \limits
                // is there for anyone who wants the other.
                var limits = sideLimitOperators.Contains(symbolAtom.Name) ? false : (bool?)null;
                var opAtom = new BigOperatorAtom(formulaSource, symbolAtom, null, null, limits);
                formula.Add(AttachScripts(formula, value, ref position, opAtom, true, environment), RowSource(position));
            }
            else
            {
                formula.Add(
                    AttachScripts(formula, value, ref position, symbolAtom, true, environment), RowSource(position));
            }
        }
        else if (predefinedFormulas.TryGetValue(command, out var factory))
        {
            // Predefined formula was found.
            var predefinedFormula = factory(formulaSource);
            // Re-source its root onto the command as written in the input. A predefined formula is parsed
            // from its own definition text, so its atoms carry offsets into that string; left alone, \sin,
            // \lim, \sup and the multiple integrals are attributable to no part of the input they came
            // from, which makes them invisible to anything mapping rendered output back to source.
            Atom root = predefinedFormula!.RootAtom! with { Source = formulaSource }; // Nullable TODO: This might need null checking

            // The multiple integrals are built out of \int rather than being symbols of their own, so
            // they have to be told where their limits go in the same breath.
            if (sideLimitOperators.Contains(command))
                root = new BigOperatorAtom(formulaSource, root, null, null, false);

            var atom = AttachScripts(formula, value, ref position, root, true, environment);
            formula.Add(atom, RowSource(position));
        }
        else if (command.Equals("nbsp") || command.Equals(" "))
        {
            // A space was found: '\nbsp', or the control space '\ ' (a normal inter-word space).
            var atom = AttachScripts(formula, value, ref position, new SpaceAtom(formulaSource), true, environment);
            formula.Add(atom, RowSource(position));
        }
        else if (textStyles.Contains(command))
        {
            // Text style was found.
            position = WithSkippedWhiteSpace(value, position);

            var afterRead = ReadElement(value, position);

            position = afterRead.position;

            // \mbox is \text spelled differently - LaTeX's box-making side of it has no meaning
            // here, where there is no line breaking to protect the contents from.
            var styleName = command == "mbox" ? TexUtilities.TextStyleName : command;

            TexFormula styledFormula =
                rawTextStyles.Contains(command)
                ? ConvertRawText(afterRead.source, styleName)
                : Parse(afterRead.source, styleName, environment.CreateChildEnvironment());

            // From where the command began, as an index into `value`. `commandSpan.Start` is an offset
            // into the whole input, and Segment adds `value.Start` to whatever it is given, so passing it
            // counted the base twice — a `\mathrm` nested in anything reported a stretch of source well
            // before itself.
            var source = value.Segment(initialSrcPosition, position - initialSrcPosition);
            var atom = styledFormula.RootAtom ?? new NullAtom(source);
            var commandAtom = AttachScripts(formula, value, ref position, atom, true, environment);
            formula.Add(commandAtom, RowSource(position));
        }
        else if (embeddedCommands.Contains(command)
             || environment.AvailableCommands.ContainsKey(command)
             || _commandRegistry.ContainsKey(command))
        {
            // Command was found.
            var (appendMode, commandAtom) = ProcessCommand(
                formula,
                value,
                ref position,
                command,
                allowClosingDelimiter,
                ref closedDelimiter,
                environment);

            if (commandAtom != null)
            {
                commandAtom = allowClosingDelimiter
                    ? commandAtom
                    : AttachScripts(
                        formula,
                        value,
                        ref position,
                        commandAtom,
                        true,
                        environment);

                // A command may build its result by parsing a body it synthesised — \pmod and \bmod do —
                // in which case its atoms carry offsets into *that* body, not into the input. Its own end
                // offset is only meaningful when it came from the same text; otherwise the parse position
                // is what says where the command finished.
                var fromThisText = string.Equals(
                    commandAtom.Source?.Source, value.Source, StringComparison.Ordinal);

                // As above: a length is wanted here, and Source.End is an absolute offset.
                var commandEnd = fromThisText ? commandAtom.Source!.End : position;
                var source = new SourceSpan(
                    formulaSource.SourceName,
                    formulaSource.Source,
                    formulaSource.Start,
                    commandEnd - formulaSource.Start);

                // …and give such a result the span of the command as written, so that what it draws is
                // attributable to the characters that asked for it rather than to nothing at all.
                if (!fromThisText) commandAtom = commandAtom with { Source = source };
                switch (appendMode)
                {
                    case AtomAppendMode.Add:
                        formula.Add(commandAtom, RowSource(position));
                        break;
                    case AtomAppendMode.Replace:
                        formula.RootAtom = commandAtom;
                        break;
                }
            }
        }
        else
        {
            // Escape sequence is invalid.
            throw new TexParseException(
                "Unknown symbol or command or predefined TeXFormula: '" + command + "'",
                afterEscapeRead.source);
        }
    }

    private Atom AttachScripts(
        TexFormula formula,
        SourceSpan value,
        ref int position,
        Atom atom,
        bool skipWhiteSpace,
        ICommandEnvironment environment)
    {
        if (skipWhiteSpace)
        {
            position = WithSkippedWhiteSpace(value, position);
        }

        var initialPosition = position;
        if (position == value.Length)
            return atom;

        // The limit controls follow the operator whose scripts they place. Only an operator can
        // carry them, so anywhere else they stay unknown commands rather than being swallowed.
        if (atom.GetRightType() == TexAtomType.BigOperator)
            atom = ReadLimitControls(value, ref position, atom);

        // Check for prime marks.
        var primesStart = position;
        var primesRowAtom = new RowAtom(value.Segment(primesStart, 0));
        int i = position;
        while (i < value.Length)
        {
            if (value[i] == primeChar)
            {
                primesRowAtom = primesRowAtom.Add(SymbolAtom.GetAtom("prime", value.Segment(i, 1)));
                position++;
            }
            else if (!IsWhiteSpace(value[i]))
                break;
            i++;
        }

        primesRowAtom = primesRowAtom with { Source = value.Segment(primesStart, position - primesStart) };

        if (primesRowAtom.Elements.Count > 0)
        {
            // From where the base began, as the scripts below do. Given the primes' own span instead, the
            // atom that draws `f''` claimed only the `''` — so the f inside it named a character its own
            // container did not.
            var primed = BaseStartWithin(atom, value, primesStart);
            atom = new ScriptsAtom(value.Segment(primed, position - primed), atom, null, primesRowAtom);
        }

        if (position == value.Length)
            return atom;

        TexFormula? superscriptFormula = null;
        TexFormula? subscriptFormula = null;

        var ch = value[position];
        if (ch == superScriptChar)
        {
            // Attach superscript.
            position++;
            superscriptFormula = ReadScript(formula, value, ref position, environment);

            position = WithSkippedWhiteSpace(value, position);
            if (position < value.Length && value[position] == subScriptChar)
            {
                // Attach subscript also.
                position++;
                subscriptFormula = ReadScript(formula, value, ref position, environment);
            }
        }
        else if (ch == subScriptChar)
        {
            // Add subscript.
            position++;
            subscriptFormula = ReadScript(formula, value, ref position, environment);

            position = WithSkippedWhiteSpace(value, position);
            if (position < value.Length && value[position] == superScriptChar)
            {
                // Attach superscript also.
                position++;
                superscriptFormula = ReadScript(formula, value, ref position, environment);
            }
        }

        if (superscriptFormula == null && subscriptFormula == null)
            return atom;

        // Check whether to return Big Operator or Scripts.
        var subscriptAtom = subscriptFormula?.RootAtom;
        var superscriptAtom = superscriptFormula?.RootAtom;

        // Either kind draws its base as well as its scripts, so the span has to start where the base did.
        // `position` and `initialPosition` index into `value`; an atom's Source.Start is an offset into
        // the whole input, so it must be brought back into value's frame before it can be used as one.
        // Confusing the two is invisible at the top level, where value starts at zero, and wrong for every
        // nested construct: a numerator's `x^2` reported the offsets of `\fr`.
        var baseStart = BaseStartWithin(atom, value, initialPosition);
        var source = value.Segment(baseStart, position - baseStart);

        if (atom.GetRightType() == TexAtomType.BigOperator)
        {
            if (atom is BigOperatorAtom typedAtom)
            {
                return new BigOperatorAtom(
                    source,
                    typedAtom.BaseAtom,
                    subscriptAtom,
                    superscriptAtom,
                    typedAtom.UseVerticalLimits);
            }

            return new BigOperatorAtom(source, atom, subscriptAtom, superscriptAtom);
        }

        return new ScriptsAtom(source, atom, subscriptAtom, superscriptAtom);
    }

    /// <summary>
    /// Where <paramref name="atom"/> begins as an index into <paramref name="value"/>, falling back to
    /// <paramref name="fallback"/> when it came from somewhere else — the body of a macro, say, whose
    /// offsets say nothing about this text.
    /// </summary>
    private static int BaseStartWithin(Atom atom, SourceSpan value, int fallback) =>
        atom.Source is { } source
        && string.Equals(source.Source, value.Source, StringComparison.Ordinal)
        && source.Start >= value.Start
        && source.Start <= value.Start + value.Length
            ? source.Start - value.Start
            : fallback;

    /// <summary>
    /// Reads the run of <c>\limits</c>, <c>\nolimits</c> and <c>\displaylimits</c> that may follow an
    /// operator, and hands the operator back carrying the placement the last of them asked for.
    /// </summary>
    private static Atom ReadLimitControls(SourceSpan value, ref int position, Atom atom)
    {
        while (true)
        {
            var start = WithSkippedWhiteSpace(value, position);
            if (start + 1 >= value.Length || value[start] != escapeChar)
                return atom;

            var afterCommand = ReadEscapeSequence(value, start);
            bool? useVerticalLimits;
            switch (afterCommand.source.Segment(1).ToString())
            {
                case "limits": useVerticalLimits = true; break;
                case "nolimits": useVerticalLimits = false; break;
                // \displaylimits asks for whatever the current style would have chosen anyway.
                case "displaylimits": useVerticalLimits = null; break;
                default: return atom;
            }

            // TeX lets them pile up and the last one wins, so keep reading.
            position = afterCommand.position;
            atom = atom is BigOperatorAtom bigOperator
                ? bigOperator with { UseVerticalLimits = useVerticalLimits }
                : new BigOperatorAtom(atom.Source, atom, null, null, useVerticalLimits);
        }
    }

    /// <remarks>May return <c>null</c>.</remarks>
    private static Atom? ConvertCharacter(
        TexFormula formula,
        ref int position,
        SourceSpan source,
        ICommandEnvironment environment)
    {
        var character = source[0];
        position++;
        if (IsSymbol(character) && formula.TextStyle != TexUtilities.TextStyleName)
        {
            // Character is symbol.
            var symbolName = symbols.ElementAtOrDefault(character);
            if (string.IsNullOrEmpty(symbolName))
            {
                if (environment.ProcessUnknownCharacter(formula, character, source))
                    return null;

                throw new TexParseException($"Unknown character : '{character}'", source);
            }

            try
            {
                return SymbolAtom.GetAtom(symbolName, source);
            }
            catch (SymbolNotFoundException e)
            {
                throw new TexParseException("The character '"
                        + character.ToString()
                        + "' was mapped to an unknown symbol with the name '"
                        + (string)symbolName + "'!", source, e);
            }
        }
        else // Character is alpha-numeric or should be rendered as text.
        {
            return new CharAtom(source, character, formula.TextStyle);
        }
    }

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
    internal static Atom CharacterOf(char character, SourceSpan source, string? textStyle = null)
    {
        if (!IsSymbol(character) || textStyle == TexUtilities.TextStyleName)
            return new CharAtom(source, character, textStyle);

        var symbolName = symbols.ElementAtOrDefault(character);

        return string.IsNullOrEmpty(symbolName)
            ? new CharAtom(source, character, textStyle)
            : SymbolAtom.GetAtom(symbolName, source);
    }

    internal readonly record struct AfterReadingInfo(SourceSpan source, int position);

    private static AfterReadingInfo ReadEscapeSequence(SourceSpan value, int position)
    {
        var initialPosition = position;
        if (value[initialPosition] != escapeChar)
            throw new Exception($"Invalid state: {nameof(ReadEscapeSequence)} called for a value without escape character ({value})");

        position++;
        var start = position;
        while (position < value.Length)
        {
            var ch = value[position];
            var isEnd = position == value.Length - 1;
            if (!char.IsLetter(ch) || isEnd)
            {
                // Escape sequence has ended
                // Or it's a symbol. Assuming in this case it will only be a single char.
                if ((isEnd && char.IsLetter(ch)) || position - start == 0)
                {
                    position++;
                }
                break;
            }

            position++;
        }

        var length = position - initialPosition;
        if (length <= 1)
            throw new TexParseException($"Unfinished escape sequence (value: \"{value}\", index {position})", Rest(value, initialPosition));

        return new(
            value.Segment(initialPosition, length),
            position);
    }

    private static AfterReadingInfo ReadElementGroup(
        SourceSpan value,
        int position,
        char openChar,
        char closeChar)
    {
        if (position == value.Length || value[position] != openChar)
            throw new TexParseException("missing '" + openChar + "'!", Rest(value, position));

        var group = 0;
        position++;
        var start = position;
        while (position < value.Length && !(value[position] == closeChar && group == 0))
        {
            // An escaped brace is a character, not a nesting level: the { of \{ opens nothing and
            // the } of \} closes nothing, so the escape carries its follower past the count.
            if (value[position] == escapeChar && position + 1 < value.Length)
                position++;
            else if (value[position] == openChar)
                group++;
            else if (value[position] == closeChar)
                group--;
            position++;
        }

        if (position == value.Length)
        {
            // Reached end of formula but group has not been closed.
            throw new TexParseException("Illegal end,  missing '" + closeChar + "'!", Rest(value, start - 1));
        }

        position++;

        return new(
            value.Segment(start, position - start - 1),
            position);
    }

    /// <returns>New position after space skipped</returns>
    /// <summary>
    /// An argument that parsed to nothing, standing in as a placeholder.
    /// <para>
    /// <c>\frac{}{}</c> is a fraction with two arguments; they are simply empty. Left as nothing it sets
    /// as a bar with two invisible sides — a formula a reader cannot see, cannot aim at and cannot tell
    /// from a broken one. A placeholder is a symbol in its place, so everything downstream that knows how
    /// to find, hit-test, select, carry or replace a symbol handles the hole without knowing it is one.
    /// </para>
    /// <para>
    /// It exists in the parse and never in the source, which is what makes it a hole rather than
    /// content: nothing the reader saves, copies or solves can carry it. And because an argument that
    /// has not been written is a formula that does not yet mean anything, each one is reported — a
    /// caller asking "can this be read" is told no, while the picture still draws.
    /// </para>
    /// </summary>
    /// <param name="parsed">The argument as it parsed.</param>
    /// <param name="at">The characters it was read from — the braces included, since they produced it.</param>
    /// <summary>
    /// Reads one <c>{…}</c> argument and parses it, making a hole of it when it was left empty — see
    /// <see cref="WithPlaceholderIfEmpty"/>. The built-in constructs read their arguments through this
    /// so that none of them has to know a hole is a thing.
    /// </summary>
    private TexFormula ReadArgumentFormula(
        TexFormula formula, SourceSpan value, ref int position, ICommandEnvironment environment)
    {
        var start = WithSkippedWhiteSpace(value, position);
        var after = ReadElement(value, position);
        position = after.position;

        return WithPlaceholderIfEmpty(
            Parse(after.source, formula.TextStyle, environment.CreateChildEnvironment()),
            value.Segment(start, position - start),
            environment);
    }

    internal static TexFormula WithPlaceholderIfEmpty(
        TexFormula parsed, SourceSpan at, ICommandEnvironment environment)
    {
        if (parsed.RootAtom is not null) return parsed;
        if (!environment.Placeholders) return parsed;

        // The placeholder's own span is empty, and sits where the argument's contents would have
        // begun. It stands for nothing that was written, so it covers nothing that was written — and
        // that is also what makes typing over it put the characters inside the braces rather than
        // instead of them. The report covers the braces, because a wave needs something to sit under.
        parsed.RootAtom = new PlaceholderAtom(at.Segment(at.Length > 0 ? 1 : 0, 0));
        environment.Diagnostics?.Add(new TexParseDiagnostic("Something still has to go here.", at));
        return parsed;
    }

    internal static int WithSkippedWhiteSpace(SourceSpan value, int position)
    {
        while (position < value.Length && IsWhiteSpace(value[position]))
            position++;

        return position;
    }

    private static Atom CreateForRow(RowAtom? bodyRow, SourceSpan? source)
    {
        if (bodyRow == null)
        {
            return new RowAtom(source);
        }
        else if (bodyRow.Elements.Count > 2)
        {
            return
                bodyRow.Elements
                .Take(bodyRow.Elements.Count - 1)
                .Aggregate(
                    new RowAtom(source),
                    (r, atom) => r.Add(atom)
                );
        }
        else if (bodyRow.Elements.Count == 2)
        {
            return bodyRow.Elements[0];
        }
        else
        {
            throw new NotSupportedException($"Cannot convert {bodyRow} to fenced atom body");
        }
    }
}
