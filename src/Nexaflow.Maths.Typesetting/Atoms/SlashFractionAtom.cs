using XamlMath.Boxes;

namespace XamlMath.Atoms;

// Atom representing an inline "slash" (split-level) fraction: a raised, script-size numerator, a slash,
// and a lowered, script-size denominator — the look of \nicefrac and \sfrac (e.g. ³/₄).
internal sealed record SlashFractionAtom : Atom
{
    public SlashFractionAtom(SourceSpan? source, Atom? numerator, Atom? denominator)
        : base(source, TexAtomType.Ordinary)
    {
        this.Numerator = numerator;
        this.Denominator = denominator;
    }

    public Atom? Numerator { get; }

    public Atom? Denominator { get; }

    protected override Box CreateBoxCore(TexEnvironment environment)
    {
        // Numerator and denominator are set in script size, like \nicefrac's \scriptstyle.
        var scriptEnvironment = environment.GetSubscriptStyle();
        var numeratorBox = this.Numerator == null ? StrutBox.Empty : this.Numerator.CreateBox(scriptEnvironment);
        var denominatorBox = this.Denominator == null ? StrutBox.Empty : this.Denominator.CreateBox(scriptEnvironment);
        var slashBox = SymbolAtom.GetAtom("slash", null).CreateBox(environment);

        var xHeight = environment.MathFont.GetXHeight(environment.Style, environment.LastFontId);

        // In a HorizontalBox a child's Shift is vertical (positive = down): raise the numerator into the
        // super-script zone and drop the denominator slightly below the baseline.
        numeratorBox.Shift = -(0.6 * xHeight + numeratorBox.Depth);
        denominatorBox.Shift = 0.2 * xHeight;

        // A small negative kern on each side tucks the parts against the slash, as \nicefrac does.
        var kern = -0.12 * slashBox.Width;

        var resultBox = new HorizontalBox();
        resultBox.Add(numeratorBox);
        resultBox.Add(new StrutBox(kern, 0, 0, 0));
        resultBox.Add(slashBox);
        resultBox.Add(new StrutBox(kern, 0, 0, 0));
        resultBox.Add(denominatorBox);
        return resultBox;
    }
}
