using System.Globalization;
using System.Xml.Linq;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Latex;

/// <summary>
/// What the names of LaTeX mean: every symbol and its class, which character stands for which symbol, the faces, and what
/// each command and environment is — the words a formula is written in, as against how any of them is set.
///
/// <para>
/// Read by <see cref="Stages.ResolveCommands"/>, which says in the tree what each name it meets means, so nothing that sets a
/// formula looks a name up and nothing that reads one has to ask a typesetter what it knows. The symbol table and the
/// character mappings are XAML-Math's (MIT, see Core's ThirdPartyNotices); how a symbol is drawn, its metrics in Computer
/// Modern, stays with the typesetter.
/// </para>
/// </summary>
internal static class TexVocabulary
{
    /// <summary>The face whose argument is words rather than maths — and what every face in that family is drawn from.</summary>
    public const string Text = "text";

    /// <summary>The name the tables give a side of a fence written open — <c>\left.</c> — which draws nothing.</summary>
    public const string EmptyDelimiter = "_emptyDelimiter";

    private static readonly IReadOnlyDictionary<string, TexSymbol> Symbols = ReadSymbols();

    private static readonly (IReadOnlyDictionary<char, string> Symbols, IReadOnlyDictionary<char, string> Delimiters,
                             IReadOnlySet<string> Faces) Settings = ReadSettings();

    /// <summary>The symbol the tables call this, or null where they call nothing that.</summary>
    public static TexSymbol? Symbol(string name) => Symbols.GetValueOrDefault(name);

    /// <summary>The delimiter this name stands for, or null where it names none or what it names cannot grow.</summary>
    public static TexSymbol? Delimiter(string? name) =>
        name is not null && Symbol(name) is { IsDelimiter: true } symbol ? symbol : null;

    /// <summary>The symbol a character is set as — <c>+</c> is <c>plus</c> — or null for a letter, a digit, or anything set as itself.</summary>
    public static TexSymbol? SymbolOf(char character) =>
        char.IsLetterOrDigit(character) ? null
        : Settings.Symbols.TryGetValue(character, out var name) ? Symbol(name) : null;

    /// <summary>
    /// The delimiter a character stands for, or null. <c>.</c> is how a fence is written with one end left open —
    /// <c>\left. \right)</c> — so no delimiter is the right answer rather than a failure.
    /// </summary>
    public static TexSymbol? DelimiterOf(char character) =>
        character != '.' && Settings.Delimiters.TryGetValue(character, out var name) ? Delimiter(name) : null;

    /// <summary>
    /// The delimiter a command's argument writes — a bracket written as itself, or as a command naming one: <c>(</c>,
    /// <c>\langle</c>, <c>\|</c> — or null where it writes none.
    /// </summary>
    public static TexSymbol? DelimiterWritten(ContentNode command)
    {
        if (command.Part(TexRole.Argument) is not { } written) return null;
        if ((written.IsLeaf ? written.Text : written.Part(Roles.Name)?.Text) is not { } text) return null;

        return text switch
        {
            @"\|" => Delimiter("Vert"),
            { Length: 1 } => DelimiterOf(text[0]),
            _ => Delimiter(text.TrimStart('\\')),
        };
    }

    /// <summary>Whether a name is a face its argument is set in — <c>mathrm</c>, <c>text</c>.</summary>
    public static bool IsFace(string name) => Settings.Faces.Contains(name);

    /// <summary>
    /// Whether a face's argument is words rather than maths — <c>\text</c>, <c>\mbox</c> and the <c>\text…</c> family. Every
    /// character in one is set as written, spaces included.
    /// </summary>
    public static bool IsWords(string name) => Words.Contains(name);

    /// <summary>Whether a big operator sets its limits beside it in every style — the integrals.</summary>
    public static bool SetsLimitsBeside(string name) => SideLimits.Contains(name);

    /// <summary>Whether LaTeX has this command at all, drawn here or not — the difference between a gap here and a mistake in what was typed.</summary>
    public static bool Knows(string name) =>
        Commands.ContainsKey(name) || Sizes.ContainsKey(name) || Known.Contains(name) || IsFace(name) || Symbol(name) is not null;

    /// <summary>What a command's name alone says it is, or null where it is none of these.</summary>
    public static TexMeaning? Command(string name) => Commands.GetValueOrDefault(name);

