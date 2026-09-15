using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

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

/// <summary>One atom of a <see cref="Molecule"/>.</summary>
/// <param name="Index">Its place in the order atoms are written — what a stage's facts name it by.</param>
/// <param name="Node">The piece of the tree it was read from.</param>
/// <param name="Number">Its atomic number; zero for <c>*</c>.</param>
/// <param name="Written">The symbol as written, lowercase where aromatic.</param>
/// <param name="Hydrogens">The hydrogens written in its brackets, or null for an atom without brackets.</param>
/// <param name="Implicit">The hydrogens it carries without their being written — a stage's answer.</param>
/// <param name="Chirality">Its handedness as written, or null.</param>
/// <param name="DoubleTo">The atom an aromatic atom shares its double bond with once its ring is drawn alternating — a stage's answer.</param>
public sealed record MoleculeAtom(
    int Index, ContentNode Node, int Number, string Written, bool Aromatic, bool Bracketed,
    int? Isotope, int Charge, int? Hydrogens, int Implicit, string? Chirality, int? Class, int? DoubleTo)
{
    /// <summary>The element's symbol, properly cased.</summary>
    public string Symbol => Elements.Symbol(Number);

    /// <summary>Every hydrogen on it, written or not.</summary>
    public int TotalHydrogens => (Hydrogens ?? 0) + Implicit;
}

/// <summary>One bond of a <see cref="Molecule"/>.</summary>
/// <param name="From">The atom written first.</param>
/// <param name="To">The atom written second.</param>
/// <param name="Node">
/// What it was written as: a bond symbol, or the ring closure that closed it — null for the bond between two atoms
/// written side by side, which nobody typed.
/// </param>
/// <param name="Direction">
/// <c>/</c> or <c>\</c> where one was written, which says which side of a double bond this one leaves on.
/// </param>
public sealed record MoleculeBond(int Index, int From, int To, BondOrder Order, ContentNode? Node, char? Direction)
{
    /// <summary>The atom at the other end from <paramref name="atom"/>.</summary>
    public int Other(int atom) => atom == From ? To : From;
}

/// <summary>
/// A molecule as a graph: its atoms and the bonds between them, read off a <see cref="SmilesKinds.Molecule"/> tree.
///
/// <para>
/// A reading, not a stage. What it knows is what the tree says, and what the stages before it have hung there: a
/// ring closure is only a bond once <see cref="Stages.ConnectAtoms"/> has said which atom it reaches, and an atom
/// only has hydrogens once <see cref="Stages.CountHydrogens"/> has counted them. Read off a bare parse it is the
/// atoms and the chain bonds and nothing more — which is exactly what the stages that work those facts out need.
/// </para>
/// <para>
/// <b>Atoms are numbered in the order they are written</b>, branches included, which is the numbering every
/// SMILES reader uses and the one a stage's facts name them by.
/// </para>
/// </summary>
public sealed class Molecule
{
    private readonly List<MoleculeAtom> _atoms = [];
    private readonly List<MoleculeBond> _bonds = [];
    private readonly List<List<int>> _around = [];

    /// <summary>For each atom, what it was bonded to in the order those bonds were written — which chirality is read against.</summary>
    private readonly List<List<int>> _written = [];

    private readonly List<bool> _preceded = [];

    private Molecule() { }

    public IReadOnlyList<MoleculeAtom> Atoms => _atoms;

    public IReadOnlyList<MoleculeBond> Bonds => _bonds;

    /// <summary>The bonds at <paramref name="atom"/>, by index into <see cref="Bonds"/>.</summary>
    public IReadOnlyList<int> BondsAt(int atom) => _around[atom];

    /// <summary>
    /// The atoms <paramref name="atom"/> is bonded to, in the order SMILES reads its handedness against: the atom
    /// before it, then its ring closures, then what follows it.
    /// </summary>
    public IReadOnlyList<int> NeighboursAsWritten(int atom) => _written[atom];

    /// <summary>Whether <paramref name="atom"/> was written after an atom it bonds to, rather than first in its chain.</summary>
    public bool Preceded(int atom) => _preceded[atom];

    /// <summary>The bond between two atoms, or null.</summary>
    public MoleculeBond? Between(int a, int b)
    {
        foreach (var bond in _around[a])
            if (_bonds[bond].Other(a) == b) return _bonds[bond];
        return null;
    }

    /// <summary>
    /// How many bonds <paramref name="atom"/> makes by what is written, an aromatic bond counting one — the number
    /// its hydrogens are counted from.
    /// </summary>
    public int Valence(int atom)
    {
        var sum = 0;
        foreach (var bond in _around[atom])
            sum += _bonds[bond].Order is BondOrder.Aromatic ? 1 : (int)_bonds[bond].Order;
        return sum;
    }

    /// <summary>
    /// How a bond is drawn: as written, except that an aromatic bond is double where <see cref="Stages.Kekulize"/>
    /// paired its atoms and single where it paired them elsewhere — and stays aromatic only where the ring could not
    /// be given alternating bonds at all.
    /// </summary>
    public BondOrder Drawn(MoleculeBond bond)
    {
        if (bond.Order != BondOrder.Aromatic) return bond.Order;

        var (from, to) = (_atoms[bond.From], _atoms[bond.To]);
        if (from.DoubleTo == bond.To) return BondOrder.Double;
        if (from.Node.Trouble is not null || to.Node.Trouble is not null) return BondOrder.Aromatic;
        return BondOrder.Single;
    }

    /// <summary>The molecules in <paramref name="block"/>, in the order written, each with the entry it came from.</summary>
    public static IEnumerable<ContentNode> In(ContentNode block) =>
        block.SelfAndDescendants().Where(node => node.Kind == SmilesKinds.Molecule);

