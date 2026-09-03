using XamlMath.Boxes;

namespace XamlMath.Atoms;

// Atom that renders its content in the bold companion of whatever font each character would
// otherwise come from (\boldsymbol). Unlike a text style, this reaches symbols and Greek letters,
// because it acts when the character is resolved rather than when it is chosen.
internal sealed record BoldAtom : Atom
{
    public BoldAtom(Atom? baseAtom)
    {
        this.BaseAtom = baseAtom;
    }

    public Atom? BaseAtom { get; }

    protected override Box CreateBoxCore(TexEnvironment environment) =>
        this.BaseAtom == null
            ? StrutBox.Empty
            : this.BaseAtom.CreateBox(environment with { IsBold = true });

    public override TexAtomType GetLeftType() => this.BaseAtom?.GetLeftType() ?? this.Type;

    public override TexAtomType GetRightType() => this.BaseAtom?.GetRightType() ?? this.Type;
}
