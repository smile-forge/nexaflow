using System.Collections.Generic;
using System.Globalization;
using System.IO.Pipes;
using XamlMath.Atoms;
using XamlMath.Boxes;
using XamlMath.Exceptions;
using XamlMath.Parsers.Matrices;
using System;

namespace XamlMath.Parsers;

internal static class StandardCommands
{
    private class UnderlineCommand
    {
    }

    // The stretchy arrow accents: an arrow drawn to the width of its argument, above or below it.
    private sealed class OverArrowCommand : IAssembleCommand
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

        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count == 1
                ? new OverArrowAtom(null, arguments[0], _decoration, _over) { Origin = origin }
                : null;
    }

    // \vdots and \ddots take no argument; they just emit a fixed run of dots.
    private sealed class DotsCommand : IAssembleCommand
    {
        public static DotsCommand Vertical { get; } = new(DotsAtom.DotsShape.Vertical);
        public static DotsCommand Diagonal { get; } = new(DotsAtom.DotsShape.Diagonal);

        private readonly DotsAtom.DotsShape _shape;

        private DotsCommand(DotsAtom.DotsShape shape)
        {
            _shape = shape;
        }

        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count == 0 ? new DotsAtom(null, _shape) { Origin = origin } : null;
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

    /// <summary>
    /// The space a length-taking command asks for — <c>\mspace{-3mu}</c>, <c>\hspace{2em}</c> — read
    /// from the argument as it was written.
    ///
    /// <para>
    /// A length is text and only text: <c>-3mu</c> is not something that survives being built into
    /// atoms, so this is the one thing that cannot come through <see cref="IAssembleCommand"/> like the
    /// rest of them. It does not need to. Whoever is building holds the parse tree, the tree holds the
    /// argument, and the argument holds its own characters — so it is asked for them directly.
    /// </para>
    /// <para>
    /// Null where the text is not a length at all, which is a formula saying something nobody can act
    /// on rather than a reason to stop.
    /// </para>
    /// </summary>
    internal static Atom? SpaceOf(string command, string written)
    {
        try
        {
            ParseLength(written, command, out var unit, out var value);
            return new SpaceAtom(null, unit, value, 0, 0);
        }
        catch
        {
            return null;
        }
    }

    // \dfrac and \tfrac: \frac forced into display or text style respectively.
    private sealed class FracStyleCommand : IAssembleCommand
    {
        public static FracStyleCommand Dfrac { get; } = new(TexStyle.Display);
        public static FracStyleCommand Tfrac { get; } = new(TexStyle.Text);

        private readonly TexStyle _style;

        private FracStyleCommand(TexStyle style)
        {
            _style = style;
        }

        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count == 2
                ? new FractionAtom(null, arguments[0], arguments[1], true) { OverrideStyle = _style, Origin = origin }
                : null;
    }

    // \cfrac[l|c|r]{a}{b}: a continued-fraction fraction — display style throughout (nested \cfrac stays
    // full size) with an optional numerator alignment.
    private sealed class CfracCommand : IAssembleCommand
    {
        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count == 2
                ? new FractionAtom(null, arguments[0], arguments[1], true, TexAlignment.Center, TexAlignment.Center)
                  { OverrideStyle = TexStyle.Display, KeepContentStyle = true, Origin = origin }
                : null;
    }

    // \nicefrac{a}{b} and \sfrac{a}{b}: an inline "slash" fraction (raised numerator / lowered denominator).
    private sealed class SlashFractionCommand : IAssembleCommand
    {
        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count == 2
                ? new SlashFractionAtom(null, arguments[0], arguments[1]) { Origin = origin }
                : null;
    }

    // \pmod{n} -> "(mod n)" after a wide space; \pod{n} -> "(n)". Used as e.g. a \equiv b \pmod{n}.
    private sealed class ParenModCommand : IAssembleCommand
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

        /// <summary>
        /// <c>(mod n)</c>, built round an argument already read.
        /// <para>
        /// The parentheses and the word are this command's, not the reader's — they are why <c>\pmod{n}</c>
        /// is one thing to point at and not just an n. Assembled here rather than by re-reading a string
        /// of LaTeX, because a formula built from a parse tree should not have to write LaTeX to get one.
        /// </para>
        /// </summary>
        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin)
        {
            if (arguments.Count != 1) return null;

            var inside = new RowAtom(null);

            // The same pieces the written-out form would have produced, asked for by name rather than
            // guessed at. `\quad` and `\;` are macros with definitions; inventing two space atoms of
            // roughly the right size instead moved every \pmod in the corpus a fraction of an em.
            if (_withMod)
            {
                foreach (var letter in "mod") inside = inside.Add(new CharAtom(null, letter, "mathrm"));

                if (PrimitiveOf("thickspace") is { } thin) inside = inside.Add(thin);
            }

            inside = inside.Add(arguments[0]);

            // Without the parentheses, the word and its argument are the whole of it.
            if (!_fenced)
            {
                var bare = new RowAtom(null);
                if (PrimitiveOf("quad") is { } lead) bare = bare.Add(lead);
                bare = bare.Add(inside);
                bare.Origin = origin;
                return bare;
            }

            var fenced = new FencedAtom(
                null,
                inside,
                new SymbolAtom(null, "(", TexAtomType.Opening, true) { Origin = origin },
                new SymbolAtom(null, ")", TexAtomType.Closing, true) { Origin = origin })
            {
                Origin = origin,
            };

            var whole = new RowAtom(null);
            if (PrimitiveOf("quad") is { } gap) whole = whole.Add(gap);
            whole = whole.Add(fenced);

            whole.Origin = origin;
            return whole;
        }
    }

    /// <summary>
    /// <c>\genfrac{l}{r}{thickness}{style}{numerator}{denominator}</c> — the general fraction that every
    /// other one in amsmath is spelled with.
    ///
    /// <para>
    /// Assembled from arguments already built, like every other command here. It used to be read instead:
    /// a command parser that walked the source itself, which meant it worked only for the engine's own
    /// reader and set nothing at all once that reader stopped drawing. It was believed done, and the test
    /// that said so was asking the old reader.
    /// </para>
    /// <para>
    /// An empty delimiter argument means no delimiter that side, and an empty thickness means the default
    /// rule — which is the one place a fraction's bar is written as a length rather than implied.
    /// </para>
    /// </summary>
    private sealed class GenFracCommand : IAssembleCommand
    {
        public static GenFracCommand Instance { get; } = new();

        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin)
        {
            if (arguments.Count != 6) return null;

            // Written as `{}` where there is to be none, so an argument that drew nothing is not a
            // delimiter that failed to build — it is a side the writer asked to leave open.
            SymbolAtom? Delimiter(Atom written) =>
                written is SymbolAtom symbol ? symbol : null;

            var fraction = new FractionAtom(null, arguments[4], arguments[5], true) { Origin = origin };

            var left = Delimiter(arguments[0]);
            var right = Delimiter(arguments[1]);
            if (left is null && right is null) return fraction;

            return new FencedAtom(null, fraction, left, right) { Origin = origin };
        }
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
    private sealed class StackedAnnotationCommand
    {
        public static StackedAnnotationCommand Overset { get; } = new(over: true, asRelation: false);
        public static StackedAnnotationCommand Underset { get; } = new(over: false, asRelation: false);
        public static StackedAnnotationCommand Stackrel { get; } = new(over: true, asRelation: true);

        private const double AnnotationSpace = 2.5; // mu, the same order as the \overbrace-style annotations

        private readonly bool _over;
        private readonly bool _asRelation;

        private StackedAnnotationCommand(bool over, bool asRelation)
        {
            _over = over;
            _asRelation = asRelation;
        }

        /// <summary>
        /// The atom this command makes of an annotation and a base already built.
        /// <para>
        /// Which of the two goes on top, and whether the whole reads as a relation, are this table's
        /// answers — <c>\stackrel</c> relates where <c>\overset</c> merely decorates — so a second reading
        /// asks rather than copies.
        /// </para>
        /// </summary>
        internal Atom Assemble(
            Atom annotation, Atom on, SourceSpan? source, Nexaflow.Maths.Latex.TexPart? origin)
        {
            Atom atom = new UnderOverAtom(source, on, annotation, TexUnit.Mu, AnnotationSpace, true, _over)
            {
                Origin = origin,
            };

            return _asRelation
                ? new TypedAtom(source, atom, TexAtomType.Relation, TexAtomType.Relation) { Origin = origin }
                : atom;
        }
    }

    // \phantom{x} and its one-dimensional variants: the content is measured and then not drawn, so it reserves
    // space without printing anything.
    private sealed class PhantomCommand : IAssembleCommand
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

        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count == 1
                ? new PhantomAtom(null, arguments[0], _useWidth, _useHeight, _useHeight) { Origin = origin }
                : null;
    }

    // \smash{x} draws the content and reports no height, \math?lap{x} draws it and reports no width. Both are the
    // inverse of \phantom: ink without extent rather than extent without ink.
    private sealed class SmashCommand : IAssembleCommand
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

        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count != 1
                ? null
                : _lapAlignment is { } alignment
                    ? new LapAtom(null, arguments[0], alignment) { Origin = origin }
                    : new SmashAtom(null, arguments[0]) { Origin = origin };
    }

    // \boxed{x} and \fbox{x}: the content inside a rectangular frame.
    private sealed class BoxedCommand : IAssembleCommand
    {
        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count == 1 ? new BoxedAtom(null, arguments[0]) { Origin = origin } : null;
    }

    // \xrightarrow[under]{over} and friends: an arrow stretched to fit the labels written over (and optionally
    // under) it. The under label is the optional argument, as in LaTeX.
    private sealed class ExtensibleArrowCommand : IAssembleCommand
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

        /// <summary>The arrow, its label over it and — where one was written — its label under it.</summary>
        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count is 1 or 2
                ? new ExtensibleArrowAtom(
                    null,
                    arguments[^1],
                    arguments.Count == 2 ? arguments[0] : null,
                    _decoration)
                {
                    Origin = origin,
                }
                : null;
    }

    // \overbrace{body}^{label} and \underbrace{body}_{label}: a brace stretched to the width of the
    // body, with an optional label beyond it. LaTeX makes these operators, so a script written after
    // one belongs above (or below) the brace rather than beside it — which means reading it here,
    // before the parser attaches it as an ordinary script.
    private sealed class BraceCommand : IAssembleCommand
    {
        public static BraceCommand Over { get; } = new(over: true);
        public static BraceCommand Under { get; } = new(over: false);

        private const double LabelKern = 0.5; // ex, between the brace and its label

        private readonly bool _over;

        private BraceCommand(bool over)
        {
            _over = over;
        }

        /// <summary>
        /// The brace over or under one thing, and the label written on it.
        ///
        /// <para>
        /// The <c>_{n}</c> of <c>\underbrace{a+b}_{n}</c> is the brace's label and not a subscript on it:
        /// it sits centred under the brace rather than beside what the brace covers. Which is why this
        /// takes a label at all — a reading that nests the script around the whole command has the two
        /// apart, and handing them over separately is what puts them back together.
        /// </para>
        /// <para>
        /// Which side counts is this command's own: <c>\overbrace</c> takes a <c>^</c> and
        /// <c>\underbrace</c> a <c>_</c>, so a script written on the other side is an ordinary script and
        /// is none of its business.
        /// </para>
        /// </summary>
        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            Labelled(arguments.Count == 1 ? arguments[0] : null, null, origin);

        /// <summary>Whether a label on this side belongs to the brace: <c>^</c> over, <c>_</c> under.</summary>
        public bool Labels(bool over) => over == _over;

        public Atom? Labelled(Atom? on, Atom? label, Nexaflow.Maths.Latex.TexPart? origin) =>
            on is null
                ? null
                : new OverUnderDelimiter(
                    null,
                    on,
                    label,
                    SymbolAtom.GetAtom(
                        TexFormulaParser.DelimiterNames[(int)TexDelimiter.Brace][
                            (int)(_over ? TexDelimeterType.Over : TexDelimeterType.Under)],
                        null),
                    TexUnit.Ex,
                    LabelKern,
                    _over)
                {
                    Origin = origin,
                };
    }

    // \boldsymbol{…} (also spelled \bm): every character underneath comes from the bold companion of
    // the font it would otherwise use, which is what makes it work on Greek letters and symbols
    // rather than only on the Latin ones a text style could reach.
    private sealed class BoldSymbolCommand : IAssembleCommand
    {
        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count == 1 ? new BoldAtom(null, arguments[0]) { Origin = origin } : null;
    }

    // \operatorname{name} sets a function name upright and, more importantly, types it as an
    // operator: that is what gives it operator spacing and lets a following script become a limit.
    // The starred form takes its limits above and below in display style, as \sum does.
    private sealed class OperatorNameCommand : IAssembleCommand
    {
        /// <param name="starred">
        /// Whether this is the <c>*</c> form, whose limits go wherever the style puts them rather than
        /// always beside the name. Reading from source finds the star for itself; a reading that arrives
        /// with its arguments already built cannot, so the table registers the two spellings separately
        /// and this is how they differ.
        /// </param>
        public OperatorNameCommand(bool starred = false) => this._starred = starred;

        private readonly bool _starred;

        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count == 1
                ? new BigOperatorAtom(null, arguments[0], null, null, this._starred ? null : (bool?)false) { Origin = origin }
                : null;
    }

    // inom{n}{k}, and \dbinom / 	binom which force display or text style. amsmath spells all
    // three as \genfrac{(}{)}{0pt}{}: a fraction with no rule drawn, inside parentheses.
    private sealed class BinomCommand : IAssembleCommand
    {
        public static BinomCommand Plain { get; } = new(null);
        public static BinomCommand Display { get; } = new(TexStyle.Display);
        public static BinomCommand Text { get; } = new(TexStyle.Text);

        private readonly TexStyle? _style;

        private BinomCommand(TexStyle? style)
        {
            _style = style;
        }

        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin)
        {
            if (arguments.Count != 2) return null;

            var fraction = new FractionAtom(null, arguments[0], arguments[1], TexUnit.Point, 0)
            {
                SuppressNullDelimiterSpace = true,
            };

            if (_style is { } style) fraction = fraction with { OverrideStyle = style };

            // Every piece carries the part, as a bracketed matrix's do: a binomial is one construct drawn
            // in several atoms — the brackets, the stack, the space they leave — and all of them came
            // from this one \binom. Tagging only the outermost would leave the stack knowing nothing.
            fraction.Origin = origin;

            return new FencedAtom(
                null,
                fraction,
                new SymbolAtom(null, "(", TexAtomType.Opening, true) { Origin = origin },
                new SymbolAtom(null, ")", TexAtomType.Closing, true) { Origin = origin })
            {
                Origin = origin,
            };
        }
    }

    /// <summary>
    /// The braket package's Dirac notation: <c>\bra{A}</c> is ⟨A|, <c>\ket{B}</c> is |B⟩, and
    /// <c>\braket{A|B}</c> is ⟨A|B⟩.
    /// <para>
    /// A fence, like every other bracketed thing, so the delimiters grow with what is between them and
    /// the editor already knows how to select, carry and un-render one. The capitalised forms are the
    /// package's "always stretch" variants, which is what a fence does anyway — they exist so that
    /// copied source keeps working rather than to render differently.
    /// </para>
    /// <para>
    /// The bar in <c>\braket{A|B}</c> is left where it is, as the character it already is. Splitting on
    /// it to name the two halves would be a better parse — a bra and a ket are parts in the sense a
    /// numerator is — but it is not what makes the notation render, and a divider that is sometimes a
    /// separator and sometimes an ordinary bar is worth getting right on purpose rather than in passing.
    /// </para>
    /// </summary>
    private sealed class BraketCommand : IAssembleCommand
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

        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count == 1
                ? new FencedAtom(
                    null,
                    arguments[0],
                    new SymbolAtom(null, _open, TexAtomType.Opening, true) { Origin = origin },
                    new SymbolAtom(null, _close, TexAtomType.Closing, true) { Origin = origin })
                {
                    Origin = origin,
                }
                : null;
    }

    private sealed class CancelCommand : IAssembleCommand
    {
        public static CancelCommand BCancel { get; } = new(StrokeBoxMode.Back);
        public static CancelCommand Cancel { get; } = new(StrokeBoxMode.Normal);
        public static CancelCommand XCancel { get; } = new(StrokeBoxMode.Both);

        private CancelCommand(StrokeBoxMode strokeBoxMode)
        {
            _strokeBoxMode = strokeBoxMode;
        }

        private readonly StrokeBoxMode _strokeBoxMode;

        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count == 1
                ? new CancelAtom(null, arguments[0], _strokeBoxMode) { Origin = origin }
                : null;
    }

    /// <summary>
    /// This command will parse the remaining part of an input string, and add it onto a new line of a formula. The
    /// new line is created as a <see cref="MatrixAtom"/>; the command will try to reuse existing atoms if possible.
    /// </summary>
    private class NewLineCommand
    {
    }

    // \mathop{…} and its family: the argument keeps its shape and changes its kind, which is what
    // decides the space around it. A paper reaches for \mathop where a name should behave as an
    // operator and for \mathrel where a symbol should behave as a relation.
    private sealed class AtomTypeCommand : IAssembleCommand
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

        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count == 1
                ? new TypedAtom(null, arguments[0], _type, _type) { Origin = origin }
                : null;
    }

    // \_ : there is no underscore in the text encoding, so LaTeX draws one - a rule 0.3em wide,
    // sitting a little below the baseline. Its neighbours \# \$ \% \& are ordinary glyphs and are
    // handled as symbols instead.
    private sealed class UnderscoreCommand
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
    /// <summary>
    /// Whether this command draws nothing at all — its effect belongs to a page rather than to a formula,
    /// as <c>\tag</c> and <c>\nonumber</c> do. Asked so that a second reading discards exactly what this
    /// one discards, rather than keeping its own list of which those are.
    /// </summary>
    internal static bool IsDiscarded(string command) =>
        Dictionary.TryGetValue(command, out var parser) && parser is DiscardedCommand;

    /// <summary>
    /// A brace with its label, where this command is one that takes one on that side — <c>\overbrace</c>
    /// on a <c>^</c>, <c>\underbrace</c> on a <c>_</c>. Null for anything else, including a brace scripted
    /// on the side it does not label, which is then an ordinary script like any other.
    /// </summary>
    internal static Atom? LabelledBraceOf(
        string command, bool over, Atom on, Atom label, Nexaflow.Maths.Latex.TexPart? origin) =>
        Dictionary.TryGetValue(command, out var parser) && parser is BraceCommand brace && brace.Labels(over)
            ? brace.Labelled(on, label, origin)
            : null;

    /// <summary>Whether this command can be built from arguments somebody else has already read.</summary>
    internal static bool CanAssemble(string command) =>
        Dictionary.TryGetValue(command, out var parser) && parser is IAssembleCommand;

    /// <summary>
    /// What this command makes of arguments already built, in the order it reads them — or null where it
    /// is not one that can be asked.
    /// </summary>
    internal static Atom? AssembledOf(
        string command,
        IReadOnlyList<Atom> arguments,
        TexFormulaParser knowledge,
        Nexaflow.Maths.Latex.TexPart? origin) =>
        Dictionary.TryGetValue(command, out var parser) && parser is IAssembleCommand assembler
            ? assembler.Assemble(arguments, knowledge, origin)
            : null;

    /// <summary>
    /// The stacked annotation this command stands for, given both parts already built — or null where the
    /// command sets nothing above or below anything.
    /// </summary>
    internal static Atom? StackedOf(
        string command, Atom annotation, Atom on, Nexaflow.Maths.Latex.TexPart? origin) =>
        Dictionary.TryGetValue(command, out var parser) && parser is StackedAnnotationCommand stacked
            ? stacked.Assemble(annotation, on, null, origin)
            : null;

    /// <summary>
    /// The sized delimiter this command stands for, given the delimiter already read — or null where the
    /// command is not one of the <c>\big</c> family at all.
    /// <para>
    /// The question asked from outside, so that which commands those are, how big each one is and whether
    /// it opens, closes or relates stay answers this table gives rather than facts copied out of it.
    /// </para>
    /// </summary>
    internal static Atom? BigDelimiterOf(
        string command, string delimiter, Nexaflow.Maths.Latex.TexPart? origin) =>
        Dictionary.TryGetValue(command, out var parser) && parser is BigDelimiterCommand big
            ? big.Assemble(delimiter, null, origin)
            : null;

    private sealed class BigDelimiterCommand
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

        /// <summary>
        /// The atom this command makes of a delimiter that has already been read.
        ///
        /// <para>
        /// Split out so that a reading done elsewhere gets the same size and the same class from the same
        /// place. How big a <c>\Bigg</c> is and whether a <c>\bigl</c> opens or closes are this table's
        /// answers, and a second copy of them would be two tables to keep in step.
        /// </para>
        /// </summary>
        internal Atom Assemble(string delimiter, SourceSpan? source, Nexaflow.Maths.Latex.TexPart? origin) =>
            new BigDelimiterAtom(source, delimiter, SmallestHeight + HeightStep * _size, _type)
            {
                Origin = origin,
            };
    }

    // \hdotsfor[spacing]{n}: a run of dots across n columns of a matrix, standing in for a row of
    // entries left unwritten.
    private sealed class HDotsForCommand
    {
        public static HDotsForCommand Instance { get; } = new();
    }

    /// <summary>
    /// A document-level command - numbering, cross references, page breaks - read and dropped. A
    /// formula here stands alone: there is no page to break, nothing to number and nothing to refer
    /// to, so the command has no work left to do. Rejecting it would only make a formula lifted out
    /// of a paper unrenderable over a detail that could never have shown up anyway.
    /// </summary>
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
    private sealed class TransparentCommand : IAssembleCommand
    {
        public static TransparentCommand Instance { get; } = new();

        /// <summary>What was inside it, and nothing of its own: the layout goes, the contents stay.</summary>
        public Atom? Assemble(IReadOnlyList<Atom> arguments, TexFormulaParser knowledge, Nexaflow.Maths.Latex.TexPart? origin) =>
            arguments.Count == 1 ? arguments[0] : null;
    }

    /// <summary>
    /// A display environment that carries nothing beyond its contents here: a formula in a markdown
    /// document is already its own display, with no page, no equation numbers, and no margins to be
    /// flush with. The wrapper is dropped and the body parsed in its place.
    /// </summary>
    private sealed class TransparentEnvironment
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
    /// <c>\displaystyle</c> and their kin.
    /// <para>
    /// The distinction is not decoration. A command takes an argument; a switch takes <em>the rest of the
    /// group it stands in</em>, so <c>{\cal L}</c> and <c>{\cal L M}</c> differ in what is affected and
    /// nothing in either says where the scope ends except the closing brace. Anything building a formula
    /// out of its own reading has to know which it is holding, and this is where that is written down.
    /// </para>
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
    /// Written-down space, in mu — eighteenths of a quad.
    ///
    /// <para>
    /// These are not macros and never were, whatever table they used to live in. A macro stands for
    /// something somebody could have written out longhand; a strut of four mu stands for nothing but
    /// itself, because LaTeX has no way of saying it. So it belongs here, with the symbols, which are
    /// the other thing the typesetter knows how to draw and the reader cannot spell.
    /// </para>
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

    /// <summary>
    /// What this name draws, where the typesetter draws it itself rather than reading it out of
    /// anything — or null where the name means nothing to it.
    ///
    /// <para>
    /// Everything here reaches for something LaTeX cannot say: a length in mu, a sign lifted onto the
    /// axis, one glyph set over another at a fixed height without being shrunk. A name that <em>can</em>
    /// be said in LaTeX is a macro and belongs to the reader, in <c>TexMacros</c>; these are the
    /// residue, and they are the same kind of thing as a symbol.
    /// </para>
    /// </summary>
    internal static Atom? PrimitiveOf(string name)
    {
        if (Struts.TryGetValue(name, out var mu)) return new SpaceAtom(null, TexUnit.Mu, mu, 0, 0);

        return name switch
        {
            // A radical sign with nothing under it, lifted so it sits about the axis.
            "surd" => new VerticalCenteredAtom(null, SymbolAtom.GetAtom("surdsign", null)),

            // A dot and a tilde set over an equals sign. TeX builds both the same way — `\doteq` is
            // `\buildrel\textstyle.\over=` — and the height is fixed rather than scaled, which is what
            // tells them apart from anything `\overset` could write.
            "doteq" => Over("equals", "ldotp", 2),
            "cong" => Over("equals", "sim", 1),

            // The \iint family is deliberately not here. It is several integral signs squeezed together
            // and typed as one big operator, and an integral sign is not a plain symbol — it is promoted
            // to a big operator on the way past, so reproducing the pile exactly means reproducing that
            // too. Tried, and it moved the typesetting. The corpus names \iint twenty times, \iiint once
            // and the other four never, so there is nothing to check a second attempt against; it stays
            // in the table it was already in until something needs it to move.
            _ => null,
        };
    }

    /// <summary>One glyph over another, at a fixed height and full size, and a relation either side.</summary>
    /// <summary>One glyph over another, at a fixed height and full size, and a relation either side.</summary>
    private static Atom Over(string under, string over, double mu) =>
        new TypedAtom(
            null,
            new UnderOverAtom(null, SymbolAtom.GetAtom(under, null), SymbolAtom.GetAtom(over, null),
                              TexUnit.Mu, mu, false, true),
            TexAtomType.Relation,
            TexAtomType.Relation);
}
