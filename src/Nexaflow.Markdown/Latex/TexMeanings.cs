using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.Latex;

/// <summary>
/// A symbol TeX's tables name: what they call it, its class, and whether it grows to the height of what it stands beside.
/// </summary>
internal sealed record TexSymbol(string Name, TexAtomType Class, bool IsDelimiter);

/// <summary>
/// What a command means, as <see cref="Stages.ResolveCommands"/> reads its name (<see cref="TexCommandNode"/>): which construct
/// it is and whatever its name alone says about how that construct is set. What it is set from is its parts, which stay where
/// the parser put them, so what a construct is and what it holds are both still there to be asked.
/// </summary>
internal abstract record TexMeaning;

/// <summary>A command LaTeX has and nothing here draws — <c>\colorbox</c>, a length that will not read — set as it is written.</summary>
internal sealed record TexUnset : TexMeaning;

/// <summary><c>\frac</c>: its numerator over its denominator.</summary>
internal sealed record TexFraction : TexMeaning;

/// <summary><c>\dfrac</c> and <c>\tfrac</c>: a fraction set in the style it names, whatever surrounds it.</summary>
internal sealed record TexStyledFraction(TexStyle Style) : TexMeaning;

/// <summary><c>\cfrac</c>: a continued fraction, display style all the way down, its numerator leaning as its option says.</summary>
internal sealed record TexContinuedFraction(TexAlignment Leaning) : TexMeaning;

/// <summary><c>\nicefrac</c> and <c>\sfrac</c>: a raised numerator, a slash and a lowered denominator.</summary>
internal sealed record TexSlashFraction : TexMeaning;

/// <summary><c>\genfrac</c>: a fraction between the delimiters its first two arguments name, or none.</summary>
internal sealed record TexGeneralFraction : TexMeaning;

/// <summary><c>\binom</c>, <c>\dbinom</c>, <c>\tbinom</c>: a fraction with no bar, in brackets, in the style named or the style around it.</summary>
internal sealed record TexBinomial(TexStyle? Style) : TexMeaning;

/// <summary><c>\sqrt</c>: its radicand under the sign, and its degree tucked over it where one is written.</summary>
internal sealed record TexRoot : TexMeaning;

/// <summary><c>\overline</c>: a rule over its base.</summary>
internal sealed record TexOverline : TexMeaning;

/// <summary><c>\underline</c>: a rule under its base.</summary>
internal sealed record TexUnderline : TexMeaning;

/// <summary><c>\not</c>: a slash through what is written after it, which is one sign with it.</summary>
internal sealed record TexNegation : TexMeaning;

/// <summary>
/// Room asked for: a length in a unit — <c>\quad</c>, <c>\hspace{2em}</c> — or, with no unit, a word space in the font it
/// stands in (<c>\ </c>, <c>\nbsp</c>, a tie).
/// </summary>
internal sealed record TexSpace(TexUnit? Unit, double Amount) : TexMeaning;

/// <summary><c>\hdotsfor</c>: a row of dots across the columns its <see cref="TexDots"/> says.</summary>
internal sealed record TexRowOfDots : TexMeaning;

/// <summary><c>\textcolor</c> — its argument in a colour — and <c>\color</c>, which is a switch: the rest of its group in it.</summary>
internal sealed record TexColour(HexColor Colour, bool Switch) : TexMeaning;

/// <summary>
/// One side of a fence — <c>\left(</c>, <c>\right.</c> — or a delimiter at a size of its own — <c>\big(</c>: the delimiter it
/// names, or none where a side is written open or names nothing that grows.
/// </summary>
internal sealed record TexDelimiter(TexSymbol? Symbol) : TexMeaning;

/// <summary><c>\big</c> … <c>\Biggm</c>: one delimiter at a set height in em, and the class it takes.</summary>
internal sealed record TexSizedDelimiter(TexSymbol Delimiter, double MinHeight, TexAtomType Class) : TexMeaning;

/// <summary><c>\overset</c>, <c>\underset</c> and <c>\stackrel</c>: something small over or under its base — a relation, for <c>\stackrel</c>.</summary>
internal sealed record TexAnnotation(bool Over, bool AsRelation) : TexMeaning
{
    /// <summary>The gap between the base and what is set over or under it, in mu.</summary>
    public const double Space = 2.5;
}

/// <summary>
/// <c>\mathrm{…}</c> and its family — its argument in a face — and <c>\text{…}</c> and its family, whose argument is words: set
/// as the characters written, spaces and all.
/// </summary>
internal sealed record TexFace(string Face, bool Words) : TexMeaning;