    /// <summary>How an environment arranges what it holds, or null where nothing here arranges it.</summary>
    public static TexArrangement? Environment(string name) => Environments.GetValueOrDefault(name);

    /// <summary>The height and class of a <c>\big</c>-family delimiter, or null where the command is not one.</summary>
    public static (double MinHeight, TexAtomType Class)? Size(string name) => Sizes.TryGetValue(name, out var size) ? size : null;

    /// <summary>A strut written down by name — <c>\quad</c>, <c>\thinspace</c> — in mu, or null.</summary>
    public static double? Strut(string name) => Struts.TryGetValue(name, out var mu) ? mu : null;

    /// <summary>A length as written — <c>-3mu</c>, <c>2em</c>, <c>1.5cm</c> — as the unit and the value, or null where it is not one.</summary>
    public static (TexUnit Unit, double Value)? Length(string written)
    {
        var text = written.Trim();
        var split = text.Length;
        for (var at = 0; at < text.Length; at++)
        {
            if (char.IsLetter(text[at]))
            {
                split = at;
                break;
            }
        }

        if (!double.TryParse(text[..split].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;

        // Em, ex, mu, pt, pc and px are set as they are; an absolute unit is turned into points.
        return text[split..].Trim().ToLowerInvariant() switch
        {
            "em" => (TexUnit.Em, value),
            "ex" => (TexUnit.Ex, value),
            "mu" => (TexUnit.Mu, value),
            "pt" => (TexUnit.Point, value),
            "pc" => (TexUnit.Pica, value),
            "px" => (TexUnit.Pixel, value),
            "bp" => (TexUnit.Point, value * 72.27 / 72.0),
            "in" => (TexUnit.Point, value * 72.27),
            "cm" => (TexUnit.Point, value * 72.27 / 2.54),
            "mm" => (TexUnit.Point, value * 72.27 / 25.4),
            _ => null,
        };
    }

    /// <summary>
    /// Text faces whose argument is words rather than a formula: the spaces in it are kept and the characters are not treated
    /// as maths symbols.
    /// </summary>
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        Text, "mbox", "textbf", "textit", "textrm", "textsc", "textsf", "texttt",
    };

    /// <summary>
    /// The big operators whose limits go beside them rather than above and below, in every style: the integrals. TeX gives
    /// <c>\intop</c> <c>\nolimits</c> by default and <c>\sum</c> <c>\limits</c>, which is why an integral's bounds sit at its
    /// side in every published paper. <c>\limits</c> after one still stacks them.
    /// </summary>
    private static readonly HashSet<string> SideLimits = new(StringComparer.Ordinal)
    {
        "int", "intop", "iint", "iiint", "iiiint", "idotsint", "oint", "oiint", "oiiint",
    };

    /// <summary>
    /// Commands LaTeX has whose meaning is read from what is written with them rather than from the name — a colour, a length,
    /// a number — or that are structure here rather than anything of their own, or that nothing here draws.
    /// </summary>
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "left", "right", "begin", "color", "textcolor", "colorbox", "tag", "hspace", "mspace", "cfrac",
        "bmatrix", "Bmatrix", "vmatrix", "Vmatrix",
    };

    /// <summary>
    /// Written-down space, in mu — eighteenths of a quad. Not macros: a macro stands for something that could be written out
    /// longhand, but a strut of four mu stands for nothing but itself, since LaTeX has no way of saying it.
    /// </summary>
    private static readonly Dictionary<string, double> Struts = new(StringComparer.Ordinal)
    {
        ["thinspace"] = 3,
        ["medspace"] = 4,
        ["thickspace"] = 5,
        ["negthinspace"] = -3,
        ["negmedspace"] = -4,
        ["negthickspace"] = -5,
        ["enspace"] = 9,
        ["space"] = 6,
        ["quad"] = 18,
        ["qquad"] = 36,
    };

