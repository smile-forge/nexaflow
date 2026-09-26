using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Chemistry.Depiction;

namespace Nexaflow.Markdown.Chemistry;

/// <summary>How many electron pairs a bond shares, as SMILES writes it.</summary>
public enum BondOrder
{
    Single = 1,
    Double = 2,
    Triple = 3,
    Quadruple = 4,

    /// <summary>A bond in an aromatic ring, before the ring is drawn alternating.</summary>
    Aromatic = 5,
}

/// <summary>One bond of a molecule, as <see cref="Stages.ConnectAtoms"/> says it.</summary>
/// <param name="From">The atom written first, by the order atoms are written in.</param>
/// <param name="To">The atom written second.</param>
/// <param name="Direction">
/// <c>/</c> or <c>\</c> where one was written, which says which side of a double bond this one leaves on.
/// </param>
public sealed record MoleculeBond(int Index, int From, int To, BondOrder Order, char? Direction)
{
    /// <summary>The atom at the other end from <paramref name="atom"/>.</summary>
    public int Other(int atom) => atom == From ? To : From;
}

/// <summary>
/// A molecule as its stages leave it: which of its atoms are bonded to which (<see cref="Stages.ConnectAtoms"/>) — the bonds
/// between two atoms written side by side included, which nobody typed — and, once <see cref="Stages.DepictStructure"/> has
/// been over it, where every atom goes. It prints as the molecule written.
///
/// <para>
/// <b>Atoms are numbered in the order they are written</b>, branches included, which is the numbering every SMILES reader uses:
/// <see cref="Atoms"/> are this molecule's own <see cref="AtomNode"/>s in that order, and a bond names its atoms by it.
/// </para>
/// </summary>
internal sealed class MoleculeNode : ContentNode
{
    private readonly IReadOnlyList<IReadOnlyList<int>> _around;
    private readonly IReadOnlyList<IReadOnlyList<int>> _written;
    private readonly IReadOnlyList<bool> _preceded;
    private IReadOnlyList<AtomNode>? _atoms;

    internal MoleculeNode(ContentNode written, IReadOnlyList<MoleculeBond> bonds, IReadOnlyList<IReadOnlyList<int>> around,
                          IReadOnlyList<IReadOnlyList<int>> neighbours, IReadOnlyList<bool> preceded, Structure? structure = null)
        : base(written)
    {
        this.Bonds = bonds;
        _around = around;
        _written = neighbours;
        _preceded = preceded;
        this.Structure = structure;
    }

    /// <summary>Its atoms, in the order they are written.</summary>
    public IReadOnlyList<AtomNode> Atoms => _atoms ??= [.. Written(this)];

    public IReadOnlyList<MoleculeBond> Bonds { get; }

    /// <summary>Where its atoms go on the page — null until <see cref="Stages.DepictStructure"/> has said.</summary>
    public Structure? Structure { get; }

    /// <summary>The bonds at <paramref name="atom"/>, by index into <see cref="Bonds"/>.</summary>
    public IReadOnlyList<int> BondsAt(int atom) => _around[atom];

    /// <summary>
    /// The atoms <paramref name="atom"/> is bonded to, in the order SMILES reads its handedness against: the atom before it,
    /// then its ring closures, then what follows it.
    /// </summary>
    public IReadOnlyList<int> NeighboursAsWritten(int atom) => _written[atom];

    /// <summary>Whether <paramref name="atom"/> was written after an atom it bonds to, rather than first in its chain.</summary>
    public bool Preceded(int atom) => _preceded[atom];

    /// <summary>The bond between two atoms, or null.</summary>
    public MoleculeBond? Between(int a, int b)
    {
        foreach (var bond in _around[a])
            if (this.Bonds[bond].Other(a) == b) return this.Bonds[bond];

        return null;
    }

    /// <summary>
    /// How many bonds <paramref name="atom"/> makes by what is written, an aromatic bond counting one — the number its
    /// hydrogens are counted from.
    /// </summary>
    public int Valence(int atom)
    {
        var sum = 0;
        foreach (var bond in _around[atom])
            sum += this.Bonds[bond].Order is BondOrder.Aromatic ? 1 : (int)this.Bonds[bond].Order;

        return sum;
    }

    /// <summary>
    /// How a bond is drawn: as written, except that an aromatic bond is double where <see cref="Stages.Kekulize"/> paired its
    /// atoms and single where it paired them elsewhere — and stays aromatic only where the ring could not be given alternating
    /// bonds at all.
    /// </summary>
    public BondOrder Drawn(MoleculeBond bond)
    {
        if (bond.Order != BondOrder.Aromatic) return bond.Order;

        var (from, to) = (this.Atoms[bond.From], this.Atoms[bond.To]);
        if (from.DoubleTo == bond.To) return BondOrder.Double;
        if (from.Trouble is not null || to.Trouble is not null) return BondOrder.Aromatic;

        return BondOrder.Single;
    }

