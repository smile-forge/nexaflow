using System.Collections.Generic;
using System.Globalization;
using System.IO.Pipes;
using XamlMath.Atoms;
using XamlMath.Boxes;
using XamlMath.Exceptions;
using XamlMath.Parsers.Matrices;

namespace XamlMath.Parsers;

internal static class StandardCommands
{
    private class UnderlineCommand : ICommandParser
    {
        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var position = context.ArgumentsStartPosition;
            var afterFormula = TexFormulaParser.ReadElement(source, position);
            position = afterFormula.position;
            var underlineFormula = context.Parser.Parse(
                afterFormula.source,
                context.Formula.TextStyle,
                context.Environment);
            var start = context.CommandNameStartPosition;
            var atomSource = source.Segment(start, position - start);
            var atom = new UnderlinedAtom(atomSource, underlineFormula.RootAtom);
            return new CommandProcessingResult(atom, position);
        }
    }

    // The stretchy arrow accents: an arrow drawn to the width of its argument, above or below it.
    private sealed class OverArrowCommand : ICommandParser
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

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var position = context.ArgumentsStartPosition;
            var afterFormula = TexFormulaParser.ReadElement(source, position);
            position = afterFormula.position;
            var baseFormula = context.Parser.Parse(
                afterFormula.source,
                context.Formula.TextStyle,
                context.Environment.CreateChildEnvironment());
            var start = context.CommandNameStartPosition;
            var atomSource = source.Segment(start, position - start);
            var atom = new OverArrowAtom(atomSource, baseFormula.RootAtom, _decoration, _over);
            return new CommandProcessingResult(atom, position);
        }
    }

    // \vdots and \ddots take no argument; they just emit a fixed run of dots.
    private sealed class DotsCommand : ICommandParser
    {
        public static DotsCommand Vertical { get; } = new(DotsAtom.DotsShape.Vertical);
        public static DotsCommand Diagonal { get; } = new(DotsAtom.DotsShape.Diagonal);

        private readonly DotsAtom.DotsShape _shape;

        private DotsCommand(DotsAtom.DotsShape shape)
        {
            _shape = shape;
        }

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var start = context.CommandNameStartPosition;
            var position = context.ArgumentsStartPosition;
            var atomSource = context.CommandSource.Segment(start, position - start);
            var atom = new DotsAtom(atomSource, _shape);
            return new CommandProcessingResult(atom, position);
        }
    }

    // \hspace{<length>} inserts horizontal space of an explicit length, e.g. \hspace{2em} or \hspace{-3pt}.
    private sealed class HspaceCommand : ICommandParser
    {
        public static HspaceCommand Hspace { get; } = new("hspace");

        /// <summary>amsmath's <c>\mspace</c>: the same thing, in math units.</summary>
        public static HspaceCommand Mspace { get; } = new("mspace");

        private readonly string _name;

        private HspaceCommand(string name)
        {
            _name = name;
        }

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var position = context.ArgumentsStartPosition;

            // \hspace* behaves identically here (there is no line breaking to make the space removable).
            if (position < source.Length && source[position] == '*')
                position++;

            var afterArg = TexFormulaParser.ReadElement(source, position);
            position = afterArg.position;
            ParseLength(afterArg.source.ToString(), "\\" + _name, out var unit, out var value);

            var start = context.CommandNameStartPosition;
            var atomSource = source.Segment(start, position - start);
            var atom = new SpaceAtom(atomSource, unit, value, 0, 0);
            return new CommandProcessingResult(atom, position);
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

    /// <summary>Reads one <c>{…}</c> (or single-token) argument as a formula and advances <paramref name="position"/>.</summary>
    private static TexFormula ReadArgument(CommandContext context, ref int position)
    {
        var start = TexFormulaParser.WithSkippedWhiteSpace(context.CommandSource, position);
        var after = TexFormulaParser.ReadElement(context.CommandSource, position);
        position = after.position;

        // An argument written as {} is a hole, not an absence — see WithPlaceholderIfEmpty. Taken from
        // here so that every command gets it at once: each of them reads its arguments through this and
        // then uses RootAtom, so none of them has to know that a hole is a thing.
        return TexFormulaParser.WithPlaceholderIfEmpty(
            context.Parser.Parse(after.source, context.Formula.TextStyle, context.Environment.CreateChildEnvironment()),
            context.CommandSource.Segment(start, position - start),
            context.Environment);
    }

    // \dfrac and \tfrac: \frac forced into display or text style respectively.
    private sealed class FracStyleCommand : ICommandParser
    {
        public static FracStyleCommand Dfrac { get; } = new(TexStyle.Display);
        public static FracStyleCommand Tfrac { get; } = new(TexStyle.Text);

        private readonly TexStyle _style;

        private FracStyleCommand(TexStyle style)
        {
            _style = style;
        }

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var position = context.ArgumentsStartPosition;
            var numerator = ReadArgument(context, ref position);
            var denominator = ReadArgument(context, ref position);
            var start = context.CommandNameStartPosition;
            var atomSource = context.CommandSource.Segment(start, position - start);
            var atom = new FractionAtom(atomSource, numerator.RootAtom, denominator.RootAtom, true)
            {
                OverrideStyle = _style
            };
            return new CommandProcessingResult(atom, position);
        }
    }

    // \cfrac[l|c|r]{a}{b}: a continued-fraction fraction — display style throughout (nested \cfrac stays
    // full size) with an optional numerator alignment.
    private sealed class CfracCommand : ICommandParser
    {
        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var position = context.ArgumentsStartPosition;

            var numeratorAlignment = TexAlignment.Center;
            var optional = TexFormulaParser.ReadElementGroupOptional(source, ref position, '[', ']')?.ToString().Trim();
            if (optional == "l") numeratorAlignment = TexAlignment.Left;
            else if (optional == "r") numeratorAlignment = TexAlignment.Right;

            var numerator = ReadArgument(context, ref position);
            var denominator = ReadArgument(context, ref position);
            var start = context.CommandNameStartPosition;
            var atomSource = source.Segment(start, position - start);
            var atom = new FractionAtom(
                atomSource, numerator.RootAtom, denominator.RootAtom, true, numeratorAlignment, TexAlignment.Center)
            {
                OverrideStyle = TexStyle.Display,
                KeepContentStyle = true
            };
            return new CommandProcessingResult(atom, position);
        }
    }

    // \nicefrac{a}{b} and \sfrac{a}{b}: an inline "slash" fraction (raised numerator / lowered denominator).
    private sealed class SlashFractionCommand : ICommandParser
    {
        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var position = context.ArgumentsStartPosition;
            var numerator = ReadArgument(context, ref position);
            var denominator = ReadArgument(context, ref position);
            var start = context.CommandNameStartPosition;
            var atomSource = context.CommandSource.Segment(start, position - start);
            var atom = new SlashFractionAtom(atomSource, numerator.RootAtom, denominator.RootAtom);
            return new CommandProcessingResult(atom, position);
        }
    }

    // \pmod{n} -> "(mod n)" after a wide space; \pod{n} -> "(n)". Used as e.g. a \equiv b \pmod{n}.
    private sealed class ParenModCommand : ICommandParser
    {
        public static ParenModCommand Pmod { get; } = new(withMod: true);
        public static ParenModCommand Pod { get; } = new(withMod: false);

        private readonly bool _withMod;

        private ParenModCommand(bool withMod)
        {
            _withMod = withMod;
        }

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var position = context.ArgumentsStartPosition;
            var afterArg = TexFormulaParser.ReadElement(source, position);
            position = afterArg.position;
            var argument = afterArg.source.ToString();

            var body = _withMod
                ? $@"\quad(\mathrm{{mod}}\;{{{argument}}})"
                : $@"\quad({{{argument}}})";
            var bodySpan = new SourceSpan("pmod", body, 0, body.Length);
            var formula = context.Parser.Parse(
                bodySpan, context.Formula.TextStyle, context.Environment.CreateChildEnvironment());
            return new CommandProcessingResult(formula.RootAtom, position);
        }
    }

    // \displaystyle, \textstyle, \scriptstyle and \scriptscriptstyle are switches, not one-argument commands:
    // they apply from where they appear to the end of the enclosing group. Reading only the next element would
    // leave the scripts of e.g. "\displaystyle\sum_{i=1}^{n}" outside the switch, which is where the style
    // actually matters (display style is what moves the limits above and below the operator).
    private sealed class StyleCommand : ICommandParser
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

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var start = context.CommandNameStartPosition;

            // The rest of the group is the argument. It keeps the current environment rather than a child one, so
            // that a switch inside a matrix cell doesn't swallow the row and cell separators.
            var rest = source.Segment(context.ArgumentsStartPosition);
            var formula = context.Parser.Parse(rest, context.Formula.TextStyle, context.Environment);

            var atomSource = source.Segment(start, source.Length - start);
            Atom atom = _style is { } style
                ? new StyleAtom(atomSource, formula.RootAtom, style)
                : formula.RootAtom ?? new NullAtom(atomSource);
            return new CommandProcessingResult(atom, source.Length);
        }
    }

    // \overset{ann}{base}, \underset{ann}{base} and \stackrel{ann}{rel}: the annotation is set in script size
    // above or below the base. \stackrel differs from \overset only in the spacing it gets: its result is a
    // relation (it exists to stack something over an arrow), so it is typed as one.
    private sealed class StackedAnnotationCommand : ICommandParser
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

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var position = context.ArgumentsStartPosition;
            var annotation = ReadArgument(context, ref position);
            var baseFormula = ReadArgument(context, ref position);
            var start = context.CommandNameStartPosition;
            var atomSource = context.CommandSource.Segment(start, position - start);

            Atom atom = new UnderOverAtom(
                atomSource,
                baseFormula.RootAtom,
                annotation.RootAtom,
                TexUnit.Mu,
                AnnotationSpace,
                true,
                _over);

            if (_asRelation)
                atom = new TypedAtom(atomSource, atom, TexAtomType.Relation, TexAtomType.Relation);

            return new CommandProcessingResult(atom, position);
        }
    }

    // \phantom{x} and its one-dimensional variants: the content is measured and then not drawn, so it reserves
    // space without printing anything.
    private sealed class PhantomCommand : ICommandParser
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

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var position = context.ArgumentsStartPosition;
            var content = ReadArgument(context, ref position);
            var start = context.CommandNameStartPosition;
            var atomSource = context.CommandSource.Segment(start, position - start);
            var atom = new PhantomAtom(atomSource, content.RootAtom, _useWidth, _useHeight, _useHeight);
            return new CommandProcessingResult(atom, position);
        }
    }

    // \smash{x} draws the content and reports no height, \math?lap{x} draws it and reports no width. Both are the
    // inverse of \phantom: ink without extent rather than extent without ink.
    private sealed class SmashCommand : ICommandParser
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

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var position = context.ArgumentsStartPosition;
            var content = ReadArgument(context, ref position);
            var start = context.CommandNameStartPosition;
            var atomSource = context.CommandSource.Segment(start, position - start);
            var atom = _lapAlignment is { } alignment
                ? (Atom)new LapAtom(atomSource, content.RootAtom, alignment)
                : new SmashAtom(atomSource, content.RootAtom);
            return new CommandProcessingResult(atom, position);
        }
    }

    // \boxed{x} and \fbox{x}: the content inside a rectangular frame.
    private sealed class BoxedCommand : ICommandParser
    {
        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var position = context.ArgumentsStartPosition;
            var content = ReadArgument(context, ref position);
            var start = context.CommandNameStartPosition;
            var atomSource = context.CommandSource.Segment(start, position - start);
            var atom = new BoxedAtom(atomSource, content.RootAtom);
            return new CommandProcessingResult(atom, position);
        }
    }

    // \xrightarrow[under]{over} and friends: an arrow stretched to fit the labels written over (and optionally
    // under) it. The under label is the optional argument, as in LaTeX.
    private sealed class ExtensibleArrowCommand : ICommandParser
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

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var position = context.ArgumentsStartPosition;

            var underSource = TexFormulaParser.ReadElementGroupOptional(source, ref position, '[', ']');
            var under = underSource == null
                ? null
                : context.Parser.Parse(
                    underSource,
                    context.Formula.TextStyle,
                    context.Environment.CreateChildEnvironment());

            var over = ReadArgument(context, ref position);

            var start = context.CommandNameStartPosition;
            var atomSource = source.Segment(start, position - start);
            var atom = new ExtensibleArrowAtom(atomSource, over.RootAtom, under?.RootAtom, _decoration);
            return new CommandProcessingResult(atom, position);
        }
    }

    // \overbrace{body}^{label} and \underbrace{body}_{label}: a brace stretched to the width of the
    // body, with an optional label beyond it. LaTeX makes these operators, so a script written after
    // one belongs above (or below) the brace rather than beside it — which means reading it here,
    // before the parser attaches it as an ordinary script.
    private sealed class BraceCommand : ICommandParser
    {
        public static BraceCommand Over { get; } = new(over: true);
        public static BraceCommand Under { get; } = new(over: false);

        private const double LabelKern = 0.5; // ex, between the brace and its label

        private readonly bool _over;

        private BraceCommand(bool over)
        {
            _over = over;
        }

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var position = context.ArgumentsStartPosition;
            var body = ReadArgument(context, ref position);

            Atom? label = null;
            var afterBody = TexFormulaParser.WithSkippedWhiteSpace(source, position);
            if (afterBody < source.Length && source[afterBody] == (_over ? '^' : '_'))
            {
                position = afterBody + 1;
                label = ReadArgument(context, ref position).RootAtom;
            }

            var start = context.CommandNameStartPosition;
            var atomSource = source.Segment(start, position - start);

            // The brace is a vertical delimiter stretched to the body's width and drawn rotated, so the
            // two halves of the curly-brace pair are what open upwards and downwards.
            var name = TexFormulaParser.DelimiterNames[(int)TexDelimiter.Brace][
                (int)(_over ? TexDelimeterType.Over : TexDelimeterType.Under)];

            var atom = new OverUnderDelimiter(
                atomSource,
                body.RootAtom,
                label,
                SymbolAtom.GetAtom(name, atomSource),
                TexUnit.Ex,
                LabelKern,
                _over);
            return new CommandProcessingResult(atom, position);
        }
    }

    // \boldsymbol{…} (also spelled \bm): every character underneath comes from the bold companion of
    // the font it would otherwise use, which is what makes it work on Greek letters and symbols
    // rather than only on the Latin ones a text style could reach.
    private sealed class BoldSymbolCommand : ICommandParser
    {
        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var position = context.ArgumentsStartPosition;
            var content = ReadArgument(context, ref position);
            var start = context.CommandNameStartPosition;
            var atomSource = context.CommandSource.Segment(start, position - start);
            var atom = new BoldAtom(atomSource, content.RootAtom);
            return new CommandProcessingResult(atom, position);
        }
    }

    // \operatorname{name} sets a function name upright and, more importantly, types it as an
    // operator: that is what gives it operator spacing and lets a following script become a limit.
    // The starred form takes its limits above and below in display style, as \sum does.
    private sealed class OperatorNameCommand : ICommandParser
    {
        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var position = context.ArgumentsStartPosition;

            var starred = position < source.Length && source[position] == '*';
            if (starred)
                position++;

            var after = TexFormulaParser.ReadElement(source, position);
            position = after.position;
            var name = context.Parser.Parse(after.source, "mathrm", context.Environment.CreateChildEnvironment());

            var start = context.CommandNameStartPosition;
            var atomSource = source.Segment(start, position - start);

            // null lets the style decide, which is what the starred form means; false keeps the
            // limits beside the name whatever the style, which is the plain form.
            var atom = new BigOperatorAtom(atomSource, name.RootAtom, null, null, starred ? null : (bool?)false);
            return new CommandProcessingResult(atom, position);
        }
    }

    // inom{n}{k}, and \dbinom / 	binom which force display or text style. amsmath spells all
    // three as \genfrac{(}{)}{0pt}{}: a fraction with no rule drawn, inside parentheses.
    private sealed class BinomCommand : ICommandParser
    {
        public static BinomCommand Plain { get; } = new(null);
        public static BinomCommand Display { get; } = new(TexStyle.Display);
        public static BinomCommand Text { get; } = new(TexStyle.Text);

        private readonly TexStyle? _style;

        private BinomCommand(TexStyle? style)
        {
            _style = style;
        }

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var position = context.ArgumentsStartPosition;
            var top = ReadArgument(context, ref position);
            var bottom = ReadArgument(context, ref position);
            var start = context.CommandNameStartPosition;
            var atomSource = context.CommandSource.Segment(start, position - start);

            // The parentheses stand where a bare fraction's null delimiter space would be.
            var fraction = new FractionAtom(atomSource, top.RootAtom, bottom.RootAtom, TexUnit.Point, 0)
            {
                SuppressNullDelimiterSpace = true,
            };
            if (_style is { } style)
                fraction = fraction with { OverrideStyle = style };

            var left = new SymbolAtom(atomSource, "(", TexAtomType.Opening, true);
            var right = new SymbolAtom(atomSource, ")", TexAtomType.Closing, true);
            return new CommandProcessingResult(new FencedAtom(atomSource, fraction, left, right), position);
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
    private sealed class BraketCommand : ICommandParser
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

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var position = context.ArgumentsStartPosition;
            var body = ReadArgument(context, ref position);

            var start = context.CommandNameStartPosition;
            var atomSource = context.CommandSource.Segment(start, position - start);

            var left = new SymbolAtom(atomSource, _open, TexAtomType.Opening, true);
            var right = new SymbolAtom(atomSource, _close, TexAtomType.Closing, true);
            return new CommandProcessingResult(
                new FencedAtom(atomSource, body.RootAtom, left, right), position);
        }
    }

    private sealed class CancelCommand : ICommandParser
    {
        public static CancelCommand BCancel { get; } = new(StrokeBoxMode.Back);
        public static CancelCommand Cancel { get; } = new(StrokeBoxMode.Normal);
        public static CancelCommand XCancel { get; } = new(StrokeBoxMode.Both);

        private CancelCommand(StrokeBoxMode strokeBoxMode)
        {
            _strokeBoxMode = strokeBoxMode;
        }

        private readonly StrokeBoxMode _strokeBoxMode;

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var position = context.ArgumentsStartPosition;
            var afterFormula = TexFormulaParser.ReadElement(source, position);
            position = afterFormula.position;
            var contentFormula = context.Parser.Parse(afterFormula.source,
                                                      context.Formula.TextStyle,
                                                      context.Environment.CreateChildEnvironment());

            var start = context.CommandNameStartPosition;
            var atomSource = source.Segment(start, position - start);
            var cancelAtom = new CancelAtom(atomSource, contentFormula.RootAtom, _strokeBoxMode);

            return new CommandProcessingResult(cancelAtom, position);
        }
    }

    /// <summary>
    /// This command will parse the remaining part of an input string, and add it onto a new line of a formula. The
    /// new line is created as a <see cref="MatrixAtom"/>; the command will try to reuse existing atoms if possible.
    /// </summary>
    private class NewLineCommand : ICommandParser
    {
        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var prevFormulaAtom = context.Formula.RootAtom;

            var nextLineAtom = context.Parser.Parse(
                source.Segment(context.ArgumentsStartPosition),
                context.Formula.TextStyle,
                context.Environment).RootAtom;

            // An optimization: if the new content itself is a matrix with suitable parameters, then we won't
            // wrap it into another formula, but will combine it with the content on top.
            var newMatrix = nextLineAtom is MatrixAtom m
                && m.MatrixCellAlignment == MatrixCellAlignment.Left
                && m.HorizontalPadding == MatrixAtom.DefaultPadding
                && m.VerticalPadding == MatrixAtom.DefaultPadding
                ? m
                : null;

            var topRow = new[] {prevFormulaAtom};
            var rows = new List<IEnumerable<Atom?>> {topRow};
            if (newMatrix != null)
            {
                rows.AddRange(newMatrix.MatrixCells);
            }
            else
            {
                var bottomRow = new[] {nextLineAtom};
                rows.Add(bottomRow);
            }

            // We'll always use source = null for the resulting matrix, because it's a structural element and not a
            // useful atom generated from any particular sources.
            var atom = new MatrixAtom(null, rows, MatrixCellAlignment.Left);
            var position = source.Length; // we always parse the provided source until the end
            return new CommandProcessingResult(atom, position, AtomAppendMode.Replace);
        }
    }

    // \mathop{…} and its family: the argument keeps its shape and changes its kind, which is what
    // decides the space around it. A paper reaches for \mathop where a name should behave as an
    // operator and for \mathrel where a symbol should behave as a relation.
    private sealed class AtomTypeCommand : ICommandParser
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

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var position = context.ArgumentsStartPosition;
            var argument = ReadArgument(context, ref position);
            var start = context.CommandNameStartPosition;
            var atomSource = context.CommandSource.Segment(start, position - start);
            var atom = argument.RootAtom ?? (Atom)new NullAtom(atomSource);
            return new CommandProcessingResult(new TypedAtom(atomSource, atom, _type, _type), position);
        }
    }

    // \_ : there is no underscore in the text encoding, so LaTeX draws one - a rule 0.3em wide,
    // sitting a little below the baseline. Its neighbours \# \$ \% \& are ordinary glyphs and are
    // handled as symbols instead.
    private sealed class UnderscoreCommand : ICommandParser
    {
        public static UnderscoreCommand Instance { get; } = new();

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var start = context.CommandNameStartPosition;
            var position = context.ArgumentsStartPosition;
            var atomSource = context.CommandSource.Segment(start, position - start);
            var atom = new RuleAtom(atomSource, TexUnit.Em, Width: 0.3, Thickness: 0.04, Shift: -0.06);
            return new CommandProcessingResult(atom, position);
        }
    }

    // The plain-TeX font switches: \cal, \bf, \it, \rm, \sf, \tt, \frak. Unlike \mathcal{…} they
    // take no argument - a switch runs from where it stands to the end of its group, which is why they
    // are written {\cal N} rather than \cal{N}. Nothing in amsmath documents them and they are
    // deprecated in LaTeX2e, but published papers are full of them, so a formula lifted out of one
    // needs them to mean what it meant there.
    private sealed class FontSwitchCommand : ICommandParser
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

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var start = context.CommandNameStartPosition;

            // The rest of the group is the argument. As with the style switches, the environment is
            // kept rather than a child one, so a switch inside a matrix cell does not swallow the row
            // and cell separators.
            var rest = source.Segment(context.ArgumentsStartPosition);
            var formula = context.Parser.Parse(rest, _textStyle, context.Environment);
            var atomSource = source.Segment(start, source.Length - start);
            var atom = formula.RootAtom ?? (Atom)new NullAtom(atomSource);
            return new CommandProcessingResult(atom, source.Length);
        }
    }

    // \big, \Big, \bigg and \Bigg, with their l/r/m variants: a delimiter at a set size, rather than
    // one grown to fit what it stands beside. TeX builds them by fencing an empty box 8.5, 11.5, 14.5
    // or 17.5pt tall, and \left's sizing rule turns those into delimiters of 1.15, 1.75, 2.35 and
    // 2.95 em - an arithmetic progression, since both the struts and the rule are linear in the size.
    // Those lengths are absolute in TeX, so unlike almost everything else here they do not shrink
    // with the style: \big( is the same delimiter inside a subscript as outside one.
    private sealed class BigDelimiterCommand : ICommandParser
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

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var start = context.CommandNameStartPosition;
            var position = context.ArgumentsStartPosition;
            var delimiter = TexFormulaParser.ParseDelimiter(source, start, ref position);

            var atomSource = source.Segment(start, position - start);
            var atom = new BigDelimiterAtom(
                atomSource,
                delimiter.Name,
                SmallestHeight + HeightStep * _size,
                _type);
            return new CommandProcessingResult(atom, position);
        }
    }

    // \genfrac{ldelim}{rdelim}{thickness}{style}{numerator}{denominator}: the general fraction that
    // every other one in amsmath is spelled with. An empty delimiter argument means no delimiter on
    // that side, an empty thickness means the default rule, and an empty style means whatever the
    // surrounding one would have given.
    private sealed class GenFracCommand : ICommandParser
    {
        public static GenFracCommand Instance { get; } = new();

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var position = context.ArgumentsStartPosition;

            var left = ReadDelimiter(source, ref position);
            var right = ReadDelimiter(source, ref position);
            var thickness = ReadLiteral(source, ref position);
            var style = ReadLiteral(source, ref position);
            var numerator = ReadArgument(context, ref position);
            var denominator = ReadArgument(context, ref position);

            var start = context.CommandNameStartPosition;
            var atomSource = source.Segment(start, position - start);

            FractionAtom fraction;
            if (thickness.Length == 0)
            {
                fraction = new FractionAtom(atomSource, numerator.RootAtom, denominator.RootAtom, true);
            }
            else
            {
                // A thickness of 0pt is how \binom is written: a fraction with no rule drawn.
                ParseLength(thickness, @"\genfrac", out var unit, out var value);
                fraction = new FractionAtom(atomSource, numerator.RootAtom, denominator.RootAtom, unit, value);
            }

            if (style.Length > 0)
                fraction = fraction with { OverrideStyle = ParseStyle(style) };

            // Delimiters stand where the null delimiter space otherwise goes.
            if (left != null || right != null)
                fraction = fraction with { SuppressNullDelimiterSpace = true };

            Atom atom = left == null && right == null
                ? fraction
                : new FencedAtom(atomSource, fraction, left, right);
            return new CommandProcessingResult(atom, position);
        }

        private static SymbolAtom? ReadDelimiter(SourceSpan source, ref int position)
        {
            var after = TexFormulaParser.ReadElement(source, position);
            position = after.position;
            return after.source.ToString().Trim().Length == 0
                ? null
                : TexFormulaParser.GetDelimiterAtom(after.source, after.source);
        }

        /// <summary>Reads an argument that is a length or a digit, not maths.</summary>
        private static string ReadLiteral(SourceSpan source, ref int position)
        {
            var after = TexFormulaParser.ReadElement(source, position);
            position = after.position;
            return after.source.ToString().Trim();
        }

        private static TexStyle ParseStyle(string text) => text switch
        {
            "0" => TexStyle.Display,
            "1" => TexStyle.Text,
            "2" => TexStyle.Script,
            "3" => TexStyle.ScriptScript,
            _ => throw new TexParseException($"\\genfrac takes a style of 0 to 3, not \"{text}\"."),
        };
    }

    // \hdotsfor[spacing]{n}: a run of dots across n columns of a matrix, standing in for a row of
    // entries left unwritten.
    private sealed class HDotsForCommand : ICommandParser
    {
        public static HDotsForCommand Instance { get; } = new();

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var position = context.ArgumentsStartPosition;

            var spacing = 1.0;
            var spacingText = TexFormulaParser.ReadElementGroupOptional(source, ref position, '[', ']')?.ToString();
            if (spacingText != null &&
                !double.TryParse(spacingText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out spacing))
                throw new TexParseException($"Invalid \\hdotsfor spacing: \"{spacingText}\".");

            var afterCount = TexFormulaParser.ReadElement(source, position);
            position = afterCount.position;
            var countText = afterCount.source.ToString().Trim();
            if (!int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var columns) ||
                columns < 1)
                throw new TexParseException($"\\hdotsfor needs a column count, not \"{countText}\".");

            var start = context.CommandNameStartPosition;
            var atomSource = source.Segment(start, position - start);
            return new CommandProcessingResult(new HDotsForAtom(atomSource, columns, spacing), position);
        }
    }

    // Reads past a command's arguments without doing anything with them: a starred form, then an
    // optional [...] group where the command has one, then its mandatory {...} groups.
    private static int SkipArguments(CommandContext context, int mandatory, bool optional)
    {
        var source = context.CommandSource;
        var position = context.ArgumentsStartPosition;
        if (position < source.Length && source[position] == '*')
            position++;
        if (optional)
            TexFormulaParser.ReadElementGroupOptional(source, ref position, '[', ']');
        for (var i = 0; i < mandatory; i++)
            position = TexFormulaParser.ReadElement(source, position).position;
        return position;
    }

    /// <summary>
    /// A document-level command - numbering, cross references, page breaks - read and dropped. A
    /// formula here stands alone: there is no page to break, nothing to number and nothing to refer
    /// to, so the command has no work left to do. Rejecting it would only make a formula lifted out
    /// of a paper unrenderable over a detail that could never have shown up anyway.
    /// </summary>
    private sealed class DiscardedCommand : ICommandParser
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

        public CommandProcessingResult ProcessCommand(CommandContext context) =>
            new(null, SkipArguments(context, _mandatory, _optional));
    }

    /// <summary>
    /// A command whose LaTeX-level effect is page layout but whose argument is real maths -
    /// <c>\shoveleft</c> and <c>\shoveright</c>. The layout goes; the contents stay.
    /// </summary>
    private sealed class TransparentCommand : ICommandParser
    {
        public static TransparentCommand Instance { get; } = new();

        public CommandProcessingResult ProcessCommand(CommandContext context)
        {
            var source = context.CommandSource;
            var position = context.ArgumentsStartPosition;
            var argument = ReadArgument(context, ref position);
            var start = context.CommandNameStartPosition;
            var atomSource = source.Segment(start, position - start);
            return new CommandProcessingResult(argument.RootAtom ?? new NullAtom(atomSource), position);
        }
    }

    /// <summary>
    /// A display environment that carries nothing beyond its contents here: a formula in a markdown
    /// document is already its own display, with no page, no equation numbers, and no margins to be
    /// flush with. The wrapper is dropped and the body parsed in its place.
    /// </summary>
    private sealed class TransparentEnvironment : IEnvironmentParser
    {
        public static TransparentEnvironment Instance { get; } = new();

        public EnvironmentProcessingResult ProcessEnvironment(EnvironmentContext context)
        {
            var formula = context.Parser.Parse(
                context.EnvironmentBodySource,
                context.Formula.TextStyle,
                context.Environment.CreateChildEnvironment());
            return new EnvironmentProcessingResult(
                formula.RootAtom ?? new NullAtom(context.EnvironmentSource));
        }
    }

    /// <summary>
    /// An <c>alignat</c>-family environment: the alignment of <c>align</c>, preceded by a count of the
    /// column pairs. That count exists to set inter-column spacing across a page of text, and has
    /// nothing to govern here, so it is read and dropped.
    /// </summary>
    private sealed class CountedAlignEnvironment : IEnvironmentParser
    {
        public static CountedAlignEnvironment Instance { get; } = new();

        public EnvironmentProcessingResult ProcessEnvironment(EnvironmentContext context)
        {
            var body = context.EnvironmentBodySource;
            var position = 0;
            while (position < body.Length && char.IsWhiteSpace(body[position]))
                position++;

            // The count is mandatory in LaTeX, so a leading group is always it - but a formula written
            // by hand often leaves it out, and there is nothing here that needs it.
            if (position < body.Length && body[position] == '{')
                body = body.Segment(TexFormulaParser.ReadElement(body, position).position);

            return MatrixCommandParser.Align.ProcessEnvironment(context with { EnvironmentBodySource = body });
        }
    }

    internal static readonly IReadOnlyDictionary<string, ICommandParser> Dictionary =
        new Dictionary<string, ICommandParser>
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
            ["genfrac"] = GenFracCommand.Instance,
            ["big"] = new BigDelimiterCommand(0, TexAtomType.Ordinary),
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
            ["begin"] = new ProcessEnvironmentCommand()
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

    internal static readonly IReadOnlyDictionary<string, IEnvironmentParser> Environments =
        new Dictionary<string, IEnvironmentParser>
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
}