    // The arrangements a grid is set in. An aligned block is not a table: its columns are an equation and its parts, so they
    // keep the close spacing they had rather than taking a column gap.
    private static readonly TexMatrixArrangement Align = new(null, null, MatrixCellAlignment.Aligned,
                                                              VerticalPadding: TexMatrixArrangement.Padding,
                                                              HorizontalPadding: TexMatrixArrangement.Padding);
    private static readonly TexMatrixArrangement Cases = new("lbrace", null, MatrixCellAlignment.Left);
    private static readonly TexMatrixArrangement Matrix = new(null, null, MatrixCellAlignment.Center);
    private static readonly TexMatrixArrangement PMatrix = new("(", ")", MatrixCellAlignment.Center);
    private static readonly TexMatrixArrangement BMatrix = new("lbrack", "rbrack", MatrixCellAlignment.Center);
    private static readonly TexMatrixArrangement BbMatrix = new("lbrace", "rbrace", MatrixCellAlignment.Center);
    private static readonly TexMatrixArrangement VMatrix = new("vert", "vert", MatrixCellAlignment.Center);
    private static readonly TexMatrixArrangement VvMatrix = new("Vert", "Vert", MatrixCellAlignment.Center);
    private static readonly TexMatrixArrangement Gathered = new(null, null, MatrixCellAlignment.Center);

    // \smallmatrix is an inline matrix: the same layout, set in script size.
    private static readonly TexMatrixArrangement SmallMatrix = new(null, null, MatrixCellAlignment.Center, TexStyle.Script);

    // \substack stacks the lines of a big operator's limit: script size like \smallmatrix, but set solid, since the lines
    // belong to one limit rather than to separate rows of a table.
    private static readonly TexMatrixArrangement SubStack = new(null, null, MatrixCellAlignment.Center, TexStyle.Script,
                                                                VerticalPadding: 0.1, HorizontalPadding: 0);

    private static readonly Dictionary<string, TexArrangement> Environments = new(StringComparer.Ordinal)
    {
        ["array"] = new TexArrayArrangement(null),
        ["align"] = Align,
        ["align*"] = Align,
        ["aligned"] = Align,
        ["split"] = Align,
        ["gather"] = Gathered,
        ["gather*"] = Gathered,
        ["gathered"] = Gathered,
        ["cases"] = Cases,
        ["matrix"] = Matrix,
        ["smallmatrix"] = SmallMatrix,
        ["pmatrix"] = PMatrix,
        ["bmatrix"] = BMatrix,
        ["Bmatrix"] = BbMatrix,
        ["vmatrix"] = VMatrix,
        ["Vmatrix"] = VvMatrix,

        // The display environments: none means anything more than its contents in a formula that is already a display of
        // its own.
        ["equation"] = new TexContents(),
        ["equation*"] = new TexContents(),
        ["subequations"] = new TexContents(),
        ["multline"] = Gathered,
        ["multline*"] = Gathered,
        ["flalign"] = Align,
        ["flalign*"] = Align,

        // The counted alignments: align, with a count of column pairs first that sets spacing across a page of text and has
        // nothing to govern in one formula — so it is read as the environment's argument and set aside.
        ["alignat"] = Align,
        ["alignat*"] = Align,
        ["alignedat"] = Align,
        ["xalignat"] = Align,
        ["xalignat*"] = Align,
        ["xxalignat"] = Align,
        ["xxalignat*"] = Align,
    };

