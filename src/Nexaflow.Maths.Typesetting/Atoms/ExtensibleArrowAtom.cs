using System;
using XamlMath.Boxes;

namespace XamlMath.Atoms;

// Atom representing an arrow that stretches to fit the labels set above and below it (\xrightarrow and its
// family). Unlike OverArrowAtom, which draws an arrow over a base, this one is a relation in its own right.
internal sealed record ExtensibleArrowAtom : Atom
{
    /// <summary>The arrow is never shorter than this, as a fraction of a quad.</summary>
    private const double MinimumWidthInQuads = 1.0;

    /// <summary>The space between the arrow's ends and the label above it, as a fraction of a quad.</summary>
    private const double LabelPaddingInQuads = 0.25;

    public ExtensibleArrowAtom(
        SourceSpan? source,
        Atom? overAtom,
        Atom? underAtom,
        ArrowDecoration decoration)
        : base(source, TexAtomType.Relation)
    {
        this.OverAtom = overAtom;
        this.UnderAtom = underAtom;
        this.Decoration = decoration;
    }

    public Atom? OverAtom { get; }

    public Atom? UnderAtom { get; }

    public ArrowDecoration Decoration { get; }

    protected override Box CreateBoxCore(TexEnvironment environment)
    {
        var overBox = this.OverAtom?.CreateBox(environment.GetSuperscriptStyle());
        var underBox = this.UnderAtom?.CreateBox(environment.GetSubscriptStyle());

        var quad = environment.MathFont.GetQuad(environment.LastFontId, environment.Style);
        var padding = LabelPaddingInQuads * quad;
        var labelWidth = Math.Max(overBox?.TotalWidth ?? 0, underBox?.TotalWidth ?? 0);
        var width = Math.Max(MinimumWidthInQuads * quad, labelWidth + 2 * padding);

        var thickness = environment.MathFont.GetDefaultLineThickness(environment.Style);
        var arrowBox = new ArrowBox(environment, width, thickness, this.Decoration);
        var gap = thickness;

        var resultBox = new VerticalBox();
        var aboveArrow = 0.0;
        if (overBox != null)
        {
            resultBox.Add(new HorizontalBox(overBox, width, TexAlignment.Center));
            resultBox.Add(new StrutBox(0, gap, 0, 0));
            aboveArrow = overBox.TotalHeight + gap;
        }

        resultBox.Add(arrowBox);

        var belowArrow = 0.0;
        if (underBox != null)
        {
            resultBox.Add(new StrutBox(0, gap, 0, 0));
            resultBox.Add(new HorizontalBox(underBox, width, TexAlignment.Center));
            belowArrow = underBox.TotalHeight + gap;
        }

        // Sit the shaft — which runs down the middle of the arrow box — on the math axis, so the arrow lines up
        // with the relation symbols it stands in for.
        var axis = environment.MathFont.GetAxisHeight(environment.Style);
        var total = aboveArrow + arrowBox.TotalHeight + belowArrow;
        resultBox.Height = aboveArrow + arrowBox.Height / 2 + axis;
        resultBox.Depth = total - resultBox.Height;
        resultBox.Width = width;
        return resultBox;
    }
}
