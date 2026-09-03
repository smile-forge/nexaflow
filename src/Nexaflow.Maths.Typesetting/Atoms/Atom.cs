using System.Collections.Generic;
using XamlMath.Boxes;

namespace XamlMath.Atoms;

/// <summary>Atom (smallest unit) of TexFormula.</summary>
/// <param name="Type"></param>
internal abstract record Atom(TexAtomType Type = TexAtomType.Ordinary) : IFormulaNode
{
    /// <summary>
    /// What this is made of, under the names its own construct gives the parts. Nothing, unless the atom
    /// says otherwise — most are leaves.
    /// </summary>
    public virtual IReadOnlyList<FormulaSlot> Slots => System.Array.Empty<FormulaSlot>();

    /// <inheritdoc/>
    public Nexaflow.Maths.Latex.TexPart? Origin { get; set; }

    /// <summary>A part, or nothing when the construct did not have one — a root without a degree.</summary>
    protected static IReadOnlyList<FormulaSlot> Parts(params (string Role, Atom? Node)[] parts)
    {
        var slots = new List<FormulaSlot>(parts.Length);
        foreach (var (role, node) in parts)
            if (node is not null) slots.Add(new FormulaSlot(role, node));
        return slots;
    }

    public Box CreateBox(TexEnvironment environment)
    {
        var box = this.CreateBoxCore(environment);

        // The way back from a drawn thing to what it means. Set here, in the one place every box is
        // made, so no atom can forget it.
        //
        // The innermost atom wins, except against one that does not know where it came from. Several
        // atoms are pure wrappers — a TypedAtom, a DummyAtom — and hand back the box of the atom inside
        // them rather than making one, so the box arrives here already claimed by something further in.
        // Where that something has no part, and this does, this is the better answer: a macro's
        // expansion is exactly that shape, its insides belonging to a definition and only its outermost
        // atom knowing which token the reader wrote.
        if (box.Node is null || (box.Node.Origin is null && this.Origin is not null)) box.Node = this;

        return box;
    }

    protected abstract Box CreateBoxCore(TexEnvironment environment);

    // Gets type of leftmost child item.
    public virtual TexAtomType GetLeftType()
    {
        return this.Type;
    }

    // Gets type of leftmost child item.
    public virtual TexAtomType GetRightType()
    {
        return this.Type;
    }
}