    /// <summary>The same molecule, with where its atoms go.</summary>
    internal MoleculeNode Depicted(Structure structure) =>
        new(this, this.Bonds, _around, _written, _preceded, structure);

    protected override ContentNode Reshaped(ContentNode shape) =>
        new MoleculeNode(shape, this.Bonds, _around, _written, _preceded, this.Structure);

    /// <summary>The atoms under a piece, in the order they are written.</summary>
    private static IEnumerable<AtomNode> Written(ContentNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.IsDerived) continue;

            if (child is AtomNode atom) yield return atom;
            else foreach (var under in Written(child)) yield return under;
        }
    }
}

/// <summary>
/// An atom as its stages leave it: what its characters say it is (<see cref="Stages.ConnectAtoms"/>), the hydrogens it carries
/// without their being written (<see cref="Stages.CountHydrogens"/>), and the atom it shares a double bond with once its ring is
/// drawn alternating (<see cref="Stages.Kekulize"/>). It prints as the atom written.
/// </summary>
internal sealed class AtomNode : ContentNode
{
    internal AtomNode(ContentNode written, int index, int number, string symbol, bool bracketed, int? isotope, int charge,
                      int? hydrogens, string? chirality, int? @class, int implicitly = 0, int? doubleTo = null)
        : base(written)
    {
        this.Index = index;
        this.Number = number;
        this.Written = symbol;
        this.Bracketed = bracketed;
        this.Isotope = isotope;
        this.Charge = charge;
        this.Hydrogens = hydrogens;
        this.Chirality = chirality;
        this.Class = @class;
        this.Implicit = implicitly;
        this.DoubleTo = doubleTo;
    }

    /// <summary>Its place in the order atoms are written — what a bond names it by.</summary>
    public int Index { get; }

    /// <summary>Its atomic number; zero for <c>*</c>.</summary>
    public int Number { get; }

    /// <summary>The symbol as written, lowercase where aromatic.</summary>
    public string Written { get; }

    public bool Aromatic => this.Written.Length > 0 && char.IsAsciiLetterLower(this.Written[0]);

    public bool Bracketed { get; }

    public int? Isotope { get; }

    public int Charge { get; }

    /// <summary>The hydrogens written in its brackets, or null for an atom without brackets.</summary>
    public int? Hydrogens { get; }

    /// <summary>Its handedness as written, or null.</summary>
    public string? Chirality { get; }

    public int? Class { get; }

    /// <summary>The hydrogens it carries without their being written.</summary>
    public int Implicit { get; }

    /// <summary>The atom an aromatic atom shares its double bond with once its ring is drawn alternating.</summary>
    public int? DoubleTo { get; }

    /// <summary>The element's symbol, properly cased.</summary>
    public string Symbol => Elements.Symbol(this.Number);

    /// <summary>Every hydrogen on it, written or not.</summary>
    public int TotalHydrogens => (this.Hydrogens ?? 0) + this.Implicit;

    /// <summary>The same atom, carrying hydrogens nobody wrote.</summary>
    internal AtomNode Carrying(int implicitly) => this.Copy(this, implicitly, this.DoubleTo);

    /// <summary>The same atom, sharing its double bond with <paramref name="partner"/>.</summary>
    internal AtomNode Sharing(int partner) => this.Copy(this, this.Implicit, partner);

    protected override ContentNode Reshaped(ContentNode shape) => this.Copy(shape, this.Implicit, this.DoubleTo);

    private AtomNode Copy(ContentNode shape, int implicitly, int? doubleTo) =>
        new(shape, this.Index, this.Number, this.Written, this.Bracketed, this.Isotope, this.Charge, this.Hydrogens,
            this.Chirality, this.Class, implicitly, doubleTo);
}

/// <summary>
/// A bond symbol written between two atoms, standing for the bond it made (<see cref="Stages.ConnectAtoms"/>). A symbol that
/// made none — one with nothing after it — is left as written.
/// </summary>
internal sealed class BondNode : ContentNode
{
    internal BondNode(ContentNode written, int bond) : base(written) => this.Bond = bond;

    /// <summary>Which of its molecule's <see cref="MoleculeNode.Bonds"/> it stands for.</summary>
    public int Bond { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new BondNode(shape, this.Bond);
}

/// <summary>
/// A ring-closure digit, paired with the one that closes it (<see cref="Stages.ConnectAtoms"/>): the atom at its other end,
/// and — at the end that closes the ring — the bond the two make.
/// </summary>
internal sealed class RingBondNode : ContentNode
{
    internal RingBondNode(ContentNode written, int partner, int? bond) : base(written)
    {
        this.Partner = partner;
        this.Bond = bond;
    }

    /// <summary>The atom at the other end.</summary>
    public int Partner { get; }

    /// <summary>Which of its molecule's <see cref="MoleculeNode.Bonds"/> it closes, at the closing end; null at the opening end.</summary>
    public int? Bond { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new RingBondNode(shape, this.Partner, this.Bond);
}
