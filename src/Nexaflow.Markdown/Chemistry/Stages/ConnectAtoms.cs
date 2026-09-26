using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Chemistry.Stages;

/// <summary>
/// Says what each atom is and which atoms are bonded to which: each atom as its <see cref="AtomNode"/>, each ring-closure digit
/// paired with the one that closes it (<see cref="RingBondNode"/>), each bond symbol as the bond it made (<see cref="BondNode"/>),
/// and the molecule as the graph they make (<see cref="MoleculeNode"/>).
///
/// <para>
/// A digit is a promise that the atom it follows is bonded to whichever atom next writes the same digit — so a
/// ring bond is a fact about two places in the string at once, and neither of them says it. By the time anything
/// counts an atom's bonds, that has to have an answer. Nor does anything written say that two atoms side by side are bonded:
/// that is what a chain means.
/// </para>
/// <para>
/// A number is free again once it closes, which is how <c>C1CC1C1CC1</c> is two rings. What cannot be paired is
/// said where it was written: a digit that never closes, one that closes onto its own atom or onto an atom it is
/// already bonded to, and a pair whose two ends name different bonds.
/// </para>
/// <para>
/// <b>Atoms are numbered in the order they are written</b>, branches included, which is the numbering every SMILES reader
/// uses and the one every later stage and the builder name them by.
/// </para>
/// </summary>
public sealed class ConnectAtoms : IAstStage
{
    public string Name => "smiles:rings";

    public ContentNode Run(ContentNode tree) => SmilesRewrite.Molecules(tree, Connect);

    private sealed record Closure(int Ring, int Atom, int Partner, string? Trouble);

    private static ContentNode Connect(ContentNode molecule)
    {
        var closures = Closures(molecule);
        var graph = new Graph(closures);
        graph.Walk(molecule, previous: -1);

        var atoms = 0;
        var rings = 0;

        return AstRewrite.Each(molecule, node => node.Kind switch
        {
            SmilesKinds.Atom => graph.Atom(node, atoms++),
            SmilesKinds.RingBond => graph.Ring(node, rings++),
            SmilesKinds.Bond when graph.Made.TryGetValue(node, out var bond) => new BondNode(node, bond),
            SmilesKinds.Molecule => graph.Molecule(node),
            _ => node,
        });
    }

    // ── Pairing ring closures ───────────────────────────────────────────────

    /// <summary>What each ring closure, by the order they are written, reaches — or why it reaches nothing.</summary>
    private static Dictionary<int, Closure> Closures(ContentNode molecule)
    {
        var closures = new Dictionary<int, Closure>();
        var open = new Dictionary<int, (int Ring, int Atom, string? Bond)>();
        var bonded = new HashSet<(int, int)>();
        var atoms = 0;
        var rings = 0;

        Walk(molecule, previous: -1);

        foreach (var (number, (ring, atom, _)) in open)
            closures[ring] = new Closure(ring, atom, -1, $"Ring {number} is never closed.");

        return closures;

        int Walk(ContentNode chain, int previous)
        {
            foreach (var item in chain.Children)
            {
                switch (item.Kind)
                {
                    case SmilesKinds.Atom:
                        if (previous >= 0) bonded.Add(Key(previous, atoms));
                        previous = atoms++;
                        break;

                    case SmilesKinds.Dot:
                        previous = -1;
                        break;

                    case SmilesKinds.Branch:
                        Walk(item, previous);
                        break;

                    case SmilesKinds.RingBond:
                    {
                        var ring = rings++;
                        if (item.Trouble is not null || previous < 0) break;

                        var number = int.Parse(item.Children.First(child => child.Kind == SmilesKinds.RingNumber).Text.TrimStart('%'));
                        var bond = item.Children.FirstOrDefault(child => child.Kind == SmilesKinds.Bond)?.Text;

                        if (!open.Remove(number, out var start))
                        {
                            open[number] = (ring, previous, bond);
                            break;
                        }

                        var trouble =
                            start.Atom == previous ? $"Ring {number} closes onto the atom that opened it."
                            : bonded.Contains(Key(start.Atom, previous)) ? $"Ring {number} joins two atoms that are already bonded."
                            : Disagree(start.Bond, bond) ? $"The two ends of ring {number} name different bonds."
                            : null;

                        if (trouble is not null)
                        {
                            // The opening end is left as it was: it is the closing digit that asked for the impossible.
                            closures[ring] = new Closure(ring, previous, -1, trouble);
                            break;
                        }

                        bonded.Add(Key(start.Atom, previous));
                        closures[start.Ring] = new Closure(start.Ring, start.Atom, previous, null);
                        closures[ring] = new Closure(ring, previous, start.Atom, null);
                        break;
                    }
                }
            }

            return previous;
        }
    }

    private static (int, int) Key(int a, int b) => (Math.Min(a, b), Math.Max(a, b));

    /// <summary>
    /// Whether two ends name different bonds. One end saying nothing agrees with anything, and <c>/</c> against
    /// <c>\</c> is the same single bond seen from either end.
    /// </summary>
    private static bool Disagree(string? one, string? other)
    {
        if (one is null || other is null || one == other) return false;
        return !(one is "/" or "\\" or "-" && other is "/" or "\\" or "-");
    }

    // ── The graph they make ─────────────────────────────────────────────────

