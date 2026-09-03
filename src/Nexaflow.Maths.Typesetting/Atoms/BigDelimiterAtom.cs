using XamlMath.Boxes;

namespace XamlMath.Atoms;

/// <summary>
/// A delimiter at one of the four set sizes of <c>\big</c> … <c>\Bigg</c>, rather than one grown to
/// fit what it encloses. Nothing is enclosed: the delimiter stands on its own and takes the atom
/// type its spelling asked for, so <c>\bigl(</c> spaces as an opening and <c>\bigm|</c> as a relation.
/// </summary>
internal sealed record BigDelimiterAtom : Atom
{
    private readonly string _symbolName;
    private readonly double _minHeight;

    public BigDelimiterAtom(string symbolName, double minHeight, TexAtomType type)
        : base(type)
    {
        _symbolName = symbolName;
        _minHeight = minHeight;
    }

    protected override Box CreateBoxCore(TexEnvironment environment)
    {
        var box = DelimiterFactory.CreateBox(_symbolName, _minHeight, environment);

        // Centred on the maths axis, the way a fenced delimiter is.
        var axis = environment.MathFont.GetAxisHeight(environment.Style);
        box.Shift = -((box.Height + box.Depth) / 2 - box.Height) - axis;
        return new HorizontalBox(box);
    }
}
