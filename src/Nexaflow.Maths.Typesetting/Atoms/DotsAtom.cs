using XamlMath.Boxes;

namespace XamlMath.Atoms;

// Atom representing a run of three dots, either stacked vertically (\vdots) or along the descending
// diagonal (\ddots). The run is centred on the math axis so it lines up neatly inside matrices.
internal sealed record DotsAtom : Atom
{
    public enum DotsShape
    {
        Vertical,
        Diagonal
    }

    private const string DotSymbolName = "ldotp";
    private const int DotCount = 3;

    private readonly DotsShape _shape;

    public DotsAtom(SourceSpan? source, DotsShape shape)
        : base(source)
    {
        _shape = shape;
    }

    protected override Box CreateBoxCore(TexEnvironment environment)
    {
        var font = environment.MathFont;
        var style = environment.Style;

        Box CreateDot() => SymbolAtom.GetAtom(DotSymbolName, null).CreateBox(environment);

        var firstDot = CreateDot();
        var quad = font.GetQuad(firstDot.GetLastFontId(), style);

        // Vertical centre-to-centre spacing of the dots, and matching horizontal step for a ~45° diagonal.
        var verticalGap = 0.18 * quad;
        var horizontalStep = _shape == DotsShape.Diagonal
            ? firstDot.Height + firstDot.Depth + verticalGap
            : 0.0;

        var column = new VerticalBox();
        for (var i = 0; i < DotCount; i++)
        {
            var dot = i == 0 ? firstDot : CreateDot();
            dot.Shift = i * horizontalStep; // horizontal offset inside a VerticalBox
            if (i > 0)
                column.Add(new StrutBox(0, verticalGap, 0, 0));
            column.Add(dot);
        }

        // Centre the whole run on the math axis (same technique as VerticalCenteredAtom).
        var axis = font.GetAxisHeight(style);
        column.Shift = -((column.Height + column.Depth) / 2) - axis;
        return new HorizontalBox(column);
    }
}