    /// <summary>
    /// What each command is, by its name without the backslash. A face (<see cref="IsFace"/>), a symbol (<see cref="Symbol"/>)
    /// and a strut (<see cref="Strut"/>) are the tables' to say; everything else a formula is written with is here.
    /// </summary>
    private static readonly Dictionary<string, TexMeaning> Commands = new(StringComparer.Ordinal)
    {
        ["frac"] = new TexFraction(),
        ["dfrac"] = new TexStyledFraction(TexStyle.Display),
        ["tfrac"] = new TexStyledFraction(TexStyle.Text),
        ["nicefrac"] = new TexSlashFraction(),
        ["sfrac"] = new TexSlashFraction(),
        ["genfrac"] = new TexGeneralFraction(),

        // amsmath spells all three as \genfrac{(}{)}{0pt}{}: a fraction with no rule drawn, inside parentheses.
        ["binom"] = new TexBinomial(null),
        ["dbinom"] = new TexBinomial(TexStyle.Display),
        ["tbinom"] = new TexBinomial(TexStyle.Text),

        ["sqrt"] = new TexRoot(),
        ["overline"] = new TexOverline(),
        ["underline"] = new TexUnderline(),
        ["not"] = new TexNegation(),
        ["hdotsfor"] = new TexRowOfDots(),
        ["hline"] = new TexRule(),
        ["limits"] = new TexLimits(Vertical: true),
        ["nolimits"] = new TexLimits(Vertical: false),
        ["surd"] = new TexSurd(),

        // A dot or a tilde set over an equals sign at a fixed height, and a relation either side — plain.tex's composites,
        // which neither \overset nor \stackrel can spell, since both shrink what they put on top.
        ["doteq"] = new TexPile("equals", "ldotp", 2),
        ["cong"] = new TexPile("equals", "sim", 1),

        // Space written as a word: the width of one in the font it stands in.
        [" "] = new TexSpace(null, 0),
        ["nbsp"] = new TexSpace(null, 0),

        // Tables written as commands — a big operator's stacked limit, and plain TeX's \matrix{…}, \pmatrix{…} and \cases{…}:
        // the braces hold the rows, and the command is how they are arranged.
        ["substack"] = new TexStack(SubStack),
        ["matrix"] = new TexStack(Matrix),
        ["pmatrix"] = new TexStack(PMatrix),
        ["cases"] = new TexStack(Cases),

        // The braket package. The capitalised forms are its "always stretch" variants, which is what a fence does anyway —
        // they are here so that copied source keeps working.
        ["bra"] = new TexBraket("langle", "vert"),
        ["Bra"] = new TexBraket("langle", "vert"),
        ["ket"] = new TexBraket("vert", "rangle"),
        ["Ket"] = new TexBraket("vert", "rangle"),
        ["braket"] = new TexBraket("langle", "rangle"),
        ["Braket"] = new TexBraket("langle", "rangle"),

        ["cancel"] = new TexCancel(StrokeMode.Normal),
        ["bcancel"] = new TexCancel(StrokeMode.Back),
        ["xcancel"] = new TexCancel(StrokeMode.Both),

        ["overrightarrow"] = new TexOverArrow(ArrowDecoration.HeadRight, Over: true),
        ["overleftarrow"] = new TexOverArrow(ArrowDecoration.HeadLeft, Over: true),
        ["overleftrightarrow"] = new TexOverArrow(ArrowDecoration.HeadLeft | ArrowDecoration.HeadRight, Over: true),
        ["underrightarrow"] = new TexOverArrow(ArrowDecoration.HeadRight, Over: false),
        ["underleftarrow"] = new TexOverArrow(ArrowDecoration.HeadLeft, Over: false),
        ["underleftrightarrow"] = new TexOverArrow(ArrowDecoration.HeadLeft | ArrowDecoration.HeadRight, Over: false),

        ["xrightarrow"] = new TexExtensibleArrow(ArrowDecoration.HeadRight),
        ["xleftarrow"] = new TexExtensibleArrow(ArrowDecoration.HeadLeft),
        ["xleftrightarrow"] = new TexExtensibleArrow(ArrowDecoration.HeadLeft | ArrowDecoration.HeadRight),
        ["xRightarrow"] = new TexExtensibleArrow(ArrowDecoration.HeadRight | ArrowDecoration.DoubleShaft),
        ["xLeftarrow"] = new TexExtensibleArrow(ArrowDecoration.HeadLeft | ArrowDecoration.DoubleShaft),
        ["xLeftrightarrow"] = new TexExtensibleArrow(ArrowDecoration.HeadLeft | ArrowDecoration.HeadRight | ArrowDecoration.DoubleShaft),
        ["xmapsto"] = new TexExtensibleArrow(ArrowDecoration.HeadRight | ArrowDecoration.TailBarLeft),

        // LaTeX makes these operators, so a script written after one belongs beyond the brace rather than beside it.
        ["overbrace"] = new TexBrace(Over: true, "lbrace"),
        ["underbrace"] = new TexBrace(Over: false, "rbrace"),

        ["vdots"] = new TexStackedDots(Diagonal: false),
        ["ddots"] = new TexStackedDots(Diagonal: true),

        // \pmod{n} is "(mod n)" after a wide space and \pod{n} "(n)"; `a \mod b` is the word without the brackets.
        ["pmod"] = new TexModulus(WithMod: true, Fenced: true),
        ["pod"] = new TexModulus(WithMod: false, Fenced: true),
        ["mod"] = new TexModulus(WithMod: true, Fenced: false),

        ["phantom"] = new TexPhantom(Width: true, Height: true),
        ["hphantom"] = new TexPhantom(Width: true, Height: false),
        ["vphantom"] = new TexPhantom(Width: false, Height: true),

        ["smash"] = new TexSmash(null),
        ["mathllap"] = new TexSmash(TexAlignment.Left),
        ["mathrlap"] = new TexSmash(TexAlignment.Right),
        ["mathclap"] = new TexSmash(TexAlignment.Center),
        ["llap"] = new TexSmash(TexAlignment.Left),
        ["rlap"] = new TexSmash(TexAlignment.Right),
        ["clap"] = new TexSmash(TexAlignment.Center),

        ["boxed"] = new TexBoxed(),
        ["fbox"] = new TexBoxed(),

        ["boldsymbol"] = new TexBoldSymbol(),
        ["bm"] = new TexBoldSymbol(),
        ["pmb"] = new TexBoldSymbol(),

        ["operatorname"] = new TexOperatorName(Starred: false),
        ["operatorname*"] = new TexOperatorName(Starred: true),

        ["overset"] = new TexAnnotation(Over: true, AsRelation: false),
        ["underset"] = new TexAnnotation(Over: false, AsRelation: false),
        ["stackrel"] = new TexAnnotation(Over: true, AsRelation: true),

        // Retyping commands: the argument keeps its shape and changes its kind, which is what decides the space round it.
        ["mathord"] = new TexRetyped(TexAtomType.Ordinary),
        ["mathop"] = new TexRetyped(TexAtomType.BigOperator),
        ["mathbin"] = new TexRetyped(TexAtomType.BinaryOperator),
        ["mathrel"] = new TexRetyped(TexAtomType.Relation),
        ["mathopen"] = new TexRetyped(TexAtomType.Opening),
        ["mathclose"] = new TexRetyped(TexAtomType.Closing),
        ["mathpunct"] = new TexRetyped(TexAtomType.Punctuation),
        ["mathinner"] = new TexRetyped(TexAtomType.Inner),

        ["_"] = new TexUnderscore(),

        // Switches: they set the rest of the group they stand in, which is why they are written {\cal N} rather than \cal{N}.
        ["displaystyle"] = new TexSwitch(null, TexStyle.Display),
        ["textstyle"] = new TexSwitch(null, TexStyle.Text),
        ["scriptstyle"] = new TexSwitch(null, TexStyle.Script),
        ["scriptscriptstyle"] = new TexSwitch(null, TexStyle.ScriptScript),
        ["tiny"] = new TexSwitch(null, TexStyle.ScriptScript),
        ["scriptsize"] = new TexSwitch(null, TexStyle.Script),

        // Type sizes of a document: a formula is set at one size, so these set the rest of the group as it was.
        ["footnotesize"] = new TexSwitch(null, null),
        ["small"] = new TexSwitch(null, null),
        ["normalsize"] = new TexSwitch(null, null),
        ["large"] = new TexSwitch(null, null),
        ["Large"] = new TexSwitch(null, null),
        ["LARGE"] = new TexSwitch(null, null),
        ["huge"] = new TexSwitch(null, null),
        ["Huge"] = new TexSwitch(null, null),

        // The plain-TeX font switches. Deprecated in LaTeX2e, and published papers are full of them.
        ["cal"] = new TexSwitch("mathcal", null),
        ["bf"] = new TexSwitch("mathbf", null),
        ["it"] = new TexSwitch("mathit", null),
        ["mit"] = new TexSwitch("mathit", null),   // plain TeX's name for the maths italic
        ["rm"] = new TexSwitch("mathrm", null),
        ["sf"] = new TexSwitch("mathsf", null),
        ["tt"] = new TexSwitch("mathtt", null),
        ["frak"] = new TexSwitch("mathfrak", null),
        ["scr"] = new TexSwitch("mathscr", null),

        // Numbering, cross references and page layout: read and dropped, since a formula stands alone with no page to affect.
        ["notag"] = new TexDiscarded(),
        ["nonumber"] = new TexDiscarded(),
        ["label"] = new TexDiscarded(),
        ["eqref"] = new TexDiscarded(),
        ["numberwithin"] = new TexDiscarded(),
        ["raisetag"] = new TexDiscarded(),
        ["intertext"] = new TexDiscarded(),
        ["shortintertext"] = new TexDiscarded(),
        ["allowdisplaybreaks"] = new TexDiscarded(),
        ["displaybreak"] = new TexDiscarded(),
        ["nobreakdash"] = new TexDiscarded(),
        ["accentedsymbol"] = new TexDiscarded(),
        ["DeclareMathOperator"] = new TexDiscarded(),
        ["DeclarePairedDelimiter"] = new TexDiscarded(),

        ["shoveleft"] = new TexTransparent(),
        ["shoveright"] = new TexTransparent(),

        // A new line outside a table has nowhere to go in one formula.
        [@"\"] = new TexUnset(),
    };

    /// <summary>
    /// <c>\big</c>, <c>\Big</c>, <c>\bigg</c> and <c>\Bigg</c>, with their l/r/m variants: a delimiter at a set size rather than
    /// one grown to fit what it stands beside, and the class it takes. TeX builds them by fencing an empty box 8.5, 11.5, 14.5
    /// or 17.5pt tall, and <c>\left</c>'s sizing rule turns those into delimiters of 1.15, 1.75, 2.35 and 2.95 em — an
    /// arithmetic progression, since both the struts and the rule are linear in the size. Those lengths are absolute in TeX,
    /// so <c>\big(</c> is the same delimiter inside a subscript as outside one.
    /// </summary>
    private static readonly Dictionary<string, (double MinHeight, TexAtomType Class)> Sizes =
        new[] { "big", "Big", "bigg", "Bigg" }
            .SelectMany((name, size) => new[]
            {
                (name, TexAtomType.Ordinary), (name + "l", TexAtomType.Opening),
                (name + "r", TexAtomType.Closing), (name + "m", TexAtomType.Relation),
            }.Select(sized => (sized.Item1, Size: (1.15 + 0.6 * size, sized.Item2))))
            .ToDictionary(sized => sized.Item1, sized => sized.Size, StringComparer.Ordinal);

    /// <summary>The symbol table: every symbol XAML-Math's tables name, with its class and whether it grows as a delimiter.</summary>
    private static Dictionary<string, TexSymbol> ReadSymbols()
    {
        var classes = new Dictionary<string, TexAtomType>(StringComparer.Ordinal)
        {
            ["ord"] = TexAtomType.Ordinary,
            ["op"] = TexAtomType.BigOperator,
            ["bin"] = TexAtomType.BinaryOperator,
            ["rel"] = TexAtomType.Relation,
            ["open"] = TexAtomType.Opening,
            ["close"] = TexAtomType.Closing,
            ["punct"] = TexAtomType.Punctuation,
            ["acc"] = TexAtomType.Accent,
        };

        var symbols = new Dictionary<string, TexSymbol>(StringComparer.Ordinal);

        foreach (var symbol in Resource("TexSymbols.xml").Elements("Symbol"))
        {
            var name = (string)symbol.Attribute("name")!;
            symbols.Add(name, new TexSymbol(name, classes[(string)symbol.Attribute("type")!],
                                            (bool?)symbol.Attribute("del") ?? false));
        }

        return symbols;
    }

    /// <summary>Which characters stand for which symbols and delimiters, and the faces an argument is set in.</summary>
    private static (IReadOnlyDictionary<char, string>, IReadOnlyDictionary<char, string>, IReadOnlySet<string>) ReadSettings()
    {
        var root = Resource("TexFormulaSettings.xml");

        Dictionary<char, string> Mappings(string element) =>
            (root.Element(element)?.Elements("Map") ?? []).ToDictionary(map => ((string)map.Attribute("char")!)[0],
                                                                         map => (string)map.Attribute("symbol")!);

        var faces = (root.Element("TextStyles")?.Elements("TextStyle") ?? []).Select(face => (string)face.Attribute("name")!);

        return (Mappings("CharacterToSymbolMappings"), Mappings("CharacterToDelimiterMappings"),
                new HashSet<string>(faces, StringComparer.Ordinal));
    }

    private static XElement Resource(string name)
    {
        using var stream = typeof(TexVocabulary).Assembly.GetManifestResourceStream("Nexaflow.Markdown.Latex.Data." + name)
                           ?? throw new InvalidOperationException($"The LaTeX vocabulary's {name} is not built into the assembly.");

        return XDocument.Load(stream).Root!;
    }
}
