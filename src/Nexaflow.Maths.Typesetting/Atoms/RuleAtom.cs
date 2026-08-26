using XamlMath.Boxes;

namespace XamlMath.Atoms;

/// <summary>
/// A drawn rectangle, sized in the units the caller asks for. It exists because <c>\_</c> is one:
/// the text encoding has no underscore glyph, so LaTeX draws a short rule below the baseline and
/// calls it one.
/// </summary>
/// <param name="Width">How wide, as a <see cref="SpaceAtom"/> would measure it.</param>
/// <param name="Thickness">How thick, in the same unit.</param>
/// <param name="Shift">How far below the baseline to sit, in the same unit.</param>
internal sealed record RuleAtom(
    SourceSpan? Source,
    TexUnit Unit,
    double Width,
    double Thickness,
    double Shift) : Atom(Source)
{
    protected override Box CreateBoxCore(TexEnvironment environment)
    {
        double Measure(double value) =>
            new SpaceAtom(null, this.Unit, value, 0, 0).CreateBox(environment).Width;

        return new HorizontalRule(environment, Measure(this.Thickness), Measure(this.Width), Measure(this.Shift));
    }
}
