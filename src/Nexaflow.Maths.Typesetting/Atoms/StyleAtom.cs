using XamlMath.Boxes;

namespace XamlMath.Atoms;

// Atom that renders its content in an explicitly chosen TexStyle, ignoring the style it is nested in
// (\displaystyle, \textstyle, \scriptstyle and \scriptscriptstyle).
internal sealed record StyleAtom : Atom
{
    public StyleAtom(SourceSpan? source, Atom? baseAtom, TexStyle targetStyle)
        : base(source)
    {
        this.BaseAtom = baseAtom;
        this.TargetStyle = targetStyle;
    }

    public Atom? BaseAtom { get; }

    public TexStyle TargetStyle { get; }

    protected override Box CreateBoxCore(TexEnvironment environment) =>
        this.BaseAtom == null
            ? StrutBox.Empty
            : this.BaseAtom.CreateBox(environment with { Style = this.TargetStyle });

    public override TexAtomType GetLeftType() => this.BaseAtom?.GetLeftType() ?? this.Type;

    public override TexAtomType GetRightType() => this.BaseAtom?.GetRightType() ?? this.Type;
}
