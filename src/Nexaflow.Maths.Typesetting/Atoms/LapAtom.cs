using XamlMath.Boxes;

namespace XamlMath.Atoms;

// Atom that renders its content but reports no width, letting it overlap its neighbours
// (\mathllap to the left, \mathrlap to the right, \mathclap to both sides).
internal sealed record LapAtom : Atom
{
    public LapAtom(SourceSpan? source, Atom? baseAtom, TexAlignment alignment)
        : base(source)
    {
        this.BaseAtom = baseAtom;
        this.Alignment = alignment;
    }

    public Atom? BaseAtom { get; }

    /// <summary>Where the content sits relative to the zero-width point it is anchored at.</summary>
    public TexAlignment Alignment { get; }

    protected override Box CreateBoxCore(TexEnvironment environment)
    {
        var baseBox = this.BaseAtom == null ? StrutBox.Empty : this.BaseAtom.CreateBox(environment);

        // A horizontal box lays its children out by advancing over their widths, so a negative-width strut in
        // front of the content is what moves the content back over the anchor.
        var offset = this.Alignment switch
        {
            TexAlignment.Left => -baseBox.Width,
            TexAlignment.Center => -baseBox.Width / 2,
            _ => 0.0,
        };

        var box = new HorizontalBox();
        if (offset != 0.0)
            box.Add(new StrutBox(offset, 0, 0, 0));
        box.Add(baseBox);
        box.Width = 0;
        return box;
    }

    public override TexAtomType GetLeftType() => this.BaseAtom?.GetLeftType() ?? this.Type;

    public override TexAtomType GetRightType() => this.BaseAtom?.GetRightType() ?? this.Type;
}
