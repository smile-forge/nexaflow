using XamlMath.Boxes;

using System.Collections.Generic;

namespace XamlMath.Atoms;

// Atom that renders its content but reports no height and no depth, so that nothing around it is pushed out of
// the way (\smash). The opposite of PhantomAtom, which keeps the extent and drops the ink.
internal sealed record SmashAtom : Atom
{
    public override IReadOnlyList<FormulaSlot> Slots => Parts(("base", BaseAtom));

    public SmashAtom(SourceSpan? source, Atom? baseAtom)
        : base(source)
    {
        this.BaseAtom = baseAtom;
    }

    public Atom? BaseAtom { get; }

    protected override Box CreateBoxCore(TexEnvironment environment)
    {
        var baseBox = this.BaseAtom == null ? StrutBox.Empty : this.BaseAtom.CreateBox(environment);
        var box = new HorizontalBox(baseBox);
        box.Height = 0;
        box.Depth = 0;
        return box;
    }

    public override TexAtomType GetLeftType() => this.BaseAtom?.GetLeftType() ?? this.Type;

    public override TexAtomType GetRightType() => this.BaseAtom?.GetRightType() ?? this.Type;
}
