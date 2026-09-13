using XamlMath.Boxes;

using System.Collections.Generic;

namespace XamlMath.Atoms;

// Atom representing a base atom with a stretchy horizontal arrow drawn above or below it:
// \overrightarrow and \overleftarrow and their leftright and \under… counterparts.
internal sealed record OverArrowAtom : Atom
{
    public override IReadOnlyList<FormulaSlot> Slots => Parts(("base", BaseAtom));

    private readonly ArrowDecoration _decoration;
    private readonly bool _over;

    public OverArrowAtom(Atom? baseAtom, ArrowDecoration decoration, bool over)
    {
        this.BaseAtom = baseAtom;
        _decoration = decoration;
        _over = over;
    }

    public Atom? BaseAtom { get; }

    protected override Box CreateBoxCore(TexEnvironment environment)
    {
        // Create box for base atom, in cramped style (like \overline).
        var baseBox = this.BaseAtom == null ? StrutBox.Empty : this.BaseAtom.CreateBox(environment.GetCrampedStyle());

        var thickness = environment.MathFont.GetDefaultLineThickness(environment.Style);
        var arrowBox = new ArrowBox(environment, baseBox.Width, thickness, _decoration);

        var resultBox = new VerticalBox();
        if (_over)
        {
            resultBox.Add(new StrutBox(0, thickness, 0, 0));
            resultBox.Add(arrowBox);
            resultBox.Add(new StrutBox(0, 3 * thickness, 0, 0));
            resultBox.Add(baseBox);

            // Adjust height and depth of result box so the base keeps its normal baseline.
            resultBox.Height = baseBox.Height + arrowBox.Height + 4 * thickness;
            resultBox.Depth = baseBox.Depth;
        }
        else
        {
            resultBox.Add(baseBox);
            resultBox.Add(new StrutBox(0, 3 * thickness, 0, 0));
            resultBox.Add(arrowBox);
            resultBox.Add(new StrutBox(0, thickness, 0, 0));

            resultBox.Height = baseBox.Height;
            resultBox.Depth = baseBox.Depth + arrowBox.Height + 4 * thickness;
        }

        return resultBox;
    }
}