    /// <summary>
    /// The bonds a molecule is written as, walked in the order written: each atom bonded to the one before it in its chain, and
    /// each ring closure, once paired, to the atom at its other end.
    /// </summary>
    private sealed class Graph(Dictionary<int, Closure> closures)
    {
        private readonly List<(ContentNode Node, bool Preceded)> _atoms = [];
        private readonly List<MoleculeBond> _bonds = [];
        private readonly List<List<int>> _around = [];

        /// <summary>For each atom, what it was bonded to in the order those bonds were written — which chirality is read against.</summary>
        private readonly List<List<int>> _written = [];

        /// <summary>The bond each paired ring closure closes, by the order ring closures are written — at its closing end.</summary>
        private readonly Dictionary<int, int> _closing = [];

        private int _rings;

        /// <summary>The bond each written bond symbol made.</summary>
        public Dictionary<ContentNode, int> Made { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// A chain, bonding each atom to the one before it. Returns the last atom read, which is what whatever follows a
        /// branch bonds to — the atom the branch hangs off, not the end of the branch.
        /// </summary>
        public int Walk(ContentNode chain, int previous)
        {
            var opened = new Dictionary<(int, int), ContentNode>();
            return this.Walk(chain, previous, opened);
        }

        private int Walk(ContentNode chain, int previous, Dictionary<(int, int), ContentNode> opened)
        {
            ContentNode? bond = null;

            foreach (var item in chain.Children)
            {
                switch (item.Kind)
                {
                    case SmilesKinds.Atom:
                    {
                        var atom = _atoms.Count;
                        _atoms.Add((item, previous >= 0));
                        _around.Add([]);
                        _written.Add([]);

                        if (previous >= 0) this.Bond(previous, atom, bond, placeholder: false);
                        if (previous >= 0 && bond is not null) Made[bond] = _bonds.Count - 1;

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
                        this.Walk(item, previous, opened);
                        break;

                    case SmilesKinds.RingBond:
                    {
                        var ring = _rings++;
                        if (previous < 0 || !closures.TryGetValue(ring, out var closure) || closure.Partner < 0) break;

                        var partner = closure.Partner;
                        var key = Key(previous, partner);

                        if (partner > previous)
                        {
                            // The opening end: the partner has not been read yet, so the bond waits for it. Its place in
                            // this atom's neighbours is taken now, because that is where handedness reads it.
                            opened[key] = item;
                            _written[previous].Add(-1 - partner);
                        }
                        else if (opened.Remove(key, out var start))
                        {
                            this.Bond(partner, previous, Symbol(item) ?? Symbol(start), placeholder: true);
                            _closing[ring] = _bonds.Count - 1;
                        }

                        break;
                    }
                }
            }

            return previous;
        }

        /// <summary>The bond symbol written in a ring closure, or null.</summary>
        private static ContentNode? Symbol(ContentNode ring) =>
            ring.Children.FirstOrDefault(child => child.Kind == SmilesKinds.Bond);

        private void Bond(int from, int to, ContentNode? symbol, bool placeholder)
        {
            var order = symbol?.Text switch
            {
                "=" => BondOrder.Double,
                "#" => BondOrder.Triple,
                "$" => BondOrder.Quadruple,
                ":" => BondOrder.Aromatic,
                "-" or "/" or "\\" => BondOrder.Single,
                _ => Aromatic(from) && Aromatic(to) ? BondOrder.Aromatic : BondOrder.Single,
            };

            char? direction = symbol?.Text is "/" or "\\" ? symbol.Text[0] : null;

            var index = _bonds.Count;
            _bonds.Add(new MoleculeBond(index, from, to, order, direction));
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

        /// <summary>Whether an atom was written in lowercase, which is SMILES saying it is aromatic.</summary>
        private bool Aromatic(int atom) =>
            _atoms[atom].Node.Children.FirstOrDefault(part => part.Kind == SmilesKinds.Symbol)?.Text is { Length: > 0 } symbol
            && char.IsAsciiLetterLower(symbol[0]);

        /// <summary>An atom as what its characters say it is.</summary>
        public ContentNode Atom(ContentNode node, int index)
        {
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

            return new AtomNode(node, index, Elements.Number(written) ?? 0, written, bracketed, isotope, charge,
                                bracketed ? hydrogens ?? 0 : null, chirality, @class);
        }

        /// <summary>A ring closure as what it was paired with, or with why it could not be.</summary>
        public ContentNode Ring(ContentNode node, int ring)
        {
            if (!closures.TryGetValue(ring, out var closure)) return node;

            var paired = closure.Partner >= 0
                ? new RingBondNode(node, closure.Partner, _closing.TryGetValue(ring, out var bond) ? bond : null)
                : node;

            return SmilesRewrite.Troubled(paired, closure.Trouble);
        }

        /// <summary>The molecule as the graph its atoms and bonds make.</summary>
        public ContentNode Molecule(ContentNode node) =>
            new MoleculeNode(node, _bonds, [.. _around], [.. _written], [.. _atoms.Select(atom => atom.Preceded)]);

        /// <summary><c>+</c>, <c>++</c>, <c>+2</c>, <c>-</c>…</summary>
        private static int ChargeOf(string text)
        {
            var sign = text[0] == '-' ? -1 : 1;
            if (text.Length == 1) return sign;
            return char.IsAsciiDigit(text[1]) ? sign * int.Parse(text[1..]) : sign * text.Length;
        }
    }
}
