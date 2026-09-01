using System.Collections.Generic;
using XamlMath.Boxes;

namespace XamlMath.Atoms;

/// <summary>Atom (smallest unit) of TexFormula.</summary>
/// <param name="Source"></param>
/// <param name="Type"></param>
internal abstract record Atom(SourceSpan? Source, TexAtomType Type = TexAtomType.Ordinary) : IFormulaNode
{
    /// <summary>
    /// What this is made of, under the names its own construct gives the parts. Nothing, unless the atom
    /// says otherwise — most are leaves.
    /// </summary>
    public virtual IReadOnlyList<FormulaSlot> Slots => System.Array.Empty<FormulaSlot>();

    /// <inheritdoc/>
    public Nexaflow.Maths.Latex.TexPart? Origin { get; set; }

    /// <summary>
    /// Whether this atom came out of a macro's definition rather than out of anything a reader wrote.
    ///
    /// <para>
    /// A predefined formula is parsed from its own definition text, so its atoms carry offsets into that
    /// text — <c>\cdots</c> is three dots at 0, 6 and 12 of <c>\cdotp\cdotp\cdotp</c>, a document nobody
    /// has open. Set once, where the expansion is cached, and never per use: it says where an atom came
    /// from, which is the same answer for everyone who asks for that macro.
    /// </para>
    /// </summary>
    internal bool Borrowed { get; set; }

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

        // A borrowed atom never lends a box its offsets. They name a macro's definition and not the
        // formula, so letting one through would put a point in the layout that refers to text the reader
        // has never seen — the one thing a layout tree may never do. Enforced here because this is the
        // single place every box passes through, and taken off the whole box rather than merely withheld:
        // several atoms hand their box a source in its constructor, so declining to add one is not the
        // same as there being none.
        if (this.Borrowed)
        {
            Disown(box);
        }
        else if (box.Source == null)
        {
            box.Source = this.Source;
        }

        // The way back from a drawn thing to what it means. Set here, in the one place every box is made,
        // so no atom can forget it.
        //
        // The innermost atom wins, except against one that does not know where it came from. Several atoms
        // are pure wrappers — a TypedAtom, a DummyAtom — and hand back the box of the atom inside them
        // rather than making one, so the box arrives here already claimed by something further in. Where
        // that something has no part, and this does, this is the better answer: a macro's expansion is
        // exactly that shape, its insides belonging to a definition and only its outermost atom knowing
        // which token the reader wrote.
        if (box.Node is null || (box.Node.Origin is null && this.Origin is not null)) box.Node = this;

        return box;
    }

    /// <summary>Takes the definition's offsets off a box and everything under it.</summary>
    private static void Disown(Box box)
    {
        box.Source = null;
        foreach (var child in box.Children) Disown(child);
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
