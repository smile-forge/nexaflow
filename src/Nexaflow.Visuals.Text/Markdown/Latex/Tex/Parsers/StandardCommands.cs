using System.Collections.Generic;
using System.Globalization;
using System.IO.Pipes;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Exceptions;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Parsers.Matrices;
using System;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Markdown.Latex.Tex.Parsers;

internal static class StandardCommands
{
    private class UnderlineCommand
    {
    }

    // The stretchy arrow accents: an arrow drawn to the width of its argument, above or below it.
    internal sealed class OverArrowCommand
    {
        public static OverArrowCommand Right { get; } = new(ArrowDecoration.HeadRight, over: true);
        public static OverArrowCommand Left { get; } = new(ArrowDecoration.HeadLeft, over: true);
        public static OverArrowCommand Both { get; } =
            new(ArrowDecoration.HeadLeft | ArrowDecoration.HeadRight, over: true);
        public static OverArrowCommand UnderRight { get; } = new(ArrowDecoration.HeadRight, over: false);
        public static OverArrowCommand UnderLeft { get; } = new(ArrowDecoration.HeadLeft, over: false);
        public static OverArrowCommand UnderBoth { get; } =
            new(ArrowDecoration.HeadLeft | ArrowDecoration.HeadRight, over: false);

        private readonly ArrowDecoration _decoration;
        private readonly bool _over;

        private OverArrowCommand(ArrowDecoration decoration, bool over)
        {
            _decoration = decoration;
            _over = over;
        }

        internal ArrowDecoration Decoration => _decoration;
        internal bool Over => _over;
    }

    // \vdots and \ddots take no argument; they just emit a fixed run of dots.
    internal sealed class DotsCommand
    {
        public static DotsCommand Vertical { get; } = new(DotsShape.Vertical);
        public static DotsCommand Diagonal { get; } = new(DotsShape.Diagonal);

        private readonly DotsShape _shape;

        private DotsCommand(DotsShape shape)
        {
            _shape = shape;
        }

        internal DotsShape Shape => _shape;

        internal enum DotsShape
        {
            Vertical,
            Diagonal,
        }
    }

    // \hspace{<length>} inserts horizontal space of an explicit length, e.g. \hspace{2em} or \hspace{-3pt}.
    private sealed class HspaceCommand
    {
        public static HspaceCommand Hspace { get; } = new("hspace");

        /// <summary>amsmath's <c>\mspace</c>: the same thing, in math units.</summary>
        public static HspaceCommand Mspace { get; } = new("mspace");

        private readonly string _name;

        private HspaceCommand(string name)
        {
            _name = name;
        }
    }