/// <summary>
/// A symbol by name — <c>\alpha</c>, <c>\sum</c>, <c>\hat</c> — and, for a big operator, whether its limits go beside it in
/// every style, as an integral's do.
/// </summary>
internal sealed record TexNamedSymbol(TexSymbol Symbol, bool LimitsBeside) : TexMeaning;

/// <summary><c>\surd</c>: a radical sign with nothing under it, lifted to sit about the axis.</summary>
internal sealed record TexSurd : TexMeaning;

/// <summary><c>\doteq</c> and <c>\cong</c>: one symbol set over another at a fixed gap in mu, full size, as a relation.</summary>
internal sealed record TexPile(string Under, string Over, double Gap) : TexMeaning;

/// <summary>
/// A switch — <c>\bf</c>, <c>\displaystyle</c>, <c>\large</c> — which sets the rest of the group it stands in: in a face, at
/// a size, or, for a size a formula has no equivalent of, as it was.
/// </summary>
internal sealed record TexSwitch(string? Face, TexStyle? Size) : TexMeaning;

/// <summary><c>\tag</c> and <c>\tag*</c>: an equation's number, set against the block's edge — null where none is written.</summary>
internal sealed record TexTag(string? Number, bool Starred) : TexMeaning;

/// <summary><c>\hline</c>: a rule across the table above the row it is written in.</summary>
internal sealed record TexRule : TexMeaning;

/// <summary><c>\limits</c> and <c>\nolimits</c>: whether the operator before them wears its limits over and under it.</summary>
internal sealed record TexLimits(bool Vertical) : TexMeaning;

/// <summary>Numbering, cross references and page layout — <c>\label</c>, <c>\nonumber</c>: read, and nothing for a formula to draw.</summary>
internal sealed record TexDiscarded : TexMeaning;

/// <summary><c>\overrightarrow</c> and its family: an arrow as wide as what it is over or under.</summary>
internal sealed record TexOverArrow(ArrowDecoration Decoration, bool Over) : TexMeaning;

/// <summary><c>\xrightarrow</c> and its family: an arrow stretched to the labels written over and under it.</summary>
internal sealed record TexExtensibleArrow(ArrowDecoration Decoration) : TexMeaning;

/// <summary><c>\vdots</c> and <c>\ddots</c>: three dots down, or down the diagonal.</summary>
internal sealed record TexStackedDots(bool Diagonal) : TexMeaning;

/// <summary><c>\pmod</c>, <c>\pod</c>, <c>\mod</c>: the word, the argument, and brackets round them where there are any.</summary>
internal sealed record TexModulus(bool WithMod, bool Fenced) : TexMeaning;

/// <summary><c>\phantom</c> and its one-way variants: the room its argument takes, and nothing drawn.</summary>
internal sealed record TexPhantom(bool Width, bool Height) : TexMeaning;

/// <summary><c>\smash</c> — its argument drawn with no height — and <c>\mathllap</c> and its family, drawn with no width, hanging as it says.</summary>
internal sealed record TexSmash(TexAlignment? Lap) : TexMeaning;

/// <summary><c>\boxed</c> and <c>\fbox</c>: its argument in a frame.</summary>
internal sealed record TexBoxed : TexMeaning;

/// <summary><c>\overbrace</c> and <c>\underbrace</c>: a brace over or under its base, the label written after it beyond the brace.</summary>
internal sealed record TexBrace(bool Over, string Symbol) : TexMeaning;

/// <summary><c>\boldsymbol</c>, <c>\bm</c>, <c>\pmb</c>: every character of its argument from the bold companion of its font.</summary>
internal sealed record TexBoldSymbol : TexMeaning;

/// <summary><c>\operatorname</c>: a name set upright as an operator, its limits beside it — or where the style puts them, for the <c>*</c> form.</summary>
internal sealed record TexOperatorName(bool Starred) : TexMeaning;

/// <summary><c>\bra</c>, <c>\ket</c>, <c>\braket</c>: Dirac's brackets, as a fence between the two delimiters named.</summary>
internal sealed record TexBraket(string Open, string Close) : TexMeaning;

/// <summary><c>\cancel</c> and its family: its argument struck through.</summary>
internal sealed record TexCancel(StrokeMode Mode) : TexMeaning;

/// <summary><c>\mathop</c> and its family: its argument as it is, of another class.</summary>
internal sealed record TexRetyped(TexAtomType Class) : TexMeaning;

/// <summary><c>\_</c>: a rule the width of an underscore, which the text face has no glyph for.</summary>
internal sealed record TexUnderscore : TexMeaning;

/// <summary><c>\shoveleft</c> and <c>\shoveright</c>: page layout round real maths — the layout goes and the maths stays.</summary>
internal sealed record TexTransparent : TexMeaning;

