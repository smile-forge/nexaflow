using XamlMath.Boxes;

using System.Collections.Generic;

namespace XamlMath.Atoms;

// Atom representing a formula drawn inside a rectangular frame (\boxed).
internal sealed record BoxedAtom : Atom
{
    public override IReadOnlyList<FormulaSlot> Slots => Parts(("base", BaseAtom));

    // LaTeX frames with \fboxrule around the content and \fboxsep of padding between the two; the ratio below is
    // the one the standard classes use (0.4pt to 3pt), expressed against the current rule thickness.
    private const double PaddingPerThickness = 7.5;

    public BoxedAtom(SourceSpan? source, Atom? baseAtom)
        : base(source)
    {
        this.BaseAtom = baseAtom;
    }

    public Atom? BaseAtom { get; }

    protected override Box CreateBoxCore(TexEnvironment environment)
    {
        var contentBox = this.BaseAtom == null ? StrutBox.Empty : this.BaseAtom.CreateBox(environment);
        var thickness = environment.MathFont.GetDefaultLineThickness(environment.Style);
        var inset = thickness + PaddingPerThickness * thickness;

        var content = new HorizontalBox();
        content.Add(new StrutBox(inset, 0, 0, 0));
        content.Add(contentBox);
        content.Add(new StrutBox(inset, 0, 0, 0));
        content.Height = contentBox.Height + inset;
        content.Depth = contentBox.Depth + inset;

        var frame = new FrameBox(environment, thickness)
        {
            Width = content.Width,
            Height = content.Height,
            Depth = content.Depth,
        };

        var box = new LayeredBox();
        box.Add(content);
        box.Add(frame);
        return box;
    }
}