    /// <summary>Reads a LaTeX length - "2em", "-3pt", "0pt" - into the unit and value the engine takes.</summary>
    private static void ParseLength(string text, string command, out TexUnit unit, out double value)
    {
        text = text.Trim();
        var splitIndex = text.Length;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsLetter(text[i]))
            {
                splitIndex = i;
                break;
            }
        }

        var numberPart = text.Substring(0, splitIndex).Trim();
        var unitPart = text.Substring(splitIndex).Trim().ToLowerInvariant();
        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            throw new TexParseException($"Invalid {command} length: \"{text}\".");

        // The engine natively supports em/ex/mu/pt/pc/px; absolute units are converted to points.
        switch (unitPart)
        {
            case "em": unit = TexUnit.Em; break;
            case "ex": unit = TexUnit.Ex; break;
            case "mu": unit = TexUnit.Mu; break;
            case "pt": unit = TexUnit.Point; break;
            case "pc": unit = TexUnit.Pica; break;
            case "px": unit = TexUnit.Pixel; break;
            case "bp": unit = TexUnit.Point; value *= 72.27 / 72.0; break;
            case "in": unit = TexUnit.Point; value *= 72.27; break;
            case "cm": unit = TexUnit.Point; value *= 72.27 / 2.54; break;
            case "mm": unit = TexUnit.Point; value *= 72.27 / 25.4; break;
            default: throw new TexParseException($"Unsupported {command} unit: \"{unitPart}\".");
        }
    }

    /// <summary>A length as written — <c>-3mu</c>, <c>2em</c> — as the unit and the value, or null where it is not one.</summary>
    internal static (TexUnit Unit, double Value)? LengthOf(string command, string written)
    {
        try
        {
            ParseLength(written, command, out var unit, out var value);
            return (unit, value);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The size and class of a <c>\big</c>-family delimiter, or null where the command is not one.</summary>
    internal static (double MinHeight, TexAtomType Type)? SizedDelimiterOf(string command) =>
        Dictionary.TryGetValue(command, out var parser) && parser is BigDelimiterCommand big ? (big.MinHeight, big.Type) : null;

    /// <summary>A strut written down by name — <c>\quad</c>, <c>\thinspace</c> — in mu, or null.</summary>
    internal static double? StrutOf(string name) => Struts.TryGetValue(name, out var mu) ? mu : null;

    // \dfrac and \tfrac: \frac forced into display or text style respectively.
    internal sealed class FracStyleCommand
    {
        public static FracStyleCommand Dfrac { get; } = new(TexStyle.Display);
        public static FracStyleCommand Tfrac { get; } = new(TexStyle.Text);

        private readonly TexStyle _style;

        private FracStyleCommand(TexStyle style)
        {
            _style = style;
        }

        internal TexStyle Style => _style;
    }

    // \cfrac[l|c|r]{a}{b}: a continued-fraction fraction — display style throughout (nested \cfrac stays
    // full size) with an optional numerator alignment.
    internal sealed class CfracCommand
    {
        /// <summary>
        /// Which way <c>[l]</c>, <c>[c]</c> or <c>[r]</c> says the numerator leans; centred where nothing
        /// says. Read off the part it was written as rather than off the atom built from it: the letter
        /// is an instruction and not a thing on the page, and the atom keeps no memory of which it was.
        /// </summary>
        internal static TexAlignment Leaning(Nexaflow.Markdown.Ast.ContentPart? origin) =>
            origin?.Part(Nexaflow.Markdown.Latex.TexRole.Option)?.Node.Print().Trim('[', ']', ' ') switch
            {
                "l" => TexAlignment.Left,
                "r" => TexAlignment.Right,
                _ => TexAlignment.Center,
            };
    }

    // \nicefrac{a}{b} and \sfrac{a}{b}: an inline "slash" fraction (raised numerator / lowered denominator).
    internal sealed class SlashFractionCommand
    {
    }

    // \pmod{n} -> "(mod n)" after a wide space; \pod{n} -> "(n)". Used as e.g. a \equiv b \pmod{n}.
    internal sealed class ParenModCommand
    {
        public static ParenModCommand Pmod { get; } = new(withMod: true);
        public static ParenModCommand Pod { get; } = new(withMod: false);

        // `a \mod b` is the same word without the parentheses round it — amsmath sets it as a binary
        // operator with a wide gap in front. It had a reader of its own and nothing that built it, so it
        // set as its own characters the moment the old reader stopped drawing.
        public static ParenModCommand Mod { get; } = new(withMod: true, fenced: false);

        private readonly bool _withMod;
        private readonly bool _fenced;

        private ParenModCommand(bool withMod, bool fenced = true)
        {
            _withMod = withMod;
            _fenced = fenced;
        }

        internal bool WithMod => _withMod;
        internal bool Fenced => _fenced;
    }

    /// <summary>
    /// <c>\genfrac{l}{r}{thickness}{style}{numerator}{denominator}</c> — the general fraction every other one in
    /// amsmath is spelled with. An empty delimiter argument means no delimiter that side, and an empty thickness
    /// means the default rule — the one place a fraction's bar is written as a length rather than implied.
    /// </summary>
    internal sealed class GenFracCommand
    {
        public static GenFracCommand Instance { get; } = new();
    }

    // \displaystyle, \textstyle, \scriptstyle and \scriptscriptstyle are switches, not one-argument commands:
    // they apply from where they appear to the end of the enclosing group. Reading only the next element would
    // leave the scripts of e.g. "\displaystyle\sum_{i=1}^{n}" outside the switch, which is where the style
    // actually matters (display style is what moves the limits above and below the operator).
    private sealed class StyleCommand
    {
        public static StyleCommand Display { get; } = new(TexStyle.Display);
        public static StyleCommand Text { get; } = new(TexStyle.Text);
        public static StyleCommand Script { get; } = new(TexStyle.Script);
        public static StyleCommand ScriptScript { get; } = new(TexStyle.ScriptScript);

        /// <summary>
        /// A size switch with no equivalent here - <c>\large</c>, <c>\small</c> and the rest. They set
        /// the type size of a document, and a formula is set at one size, so this applies to the rest
        /// of the group and changes nothing: a formula written with one still renders.
        /// </summary>
        public static StyleCommand Unchanged { get; } = new(null);

        private readonly TexStyle? _style;

        /// <summary>What it switches to, or null where it changes nothing — see <see cref="Unchanged"/>.</summary>
        internal TexStyle? Style => _style;

        private StyleCommand(TexStyle? style)
        {
            _style = style;
        }
    }

    // \overset{ann}{base}, \underset{ann}{base} and \stackrel{ann}{rel}: the annotation is set in script size
    // above or below the base. \stackrel differs from \overset only in the spacing it gets: its result is a
    // relation (it exists to stack something over an arrow), so it is typed as one.
    internal sealed class StackedAnnotationCommand
    {
        public static StackedAnnotationCommand Overset { get; } = new(over: true, asRelation: false);
        public static StackedAnnotationCommand Underset { get; } = new(over: false, asRelation: false);
        public static StackedAnnotationCommand Stackrel { get; } = new(over: true, asRelation: true);

        internal const double AnnotationSpace = 2.5; // mu, the same order as the \overbrace-style annotations

        private readonly bool _over;
        private readonly bool _asRelation;

        private StackedAnnotationCommand(bool over, bool asRelation)
        {
            _over = over;
            _asRelation = asRelation;
        }

        internal bool Over => _over;
        internal bool AsRelation => _asRelation;
    }

    // \phantom{x} and its one-dimensional variants: the content is measured and then not drawn, so it reserves
    // space without printing anything.
    internal sealed class PhantomCommand
    {
        public static PhantomCommand Both { get; } = new(useWidth: true, useHeight: true);
        public static PhantomCommand Horizontal { get; } = new(useWidth: true, useHeight: false);
        public static PhantomCommand Vertical { get; } = new(useWidth: false, useHeight: true);

        private readonly bool _useWidth;
        private readonly bool _useHeight;

        private PhantomCommand(bool useWidth, bool useHeight)
        {
            _useWidth = useWidth;
            _useHeight = useHeight;
        }

        internal bool UseWidth => _useWidth;
        internal bool UseHeight => _useHeight;
    }

    // \smash{x} draws the content and reports no height, \math?lap{x} draws it and reports no width. Both are the
    // inverse of \phantom: ink without extent rather than extent without ink.
    internal sealed class SmashCommand
    {
        public static SmashCommand Smash { get; } = new(null);
        public static SmashCommand Llap { get; } = new(TexAlignment.Left);
        public static SmashCommand Rlap { get; } = new(TexAlignment.Right);
        public static SmashCommand Clap { get; } = new(TexAlignment.Center);

        private readonly TexAlignment? _lapAlignment;

        private SmashCommand(TexAlignment? lapAlignment)
        {
            _lapAlignment = lapAlignment;
        }

        internal TexAlignment? LapAlignment => _lapAlignment;
    }

    // \boxed{x} and \fbox{x}: the content inside a rectangular frame.
    internal sealed class BoxedCommand
    {
    }

    // \xrightarrow[under]{over} and friends: an arrow stretched to fit the labels written over (and optionally
    // under) it. The under label is the optional argument, as in LaTeX.
    internal sealed class ExtensibleArrowCommand
    {
        public static ExtensibleArrowCommand Right { get; } = new(ArrowDecoration.HeadRight);
        public static ExtensibleArrowCommand Left { get; } = new(ArrowDecoration.HeadLeft);
        public static ExtensibleArrowCommand Both { get; } =
            new(ArrowDecoration.HeadLeft | ArrowDecoration.HeadRight);
        public static ExtensibleArrowCommand DoubleRight { get; } =
            new(ArrowDecoration.HeadRight | ArrowDecoration.DoubleShaft);
        public static ExtensibleArrowCommand DoubleLeft { get; } =
            new(ArrowDecoration.HeadLeft | ArrowDecoration.DoubleShaft);
        public static ExtensibleArrowCommand DoubleBoth { get; } =
            new(ArrowDecoration.HeadLeft | ArrowDecoration.HeadRight | ArrowDecoration.DoubleShaft);
        public static ExtensibleArrowCommand MapsTo { get; } =
            new(ArrowDecoration.HeadRight | ArrowDecoration.TailBarLeft);

        private readonly ArrowDecoration _decoration;

        private ExtensibleArrowCommand(ArrowDecoration decoration)
        {
            _decoration = decoration;
        }

        internal ArrowDecoration Decoration => _decoration;
    }

    // \overbrace{body}^{label} and \underbrace{body}_{label}: a brace stretched to the width of the
    // body, with an optional label beyond it. LaTeX makes these operators, so a script written after
    // one belongs above (or below) the brace rather than beside it — which means reading it here,
    // before the parser attaches it as an ordinary script.
    internal sealed class BraceCommand
    {
        public static BraceCommand Over { get; } = new(over: true);
        public static BraceCommand Under { get; } = new(over: false);

        internal const double LabelKern = 0.5; // ex, between the brace and its label

        private readonly bool _over;

        private BraceCommand(bool over)
        {
            _over = over;
        }

        /// <summary>Whether a label on this side belongs to the brace: <c>^</c> over, <c>_</c> under.</summary>
        public bool Labels(bool over) => over == _over;

        internal bool IsOver => _over;
    }

    // \boldsymbol{…} (also spelled \bm): every character underneath comes from the bold companion of
    // the font it would otherwise use, which is what makes it work on Greek letters and symbols
    // rather than only on the Latin ones a text style could reach.
    internal sealed class BoldSymbolCommand
    {
    }

    // \operatorname{name} sets a function name upright and, more importantly, types it as an
    // operator: that is what gives it operator spacing and lets a following script become a limit.
    // The starred form takes its limits above and below in display style, as \sum does.
    internal sealed class OperatorNameCommand
    {
        /// <param name="starred">The <c>*</c> form, whose limits go wherever the style puts them rather than always beside the name.</param>
        public OperatorNameCommand(bool starred = false) => this._starred = starred;

        private readonly bool _starred;

        internal bool Starred => _starred;
    }

    // inom{n}{k}, and \dbinom / 	binom which force display or text style. amsmath spells all
    // three as \genfrac{(}{)}{0pt}{}: a fraction with no rule drawn, inside parentheses.
    internal sealed class BinomCommand
    {
        public static BinomCommand Plain { get; } = new(null);
        public static BinomCommand Display { get; } = new(TexStyle.Display);
        public static BinomCommand Text { get; } = new(TexStyle.Text);

        private readonly TexStyle? _style;

        private BinomCommand(TexStyle? style)
        {
            _style = style;
        }

        internal TexStyle? Style => _style;
    }

    /// <summary>
    /// The braket package's Dirac notation: <c>\bra{A}</c> is ⟨A|, <c>\ket{B}</c> is |B⟩, and
    /// <c>\braket{A|B}</c> is ⟨A|B⟩. Set as a fence, like every other bracketed thing, so the delimiters grow and
    /// the editor already knows how to select/carry/un-render one. The capitalised forms are the package's "always
    /// stretch" variants — a fence does that anyway, so they exist only so copied source keeps working. The bar in
    /// <c>\braket{A|B}</c> is left as the plain character it is rather than split out as a separator.
    /// </summary>
    internal sealed class BraketCommand
    {
        public static BraketCommand Bra { get; } = new("langle", "vert");
        public static BraketCommand Ket { get; } = new("vert", "rangle");
        public static BraketCommand Braket { get; } = new("langle", "rangle");

        private readonly string _open;
        private readonly string _close;

        private BraketCommand(string open, string close)
        {
            _open = open;
            _close = close;
        }

        internal string Open => _open;
        internal string Close => _close;
    }

    internal sealed class CancelCommand
    {
        public static CancelCommand BCancel { get; } = new(StrokeMode.Back);
        public static CancelCommand Cancel { get; } = new(StrokeMode.Normal);
        public static CancelCommand XCancel { get; } = new(StrokeMode.Both);

        private CancelCommand(StrokeMode strokeBoxMode)
        {
            _strokeBoxMode = strokeBoxMode;
        }

        private readonly StrokeMode _strokeBoxMode;

        internal StrokeMode Mode => _strokeBoxMode;
    }

    /// <summary>Parses the rest of the input as a new line of the formula.</summary>
    private class NewLineCommand
    {
    }

    // \mathop{…} and its family: the argument keeps its shape and changes its kind, which is what
    // decides the space around it. A paper reaches for \mathop where a name should behave as an
    // operator and for \mathrel where a symbol should behave as a relation.
    internal sealed class AtomTypeCommand
    {
        public static AtomTypeCommand Ordinary { get; } = new(TexAtomType.Ordinary);
        public static AtomTypeCommand Operator { get; } = new(TexAtomType.BigOperator);
        public static AtomTypeCommand Binary { get; } = new(TexAtomType.BinaryOperator);
        public static AtomTypeCommand Relation { get; } = new(TexAtomType.Relation);
        public static AtomTypeCommand Opening { get; } = new(TexAtomType.Opening);
        public static AtomTypeCommand Closing { get; } = new(TexAtomType.Closing);
        public static AtomTypeCommand Punctuation { get; } = new(TexAtomType.Punctuation);
        public static AtomTypeCommand Inner { get; } = new(TexAtomType.Inner);

        private readonly TexAtomType _type;

        private AtomTypeCommand(TexAtomType type)
        {
            _type = type;
        }

        internal TexAtomType Type => _type;
    }

    // \_ : there is no underscore in the text encoding, so LaTeX draws one - a rule 0.3em wide,
    // sitting a little below the baseline. Its neighbours \# \$ \% \& are ordinary glyphs and are
    // handled as symbols instead.
    //
    // It was left an empty marker when the old reader went, and an empty marker in this table is worse
    // than no entry at all: the reading asks the table whether a name can be drawn and is told yes, so
    // `\_` came through unmarked as a name nobody knows and was quietly shown as its own two characters.
    internal sealed class UnderscoreCommand
    {
        public static UnderscoreCommand Instance { get; } = new();
    }

    // The plain-TeX font switches: \cal, \bf, \it, \rm, \sf, \tt, \frak. Unlike \mathcal{…} they
    // take no argument - a switch runs from where it stands to the end of its group, which is why they
    // are written {\cal N} rather than \cal{N}. Nothing in amsmath documents them and they are
    // deprecated in LaTeX2e, but published papers are full of them, so a formula lifted out of one
    // needs them to mean what it meant there.
    private sealed class FontSwitchCommand
    {
        public static FontSwitchCommand Calligraphic { get; } = new("mathcal");
        public static FontSwitchCommand Bold { get; } = new("mathbf");
        public static FontSwitchCommand Italic { get; } = new("mathit");
        public static FontSwitchCommand Roman { get; } = new("mathrm");
        public static FontSwitchCommand SansSerif { get; } = new("mathsf");
        public static FontSwitchCommand Typewriter { get; } = new("mathtt");
        public static FontSwitchCommand Fraktur { get; } = new("mathfrak");
        public static FontSwitchCommand Script { get; } = new("mathscr");

        private readonly string _textStyle;

        /// <summary>What it switches to, for a reader that does its own building.</summary>
        internal string TextStyle => _textStyle;

        private FontSwitchCommand(string textStyle)
        {
            _textStyle = textStyle;
        }
    }

    // \big, \Big, \bigg and \Bigg, with their l/r/m variants: a delimiter at a set size, rather than
    // one grown to fit what it stands beside. TeX builds them by fencing an empty box 8.5, 11.5, 14.5
    // or 17.5pt tall, and \left's sizing rule turns those into delimiters of 1.15, 1.75, 2.35 and
    // 2.95 em - an arithmetic progression, since both the struts and the rule are linear in the size.
    // Those lengths are absolute in TeX, so unlike almost everything else here they do not shrink
    // with the style: \big( is the same delimiter inside a subscript as outside one.
    /// <summary>Whether this command draws nothing at all — its effect belongs to a page rather than a formula, as <c>\tag</c> and <c>\nonumber</c> do.</summary>
    internal static bool IsDiscarded(string command) =>
        Dictionary.TryGetValue(command, out var parser) && parser is DiscardedCommand;

    internal sealed class BigDelimiterCommand
    {
        private const double SmallestHeight = 1.15;
        private const double HeightStep = 0.6;

        private readonly int _size;
        private readonly TexAtomType _type;

        public BigDelimiterCommand(int size, TexAtomType type)
        {
            _size = size;
            _type = type;
        }

        internal double MinHeight => SmallestHeight + HeightStep * _size;
        internal TexAtomType Type => _type;
    }

    // \hdotsfor[spacing]{n}: a run of dots across n columns of a matrix, standing in for a row of
    // entries left unwritten.
    private sealed class HDotsForCommand
    {
        public static HDotsForCommand Instance { get; } = new();
    }

    /// <summary>A document-level command (numbering, cross references, page breaks), read and dropped: a formula stands alone, so it has no page to affect.</summary>
    private sealed class DiscardedCommand
    {
        public static DiscardedCommand Bare { get; } = new(0, optional: false);
        public static DiscardedCommand BareOrOptional { get; } = new(0, optional: true);
        public static DiscardedCommand OneArgument { get; } = new(1, optional: false);
        public static DiscardedCommand TwoArguments { get; } = new(2, optional: false);
        public static DiscardedCommand ThreeArguments { get; } = new(3, optional: false);

        private readonly int _mandatory;
        private readonly bool _optional;

        private DiscardedCommand(int mandatory, bool optional)
        {
            _mandatory = mandatory;
            _optional = optional;
        }
    }

    /// <summary>
    /// A command whose LaTeX-level effect is page layout but whose argument is real maths -
    /// <c>\shoveleft</c> and <c>\shoveright</c>. The layout goes; the contents stay.
    /// </summary>
    internal sealed class TransparentCommand
    {
        public static TransparentCommand Instance { get; } = new();
    }

    /// <summary>
    /// A display environment that carries nothing beyond its contents here: a formula in a markdown
    /// document is already its own display, with no page, no equation numbers, and no margins to be
    /// flush with. The wrapper is dropped and the body parsed in its place.
    /// </summary>
    internal sealed class TransparentEnvironment
    {
        public static TransparentEnvironment Instance { get; } = new();
    }

    /// <summary>
    /// An <c>alignat</c>-family environment: the alignment of <c>align</c>, preceded by a count of the
    /// column pairs. That count exists to set inter-column spacing across a page of text, and has
    /// nothing to govern here, so it is read and dropped.
    /// </summary>
    private sealed class CountedAlignEnvironment
    {
        public static CountedAlignEnvironment Instance { get; } = new();
    }

    internal static readonly IReadOnlyDictionary<string, object?> Dictionary =
        new Dictionary<string, object?>
        {
            [@"\"] = new NewLineCommand(),
            ["binom"] = BinomCommand.Plain,
            ["dbinom"] = BinomCommand.Display,
            ["tbinom"] = BinomCommand.Text,
            // The braket package. The capitalised forms are its "always stretch" variants, which is
            // what a fence does anyway — they are here so that copied source keeps working.
            ["bra"] = BraketCommand.Bra,
            ["Bra"] = BraketCommand.Bra,
            ["ket"] = BraketCommand.Ket,
            ["Ket"] = BraketCommand.Ket,
            ["braket"] = BraketCommand.Braket,
            ["Braket"] = BraketCommand.Braket,
            ["cancel"] = CancelCommand.Cancel,
            ["bcancel"] = CancelCommand.BCancel,
            ["xcancel"] = CancelCommand.XCancel,
            ["cases"] = MatrixCommandParser.Cases,
            ["matrix"] = MatrixCommandParser.Matrix,
            ["pmatrix"] = MatrixCommandParser.PMatrix,
            ["bmatrix"] = MatrixCommandParser.BMatrix,
            ["Bmatrix"] = MatrixCommandParser.BbMatrix,
            ["vmatrix"] = MatrixCommandParser.VMatrix,
            ["Vmatrix"] = MatrixCommandParser.VvMatrix,
            ["underline"] = new UnderlineCommand(),
            ["overrightarrow"] = OverArrowCommand.Right,
            ["overleftarrow"] = OverArrowCommand.Left,
            ["overleftrightarrow"] = OverArrowCommand.Both,
            ["underrightarrow"] = OverArrowCommand.UnderRight,
            ["underleftarrow"] = OverArrowCommand.UnderLeft,
            ["underleftrightarrow"] = OverArrowCommand.UnderBoth,
            ["vdots"] = DotsCommand.Vertical,
            ["ddots"] = DotsCommand.Diagonal,
            ["hspace"] = HspaceCommand.Hspace,
            ["mspace"] = HspaceCommand.Mspace,
            ["dfrac"] = FracStyleCommand.Dfrac,
            ["tfrac"] = FracStyleCommand.Tfrac,
            ["cfrac"] = new CfracCommand(),
            ["nicefrac"] = new SlashFractionCommand(),
            ["sfrac"] = new SlashFractionCommand(),
            ["xrightarrow"] = ExtensibleArrowCommand.Right,
            ["xleftarrow"] = ExtensibleArrowCommand.Left,
            ["xleftrightarrow"] = ExtensibleArrowCommand.Both,
            ["xRightarrow"] = ExtensibleArrowCommand.DoubleRight,
            ["xLeftarrow"] = ExtensibleArrowCommand.DoubleLeft,
            ["xLeftrightarrow"] = ExtensibleArrowCommand.DoubleBoth,
            ["xmapsto"] = ExtensibleArrowCommand.MapsTo,
            ["overbrace"] = BraceCommand.Over,
            ["underbrace"] = BraceCommand.Under,
            ["substack"] = MatrixCommandParser.SubStack,
            ["hdotsfor"] = HDotsForCommand.Instance,

            // Retyping commands, and the one escaped literal that has no glyph to be.
            ["mathord"] = AtomTypeCommand.Ordinary,
            ["mathop"] = AtomTypeCommand.Operator,
            ["mathbin"] = AtomTypeCommand.Binary,
            ["mathrel"] = AtomTypeCommand.Relation,
            ["mathopen"] = AtomTypeCommand.Opening,
            ["mathclose"] = AtomTypeCommand.Closing,
            ["mathpunct"] = AtomTypeCommand.Punctuation,
            ["mathinner"] = AtomTypeCommand.Inner,
            ["_"] = UnderscoreCommand.Instance,
            ["genfrac"] = GenFracCommand.Instance,            ["big"] = new BigDelimiterCommand(0, TexAtomType.Ordinary),
            ["bigl"] = new BigDelimiterCommand(0, TexAtomType.Opening),
            ["bigr"] = new BigDelimiterCommand(0, TexAtomType.Closing),
            ["bigm"] = new BigDelimiterCommand(0, TexAtomType.Relation),
            ["Big"] = new BigDelimiterCommand(1, TexAtomType.Ordinary),
            ["Bigl"] = new BigDelimiterCommand(1, TexAtomType.Opening),
            ["Bigr"] = new BigDelimiterCommand(1, TexAtomType.Closing),
            ["Bigm"] = new BigDelimiterCommand(1, TexAtomType.Relation),
            ["bigg"] = new BigDelimiterCommand(2, TexAtomType.Ordinary),
            ["biggl"] = new BigDelimiterCommand(2, TexAtomType.Opening),
            ["biggr"] = new BigDelimiterCommand(2, TexAtomType.Closing),
            ["biggm"] = new BigDelimiterCommand(2, TexAtomType.Relation),
            ["Bigg"] = new BigDelimiterCommand(3, TexAtomType.Ordinary),
            ["Biggl"] = new BigDelimiterCommand(3, TexAtomType.Opening),
            ["Biggr"] = new BigDelimiterCommand(3, TexAtomType.Closing),
            ["Biggm"] = new BigDelimiterCommand(3, TexAtomType.Relation),
            ["operatorname"] = new OperatorNameCommand(),
        ["operatorname*"] = new OperatorNameCommand(starred: true),
            ["boldsymbol"] = new BoldSymbolCommand(),
            ["bm"] = new BoldSymbolCommand(),
            ["pmb"] = new BoldSymbolCommand(),
            ["boxed"] = new BoxedCommand(),
            ["fbox"] = new BoxedCommand(),
            ["phantom"] = PhantomCommand.Both,
            ["hphantom"] = PhantomCommand.Horizontal,
            ["vphantom"] = PhantomCommand.Vertical,
            ["smash"] = SmashCommand.Smash,
            ["mathllap"] = SmashCommand.Llap,
            ["mathrlap"] = SmashCommand.Rlap,
            ["mathclap"] = SmashCommand.Clap,
            ["llap"] = SmashCommand.Llap,
            ["rlap"] = SmashCommand.Rlap,
            ["clap"] = SmashCommand.Clap,
            ["overset"] = StackedAnnotationCommand.Overset,
            ["underset"] = StackedAnnotationCommand.Underset,
            ["stackrel"] = StackedAnnotationCommand.Stackrel,
            ["displaystyle"] = StyleCommand.Display,
            ["textstyle"] = StyleCommand.Text,
            ["scriptstyle"] = StyleCommand.Script,
            ["scriptscriptstyle"] = StyleCommand.ScriptScript,

            // The plain-TeX switches a paper is written with; see FontSwitchCommand and StyleCommand.
            ["cal"] = FontSwitchCommand.Calligraphic,
            ["bf"] = FontSwitchCommand.Bold,
            ["it"] = FontSwitchCommand.Italic,
            ["mit"] = FontSwitchCommand.Italic,   // plain TeX's name for the maths italic
            ["rm"] = FontSwitchCommand.Roman,
            ["sf"] = FontSwitchCommand.SansSerif,
            ["tt"] = FontSwitchCommand.Typewriter,
            ["frak"] = FontSwitchCommand.Fraktur,
            ["scr"] = FontSwitchCommand.Script,
            ["tiny"] = StyleCommand.ScriptScript,
            ["scriptsize"] = StyleCommand.Script,
            ["footnotesize"] = StyleCommand.Unchanged,
            ["small"] = StyleCommand.Unchanged,
            ["normalsize"] = StyleCommand.Unchanged,
            ["large"] = StyleCommand.Unchanged,
            ["Large"] = StyleCommand.Unchanged,
            ["LARGE"] = StyleCommand.Unchanged,
            ["huge"] = StyleCommand.Unchanged,
            ["Huge"] = StyleCommand.Unchanged,
            ["pmod"] = ParenModCommand.Pmod,
            ["pod"] = ParenModCommand.Pod,
            ["mod"] = ParenModCommand.Mod,

            // Numbering, cross references and page layout: read and dropped. See DiscardedCommand.
            ["tag"] = DiscardedCommand.OneArgument,
            ["notag"] = DiscardedCommand.Bare,
            ["nonumber"] = DiscardedCommand.Bare,
            ["label"] = DiscardedCommand.OneArgument,
            ["eqref"] = DiscardedCommand.OneArgument,
            ["numberwithin"] = DiscardedCommand.TwoArguments,
            ["raisetag"] = DiscardedCommand.OneArgument,
            ["intertext"] = DiscardedCommand.OneArgument,
            ["shortintertext"] = DiscardedCommand.OneArgument,
            ["allowdisplaybreaks"] = DiscardedCommand.BareOrOptional,
            ["displaybreak"] = DiscardedCommand.BareOrOptional,
            ["nobreakdash"] = DiscardedCommand.Bare,
            ["accentedsymbol"] = DiscardedCommand.TwoArguments,
            ["DeclareMathOperator"] = DiscardedCommand.TwoArguments,
            ["DeclarePairedDelimiter"] = DiscardedCommand.ThreeArguments,
            ["shoveleft"] = TransparentCommand.Instance,
            ["shoveright"] = TransparentCommand.Instance,
            ["begin"] = null
        };

    /// <summary>
    /// What this command switches, when it is a switch rather than a command — <c>\cal</c>, <c>\bf</c>,
    /// <c>\displaystyle</c> and their kin. A command takes an argument; a switch takes <em>the rest of the group it
    /// stands in</em> (nothing but the closing brace says where the scope ends), so a builder working from its own
    /// reading has to know which it is holding — this is where that is written down.
    /// </summary>
    /// <param name="textStyle">The alphabet it switches to, or null.</param>
    /// <param name="style">The size it switches to, or null — including for a switch that changes neither.</param>
    /// <returns>Whether it is a switch at all.</returns>
    internal static bool IsSwitch(string command, out string? textStyle, out TexStyle? style)
    {
        textStyle = null;
        style = null;

        if (!Dictionary.TryGetValue(command, out var parser)) return false;

        switch (parser)
        {
            case FontSwitchCommand font: textStyle = font.TextStyle; return true;
            case StyleCommand sized: style = sized.Style; return true;
            default: return false;
        }
    }

    internal static readonly IReadOnlyDictionary<string, object> Environments =
        new Dictionary<string, object>
        {
            ["array"] = ArrayCommandParser.Instance,
            ["align"] = MatrixCommandParser.Align,
            ["align*"] = MatrixCommandParser.Align,
            ["aligned"] = MatrixCommandParser.Align,
            ["split"] = MatrixCommandParser.Align,
            ["gather"] = MatrixCommandParser.Gathered,
            ["gather*"] = MatrixCommandParser.Gathered,
            ["gathered"] = MatrixCommandParser.Gathered,
            ["cases"] = MatrixCommandParser.Cases,
            ["matrix"] = MatrixCommandParser.Matrix,
            ["smallmatrix"] = MatrixCommandParser.SmallMatrix,
            ["pmatrix"] = MatrixCommandParser.PMatrix,
            ["bmatrix"] = MatrixCommandParser.BMatrix,
            ["Bmatrix"] = MatrixCommandParser.BbMatrix,
            ["vmatrix"] = MatrixCommandParser.VMatrix,
            ["Vmatrix"] = MatrixCommandParser.VvMatrix,

            // The display environments. None of them mean anything more than their contents in a
            // formula that is already a display of its own; see TransparentEnvironment.
            ["equation"] = TransparentEnvironment.Instance,
            ["equation*"] = TransparentEnvironment.Instance,
            ["subequations"] = TransparentEnvironment.Instance,
            ["multline"] = MatrixCommandParser.Gathered,
            ["multline*"] = MatrixCommandParser.Gathered,
            ["flalign"] = MatrixCommandParser.Align,
            ["flalign*"] = MatrixCommandParser.Align,
            ["alignat"] = CountedAlignEnvironment.Instance,
            ["alignat*"] = CountedAlignEnvironment.Instance,
            ["alignedat"] = CountedAlignEnvironment.Instance,
            ["xalignat"] = CountedAlignEnvironment.Instance,
            ["xalignat*"] = CountedAlignEnvironment.Instance,
            ["xxalignat"] = CountedAlignEnvironment.Instance,
            ["xxalignat*"] = CountedAlignEnvironment.Instance
        };

    /// <summary>
    /// Written-down space, in mu — eighteenths of a quad. Not macros: a macro stands for something that could be
    /// written out longhand, but a strut of four mu stands for nothing but itself, since LaTeX has no way of
    /// saying it — so it belongs here with the symbols, the other thing the reader cannot spell.
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
}
