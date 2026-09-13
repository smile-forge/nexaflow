using XamlMath.Boxes;

namespace XamlMath.Atoms;

internal sealed record NullAtom : Atom
{
    public NullAtom(TexAtomType type = TexAtomType.Ordinary) : base(type)
    {
    }

    protected override Box CreateBoxCore(TexEnvironment environment) => new StrutBox(0, 0, 0, 0);
}
