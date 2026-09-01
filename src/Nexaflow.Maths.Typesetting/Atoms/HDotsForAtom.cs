using System;
using XamlMath.Boxes;

namespace XamlMath.Atoms;

/// <summary>
/// <c>\hdotsfor[spacing]{n}</c>: a run of dots filling n columns of a matrix, for a row of entries
/// left unwritten. The dots repeat at a fixed step and the leftover is split between the two ends,
/// which is what LaTeX's leaders do with them.
/// </summary>
internal sealed record HDotsForAtom : Atom, ISpanningMatrixCell
{
    private const string DotSymbolName = "ldotp";

    /// <summary>Gap between the dots at spacing 1: a thin space, as in LaTeX.</summary>
    private const double ThinSpaceQuads = 1.0 / 6;

    private readonly double _spacing;

    public HDotsForAtom(SourceSpan? source, int columnSpan, double spacing) : base(source)
    {
        ColumnSpan = columnSpan;
        _spacing = spacing;
    }

    public int ColumnSpan { get; }

    /// <summary>
    /// Outside a matrix there are no columns to measure and nothing to fill. A quad each is a guess,
    /// but only somewhere the command has no meaning to begin with.
    /// </summary>
    protected override Box CreateBoxCore(TexEnvironment environment) =>
        CreateSpanningBox(environment, ColumnSpan * Measure(environment).Quad);

    public Box CreateSpanningBox(TexEnvironment environment, double width)
    {
        var metrics = Measure(environment);
        var gap = _spacing * ThinSpaceQuads * metrics.Quad;
        var step = metrics.DotWidth + gap;

        var count = step > TexUtilities.FloatPrecision
            ? Math.Max(2, (int)Math.Floor((width + gap) / step))
            : 2;

        var dots = new HorizontalBox();
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                dots.Add(new StrutBox(gap, 0, 0, 0));
            dots.Add(CreateDot(environment));
        }

        return new HorizontalBox(dots, width, TexAlignment.Center);
    }

    private static Box CreateDot(TexEnvironment environment) =>
        SymbolAtom.GetAtom(DotSymbolName, null).CreateBox(environment);

    private readonly record struct DotMetrics(double DotWidth, double Quad);

    private static DotMetrics Measure(TexEnvironment environment)
    {
        var dot = CreateDot(environment);
        return new DotMetrics(dot.TotalWidth, environment.MathFont.GetQuad(dot.GetLastFontId(), environment.Style));
    }
}