/// <summary><c>\matrix{…}</c>, <c>\pmatrix{…}</c>, <c>\cases{…}</c> and <c>\substack{…}</c>: rows in braces, arranged as the environment of that name arranges them.</summary>
internal sealed record TexStack(TexMatrixArrangement Arrangement) : TexMeaning;

/// <summary>
/// How an environment arranges what is between its <c>\begin</c> and <c>\end</c> (<see cref="TexGridNode"/>): as a grid, as an
/// <c>array</c> its preamble describes, or as nothing but its contents.
/// </summary>
internal abstract record TexArrangement;

/// <summary><c>equation</c> and the other display environments: a formula already on a line of its own, so only its contents.</summary>
internal sealed record TexContents : TexArrangement;

/// <summary>
/// A grid as a matrix environment sets one: the delimiters round it, how its cells sit, the style it is set in, and the room
/// kept between its rows and its columns.
/// </summary>
internal sealed record TexMatrixArrangement(
    string? Left, string? Right, MatrixCellAlignment Alignment, TexStyle? Style = null,
    double VerticalPadding = 0, double HorizontalPadding = TexMatrixArrangement.ColumnGap) : TexArrangement
{
    // TeX's line spacing for a table, in em: a baseline skip stretched as \arraystretch does, and the strut each row stands on
    // so short rows keep the spacing of tall ones.
    private const double BaselineSkip = 1.2;
    private const double ArrayStretch = 1.15;

    /// <summary>The room an aligned block keeps round its cells, in em.</summary>
    public const double Padding = 0.35;

    /// <summary>The room a table keeps between its columns, in em.</summary>
    public const double ColumnGap = 1.0;

    /// <summary>How far above its baseline every row of a table reaches at the least, in em.</summary>
    public const double RowStrutHeight = 0.7 * BaselineSkip * ArrayStretch;

    /// <summary>How far below its baseline every row of a table reaches at the least, in em.</summary>
    public const double RowStrutDepth = 0.3 * BaselineSkip * ArrayStretch;

    /// <summary>
    /// Whether its rows stand a line apart: a table struts them, while an aligned block and a stacked limit set theirs solid
    /// and space them with padding of their own.
    /// </summary>
    public bool RowStrut => this.VerticalPadding == 0;
}

/// <summary><c>array</c>: a table whose preamble says how each column sits — null where it names none, and every column is centred.</summary>
internal sealed record TexArrayArrangement(TexColumns? Columns) : TexArrangement;

/// <summary>
/// The column preamble of an <c>array</c>: <c>{lcr}</c> gives each column its alignment, and each <c>|</c> asks for a rule at
/// the boundary it sits at.
/// </summary>
internal sealed class TexColumns
{
    private TexColumns(IReadOnlyList<TexAlignment> alignments, IReadOnlyCollection<int> verticalRules)
    {
        this.Alignments = alignments;
        this.VerticalRules = verticalRules;
    }

    /// <summary>One entry per column.</summary>
    public IReadOnlyList<TexAlignment> Alignments { get; }

    /// <summary>Boundaries carrying a rule, numbered from 0 (left of the first column) to the column count (right of the last).</summary>
    public IReadOnlyCollection<int> VerticalRules { get; }

    /// <summary>
    /// Every column centred, for an array whose preamble named none.
    /// <para>
    /// <c>\begin{array}{}</c> is not legal LaTeX and the corpus has it anyway, rendered. A preamble that says nothing is not a
    /// reason to draw nothing: the cells already say how many columns there are, and centring them is what every matrix does,
    /// so it is set as one.
    /// </para>
    /// </summary>
    public static TexColumns Centred(int columns) => new(Enumerable.Repeat(TexAlignment.Center, columns).ToList(), []);

    /// <summary>A preamble such as <c>c|cc</c> read, or null where it holds anything but <c>l</c>, <c>c</c>, <c>r</c>, <c>|</c> and spaces, or no column at all.</summary>
    public static TexColumns? Read(string preamble)
    {
        var alignments = new List<TexAlignment>();
        var rules = new HashSet<int>();

        foreach (var c in preamble)
        {
            switch (c)
            {
                case 'l': alignments.Add(TexAlignment.Left); break;
                case 'c': alignments.Add(TexAlignment.Center); break;
                case 'r': alignments.Add(TexAlignment.Right); break;
                case '|': rules.Add(alignments.Count); break;
                case ' ': break;
                default: return null;
            }
        }

        return alignments.Count == 0 ? null : new TexColumns(alignments, rules.ToList());
    }

    /// <summary>The alignment of a column, repeating the last one if the body outgrew the preamble.</summary>
    public TexAlignment AlignmentOf(int column) =>
        this.Alignments[column < this.Alignments.Count ? column : this.Alignments.Count - 1];
}