    /// <summary>Reads one molecule's tree.</summary>
    public static Molecule Read(ContentNode molecule)
    {
        var read = new Molecule();
        var opened = new Dictionary<(int, int), ContentNode>();
        read.Walk(molecule, previous: -1, ref opened);
        return read;
    }

    /// <summary>
    /// A chain, bonding each atom to the one before it. Returns the last atom read, which is what whatever follows a
    /// branch bonds to — the atom the branch hangs off, not the end of the branch.
    /// </summary>
    private int Walk(ContentNode chain, int previous, ref Dictionary<(int, int), ContentNode> opened)
    {
        ContentNode? bond = null;

        foreach (var item in chain.Children)
        {
            switch (item.Kind)
            {
                case SmilesKinds.Atom:
                {
                    var atom = Atom(item, previous >= 0);
                    if (previous >= 0) Bond(previous, atom, bond);
                    previous = atom;
                    bond = null;
                    break;
                }

                case SmilesKinds.Bond:
                    bond = item.Trouble is null ? item : null;
                    break;

                case SmilesKinds.Dot:
                    previous = -1;
                    bond = null;
                    break;

                case SmilesKinds.Branch:
                    Walk(item, previous, ref opened);
                    break;

                case SmilesKinds.RingBond when previous >= 0 && item.Said(SmilesRoles.Partner) is { } said
                                               && int.TryParse(said, out var partner):
                {
                    var key = (Math.Min(previous, partner), Math.Max(previous, partner));

                    if (partner > previous)
                    {
                        // The opening end: the partner has not been read yet, so the bond waits for it. Its place in
                        // this atom's neighbours is taken now, because that is where handedness reads it.
                        opened[key] = item;
                        _written[previous].Add(-1 - partner);
                    }
                    else if (opened.Remove(key, out var start))
                    {
                        var symbol = Symbol(item) ?? Symbol(start);
                        Bond(partner, previous, symbol, item, placeholder: true);
                    }

                    break;
                }
            }
        }

        return previous;
    }

    private int Atom(ContentNode node, bool preceded)
    {
        var index = _atoms.Count;

        string written = "*";
        int? isotope = null, hydrogens = null, @class = null;
        var charge = 0;
        string? chirality = null;

        foreach (var part in node.Children)
        {
            switch (part.Kind)
            {
                case SmilesKinds.Symbol: written = part.Text; break;
                case SmilesKinds.Isotope: isotope = int.Parse(part.Text); break;
                case SmilesKinds.Chirality: chirality = part.Text; break;
                case SmilesKinds.Hydrogens: hydrogens = part.Text.Length == 1 ? 1 : int.Parse(part.Text[1..]); break;
                case SmilesKinds.Charge: charge = ChargeOf(part.Text); break;
                case SmilesKinds.Class: @class = int.Parse(part.Text[1..]); break;
            }
        }

        var bracketed = node.Children.Count > 0 && node.Children[0].Role == Roles.Open;
        var implicitly = int.TryParse(node.Said(SmilesRoles.ImplicitHydrogens), out var h) ? h : 0;
        int? doubleTo = int.TryParse(node.Said(SmilesRoles.DoubleTo), out var d) ? d : null;

        _atoms.Add(new MoleculeAtom(index, node, Elements.Number(written) ?? 0, written,
                                    Aromatic: written.Length > 0 && char.IsAsciiLetterLower(written[0]),
                                    bracketed, isotope, charge, bracketed ? hydrogens ?? 0 : null, implicitly,
                                    chirality, @class, doubleTo));
        _around.Add([]);
        _written.Add([]);
        _preceded.Add(preceded);
        return index;
    }

    /// <summary><c>+</c>, <c>++</c>, <c>+2</c>, <c>-</c>…</summary>
    private static int ChargeOf(string text)
    {
        var sign = text[0] == '-' ? -1 : 1;
        if (text.Length == 1) return sign;
        return char.IsAsciiDigit(text[1]) ? sign * int.Parse(text[1..]) : sign * text.Length;
    }

    /// <summary>The bond symbol written in a ring closure, or null.</summary>
    private static ContentNode? Symbol(ContentNode ring) =>
        ring.Children.FirstOrDefault(child => child.Kind == SmilesKinds.Bond);

    private void Bond(int from, int to, ContentNode? written) =>
        Bond(from, to, written, written, placeholder: false);

    private void Bond(int from, int to, ContentNode? symbol, ContentNode? node, bool placeholder)
    {
        var order = symbol?.Text switch
        {
            "=" => BondOrder.Double,
            "#" => BondOrder.Triple,
            "$" => BondOrder.Quadruple,
            ":" => BondOrder.Aromatic,
            "-" or "/" or "\\" => BondOrder.Single,
            _ => _atoms[from].Aromatic && _atoms[to].Aromatic ? BondOrder.Aromatic : BondOrder.Single,
        };

        char? direction = symbol?.Text is "/" or "\\" ? symbol.Text[0] : null;

        var index = _bonds.Count;
        _bonds.Add(new MoleculeBond(index, from, to, order, node, direction));
        _around[from].Add(index);
        _around[to].Add(index);

        // A ring closure took its place in the opening atom's neighbours when it was written; it is filled in now.
        if (placeholder)
        {
            var slot = _written[from].IndexOf(-1 - to);
            if (slot >= 0) _written[from][slot] = to;
            else _written[from].Add(to);
        }
        else
        {
            _written[from].Add(to);
        }

        _written[to].Add(from);
    }
}
