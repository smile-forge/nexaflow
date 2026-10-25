using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Chemistry;
using Nexaflow.Markdown.Chemistry.Depiction;
using Nexaflow.Markdown.Chemistry.Stages;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Chemistry;

/// <summary>
/// Lays a <c>smiles</c> block out onto the renderer-free layout tree: each molecule as a skeletal structure with its caption beneath,
/// flowing left to right and wrapping to the room given. Where the atoms go is the stages' to say (<see cref="DepictStructure"/>); this
/// decides how a chemist draws them (bare-corner carbon, half-and-half bond colouring, wedges for stereocentres). Every atom and bond is a
/// piece carrying the source it was drawn from, so selection yields SMILES text and errors underline the offending atom. Anything wrong
/// in the block is put right in its source, shown as written.
/// </summary>
public sealed class PortableSmilesBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                                          PortableHost host)
    : PortableContentBuilder(reading, state, style, isReadOnly, medium, host)
{
    private const string LabelFont = "Segoe UI, Arial, sans-serif";
    private const string SourceFont = "Cascadia Code, Consolas, monospace";

    private const double BondLength = 28;

    private const double LabelSize = 14;
    private const double CaptionSize = 12.5;
    private const double StrokeShare = 0.062;

    private const double GapShare = 0.2;

    private const double InsetShare = 0.14;

    /// <summary>Clear space between two entries across a row, and between two rows.</summary>
    private const double Across = 28;
    private const double Down = 18;

    /// <summary>Clear space round a structure, so a label at its edge is not cut off by the element's own.</summary>
    private const double Margin = 6;

    /// <summary>The smallest a structure is drawn at to fit its room, before it is let overflow instead.</summary>
    private const double SmallestScale = 0.5;

    /// <summary>How big the characters of a block shown as written are set.</summary>
    private const double SourceSize = 13;

    protected override PortableLaid? Build()
    {
        var entries = Reading.Root.SelfAndDescendants()
            .Where(part => part.Kind == SmilesKinds.Entry || (part.Kind == Kinds.Verbatim && part.Parent?.Kind == SmilesKinds.Line))
            .Select(Sketch)
            .ToList();

        if (entries.Count == 0) return null;

        // A molecule is only ever read where it is drawn — nothing in a structure is typed into — so anything wrong in the block, and
        // any entry that makes no molecule at all, is put right in its source: shown as written, each such part marked.
        var troubled = Reading.Root.SelfAndDescendants().Where(part => part.Trouble is not null && !part.Derived).ToList();
        IReadOnlyList<(ContentPart Part, string Reason)> blamed =
        [
            .. troubled.Select(part => (part, part.Trouble!)),
            .. entries.Where(entry => entry.Structure is null && !entry.Part.SelfAndDescendants().Any(troubled.Contains))
                      .Select(entry => (entry.Part, Style.Says("Text.Smiles.NoMolecule"))),
        ];
        if (blamed.Count > 0) return AsSource(blamed);

        // One piece holds the block, because a layout has one root and every entry is a piece inside it.
        var build = new PortableBuilder(Medium);
        build.Open(MoleculePiece.Block, Reading.Root);
        var (wide, tall) = Flow(build, entries);
        build.Close();

        return new PortableLaid(build.Seal(), wide, tall, []);
    }

    // ── Entries ─────────────────────────────────────────────────────────────

    /// <summary>One entry, measured but not yet placed: what it draws, and how much room that takes.</summary>
    private sealed class Sketched
    {
        public required ContentPart Part { get; init; }
        public Drawing? Structure { get; init; }
        public IShapedText? Caption { get; init; }
        public ContentPart? CaptionPart { get; init; }

        public (double Width, double Height) Body => Structure?.Size ?? (0, 0);

        public double Width => Math.Max(Body.Width, Caption?.Width ?? 0);
    }

    private Sketched Sketch(ContentPart entry)
    {
        var molecule = entry.Children.FirstOrDefault(child => child.Kind == SmilesKinds.Molecule);
        var label = entry.Children.FirstOrDefault(child => child.Kind == SmilesKinds.Label)?.Part(SmilesRoles.Label);

        Drawing? drawing = null;
        if (molecule?.Node is MoleculeNode { Structure: { } structure, Atoms.Count: > 0 } read)
            drawing = new Drawing(this, molecule, read, structure);

        return new Sketched
        {
            Part = entry,
            Structure = drawing,
            Caption = label is null ? null : Text(label.Text, CaptionSize, LabelFont, Style.TextMuted),
            CaptionPart = label,
        };
    }

    /// <summary>Rows entries left to right, wrapping when the next won't fit; captions share the line beneath the tallest.</summary>
    private (double Width, double Height) Flow(PortableBuilder build, List<Sketched> entries)
    {
        var rows = new List<List<Sketched>>();
        var row = new List<Sketched>();
        var across = 0.0;

        foreach (var entry in entries)
        {
            var needed = entry.Width + (row.Count > 0 ? Across : 0);
            if (row.Count > 0 && across + needed > Room)
            {
                rows.Add(row);
                row = [];
                needed = entry.Width;
                across = 0;
            }

            row.Add(entry);
            across += needed;
        }

        if (row.Count > 0) rows.Add(row);

        var top = 0.0;
        var widest = 0.0;

        foreach (var line in rows)
        {
            var body = line.Max(entry => entry.Body.Height);
            var left = 0.0;
            var bottom = 0.0;

            foreach (var entry in line)
            {
                build.Open(MoleculePiece.Entry, entry.Part, new LayoutPoint(left, top));

                var bodyTop = (body - entry.Body.Height) / 2;
                var bodyLeft = (entry.Width - entry.Body.Width) / 2;

                if (entry.Structure is { } structure)
                {
                    build.Open(MoleculePiece.Molecule, structure.Part, new LayoutPoint(bodyLeft, bodyTop));
                    structure.Lay(build);
                    build.Close();
                }

                var below = body + (entry.Caption is null ? 0 : 6);

                if (entry.Caption is { } caption)
                {
                    build.Open(MoleculePiece.Caption, entry.CaptionPart, new LayoutPoint((entry.Width - caption.Width) / 2, below));
                    build.Draw(new PortableTextMark(caption, default, Style.TextMuted));
                    build.Close();
                    below += caption.Height;
                }

                build.Close();

                bottom = Math.Max(bottom, below);
                left += entry.Width + Across;
            }

            widest = Math.Max(widest, left - Across);
            top += bottom + Down;
        }

        return (widest, Math.Max(0, top - Down));
    }

    // ── One structure ───────────────────────────────────────────────────────

    /// <summary>A molecule worked out and measured — atom positions, labels, bond endpoints — ready to lay down.</summary>
    private sealed class Drawing
    {
        private readonly PortableSmilesBuilder _owner;
        private readonly MoleculeNode _molecule;
        private readonly Structure _structure;

        /// <summary>The part each atom was drawn from, by the order atoms are written in.</summary>
        private readonly ContentPart?[] _atoms;

        /// <summary>The part each bond was written as, where one was — a bond between two atoms side by side was not.</summary>
        private readonly Dictionary<int, ContentPart> _written = [];
        private readonly LayoutPoint[] _at;
        private readonly Label?[] _labels;
        private readonly double _scale;

        /// <summary>How much longer than usual this structure's bonds are drawn, so its symbols clear one another.</summary>
        private readonly double _spread;

        /// <summary>The most a structure's bonds are lengthened to keep its symbols apart.</summary>
        private const double LongestSpread = 1.5;

        /// <summary>For each bond of a solid that passes behind another, where on the page it does.</summary>
        private readonly Dictionary<int, List<LayoutPoint>> _gaps;

        public Drawing(PortableSmilesBuilder owner, ContentPart part, MoleculeNode molecule, Structure structure)
        {
            _owner = owner;
            _molecule = molecule;
            Part = part;
            _structure = structure;
            _atoms = new ContentPart?[molecule.Atoms.Count];

            foreach (var piece in part.SelfAndDescendants())
                switch (piece.Node)
                {
                    case AtomNode atom: _atoms[atom.Index] = piece; break;
                    case BondNode bond: _written[bond.Bond] = piece; break;
                    case RingBondNode { Bond: { } closed }: _written[closed] = piece; break;
                }

            var positions = _structure.At;
            var (minX, maxX, minY, maxY) = (positions.Min(p => p.X), positions.Max(p => p.X), positions.Min(p => p.Y), positions.Max(p => p.Y));

            _spread = Spread(molecule, positions);

            // Shrunk to the room it is given, as far as a structure stays legible.
            var natural = (maxX - minX) * BondLength * _spread + 2 * (Margin + LabelSize);
            _scale = double.IsInfinity(owner.Room) ? 1 : Math.Clamp(owner.Room / natural, SmallestScale, 1);

            var length = Length;

            _at = [.. positions.Select(p => new LayoutPoint((p.X - minX) * length, (maxY - p.Y) * length))];
            _labels = [.. molecule.Atoms.Select(atom => Label.For(owner, molecule, atom, _at, _scale))];

            // Everything as far as it reaches, labels included, then moved clear of the edge.
            var reach = new LayoutRect(_at[0], _at[0]);
            for (var i = 0; i < _at.Length; i++)
            {
                reach = LayoutRect.Union(reach, new LayoutRect(_at[i], _at[i]));
                if (_labels[i] is { } label) reach = LayoutRect.Union(reach, label.Bounds);
            }

            var shift = new LayoutVector(Margin - reach.X, Margin - reach.Y);
            for (var i = 0; i < _at.Length; i++)
            {
                _at[i] += shift;
                _labels[i]?.Move(shift);
            }

            Size = (reach.Width + 2 * Margin, reach.Height + 2 * Margin);
            _gaps = Gaps();
        }

        public ContentPart Part { get; }

        public (double Width, double Height) Size { get; }

        private double Length => BondLength * _scale * _spread;

        private double Stroke => Math.Max(1, BondLength * StrokeShare * _scale);

        public void Lay(PortableBuilder build)
        {
            // Bonds under atoms, so a label sits on a clean ground and a corner is a corner.
            foreach (var bond in _molecule.Bonds)
            {
                build.Open(MoleculePiece.Bond, PartOf(bond));
                DrawBond(build, bond);
                build.Close();
            }

            for (var i = 0; i < _at.Length; i++)
            {
                build.Open(MoleculePiece.Atom, _atoms[i]);

                if (_labels[i] is { } label)
                {
                    label.Draw(build);
                }
                else
                {
                    // An unlabelled atom still needs a point to aim at; the dot fills the notch square line ends leave.
                    var half = Stroke / 2;
                    build.Draw(new PortableGeometryMark(_owner.Medium.Ellipse(_at[i], half, half), _owner.Ink(0), null));
                    build.Covers(new LayoutRect(_at[i].X - Length * 0.18, _at[i].Y - Length * 0.18, Length * 0.36, Length * 0.36));
                }

                build.Close();
            }
        }

        /// <summary>Where crossing bonds of a solid pass behind one another — the far bond is broken, the usual convention for drawing a bridged cage (e.g. adamantane).</summary>
        private Dictionary<int, List<LayoutPoint>> Gaps()
        {
            var gaps = new Dictionary<int, List<LayoutPoint>>();
            var depth = _structure.Depth;
            var solid = _molecule.Bonds.Where(bond => depth[bond.From] is not null && depth[bond.To] is not null).ToList();

            for (var x = 0; x < solid.Count; x++)
                for (var y = x + 1; y < solid.Count; y++)
                {
                    var (one, other) = (solid[x], solid[y]);
                    if (one.From == other.From || one.From == other.To || one.To == other.From || one.To == other.To) continue;

                    var p = _at[one.From];
                    var r = _at[one.To] - p;
                    var q = _at[other.From];
                    var s = _at[other.To] - q;

                    var denominator = r.X * s.Y - r.Y * s.X;
                    if (Math.Abs(denominator) < 1e-9) continue;

                    var t = ((q.X - p.X) * s.Y - (q.Y - p.Y) * s.X) / denominator;
                    var u = ((q.X - p.X) * r.Y - (q.Y - p.Y) * r.X) / denominator;
                    if (t is <= 0 or >= 1 || u is <= 0 or >= 1) continue;

                    var nearOne = depth[one.From]!.Value + (depth[one.To]!.Value - depth[one.From]!.Value) * t;
                    var nearOther = depth[other.From]!.Value + (depth[other.To]!.Value - depth[other.From]!.Value) * u;

                    var behind = nearOne < nearOther ? one : other;
                    if (!gaps.TryGetValue(behind.Index, out var points)) gaps[behind.Index] = points = [];
                    points.Add(p + r * t);
                }

            return gaps;
        }

        /// <summary>Fractions of [a,b] that are drawn: the whole line, minus a break where it passes behind another bond.</summary>
        private IEnumerable<(double From, double To)> Drawn(MoleculeBond bond, LayoutPoint a, LayoutPoint b)
        {
            var along = b - a;
            var squared = along.X * along.X + along.Y * along.Y;

            if (!_gaps.TryGetValue(bond.Index, out var points) || squared < 1e-9)
            {
                yield return (0, 1);
                yield break;
            }

            var half = (Stroke * 1.8 + 1.5) / Math.Sqrt(squared);
            var breaks = points
                .Select(point => ((point - a).X * along.X + (point - a).Y * along.Y) / squared)
                .Select(centre => (From: centre - half, To: centre + half))
                .OrderBy(gap => gap.From)
                .ToList();

            var at = 0.0;
            foreach (var (from, to) in breaks)
            {
                if (from > at) yield return (at, Math.Min(from, 1));
                at = Math.Max(at, to);
                if (at >= 1) yield break;
            }

            if (at < 1) yield return (at, 1);
        }

        /// <summary>How much to lengthen bonds so labels don't collide — needed when unbonded atoms land close together, as in a cage drawn in perspective.</summary>
        private static double Spread(MoleculeNode molecule, IReadOnlyList<Vec> at)
        {
            static bool Labelled(AtomNode atom) => atom.Number != 6 || atom.Charge != 0;

            var spread = 1.0;
            for (var i = 0; i < at.Count; i++)
                for (var j = i + 1; j < at.Count; j++)
                {
                    var (one, other) = (molecule.Atoms[i], molecule.Atoms[j]);
                    if (!Labelled(one) && !Labelled(other) || molecule.Between(i, j) is not null) continue;

                    var room = Labelled(one) && Labelled(other) ? 0.85 : 0.6;
                    var apart = Vec.Distance(at[i], at[j]);
                    if (apart > 1e-6) spread = Math.Max(spread, room / apart);
                }

            return Math.Min(spread, LongestSpread);
        }

        private ContentPart? PartOf(MoleculeBond bond) => _written.GetValueOrDefault(bond.Index);

        private void DrawBond(PortableBuilder build, MoleculeBond bond)
        {
            var (from, to) = Ends(bond.From, bond.To);
            var order = _molecule.Drawn(bond);
            var (wedge, narrow) = _structure.Wedges[bond.Index];

            if (wedge != Wedge.None)
            {
                var (start, end) = narrow == bond.From ? (from, to) : (to, from);
                var far = narrow == bond.From ? bond.To : bond.From;
                DrawWedge(build, start, end, wedge, _owner.Ink(_molecule.Atoms[narrow].Number), _owner.Ink(_molecule.Atoms[far].Number));
                return;
            }

            var gap = GapShare * Length;
            var across = (to - from).Length > 1e-6 ? new LayoutVector(-(to - from).Y, (to - from).X) / (to - from).Length : new LayoutVector(0, 1);

            switch (order)
            {
                case BondOrder.Single:
                    Line(build, bond, from, to);
                    break;

                case BondOrder.Double:
                    DrawDouble(build, bond, from, to, across, gap, dashed: false);
                    break;

                case BondOrder.Aromatic:
                    DrawDouble(build, bond, from, to, across, gap, dashed: true);
                    break;

                default:
                    Line(build, bond, from, to);
                    Line(build, bond, from + across * gap, to + across * gap);
                    Line(build, bond, from - across * gap, to - across * gap);
                    break;
            }
        }

        /// <summary>Second line goes inside the smaller ring, or toward the side with more neighbours; straddles the axis when one end is a lone atom (e.g. a carbonyl).</summary>
        private void DrawDouble(PortableBuilder build, MoleculeBond bond, LayoutPoint from, LayoutPoint to, LayoutVector across, double gap, bool dashed)
        {
            var side = Inside(bond);

            if (side == 0)
            {
                Line(build, bond, from + across * gap / 2, to + across * gap / 2);
                Line(build, bond, from - across * gap / 2, to - across * gap / 2, dashed);
                return;
            }

            Line(build, bond, from, to);

            var along = to - from;
            var inset = along.Length > 1e-6 ? along / along.Length * InsetShare * Length : default;
            var a = from + across * gap * side + (_labels[bond.From] is null ? inset : default);
            var b = to + across * gap * side - (_labels[bond.To] is null ? inset : default);
            Line(build, bond, a, b, dashed);
        }

        /// <summary>Which side the double bond's second line goes on: +1/-1, or 0 to straddle the axis.</summary>
        private int Inside(MoleculeBond bond)
        {
            var p = _at[bond.From];
            var q = _at[bond.To];
            var axis = q - p;

            int SideOf(LayoutPoint point) => Math.Sign(axis.X * (point.Y - p.Y) - axis.Y * (point.X - p.X));

            var ring = _structure.Rings
                .Where(r => Adjacent(r, bond.From, bond.To))
                .OrderBy(r => r.Length)
                .FirstOrDefault();

            if (ring is not null)
            {
                var centre = new LayoutPoint(ring.Average(atom => _at[atom].X), ring.Average(atom => _at[atom].Y));
                return SideOf(centre);
            }

            var fromDegree = _molecule.BondsAt(bond.From).Count;
            var toDegree = _molecule.BondsAt(bond.To).Count;
            if (fromDegree == 1 || toDegree == 1) return 0;

            var balance = 0;
            foreach (var end in new[] { bond.From, bond.To })
                foreach (var other in _molecule.BondsAt(end).Select(b => _molecule.Bonds[b].Other(end)))
                    if (other != bond.From && other != bond.To) balance += SideOf(_at[other]);

            return balance == 0 ? 1 : Math.Sign(balance);
        }

        private static bool Adjacent(int[] ring, int a, int b)
        {
            var i = Array.IndexOf(ring, a);
            return i >= 0 && (ring[(i + 1) % ring.Length] == b || ring[(i + ring.Length - 1) % ring.Length] == b);
        }

        /// <summary>Wedge from a stereocentre: solid toward the reader, hashed away.</summary>
        private void DrawWedge(PortableBuilder build, LayoutPoint narrow, LayoutPoint wide, Wedge kind, LayoutInk? near, LayoutInk? far)
        {
            var along = wide - narrow;
            if (along.Length < 1e-6) return;

            var across = new LayoutVector(-along.Y, along.X) / along.Length * Length * 0.12;

            if (kind == Wedge.Solid)
            {
                var shape = _owner.Medium.Outline();
                shape.Begin(narrow, filled: true, closed: true, stroked: false);
                shape.LineTo(wide + across);
                shape.LineTo(wide - across);

                build.Draw(new PortableGeometryMark(shape.Close(), near ?? far, null));
                return;
            }

            const int hashes = 7;
            var thickness = Math.Max(1, Stroke * 0.8);
            for (var k = 0; k <= hashes; k++)
            {
                var t = (double)k / hashes;
                var centre = narrow + along * t;
                var half = across * Math.Max(0.12, t);
                Segment(build, centre - half, centre + half, t < 0.5 ? near : far, thickness);
            }
        }

        private void Line(PortableBuilder build, MoleculeBond bond, LayoutPoint a, LayoutPoint b, bool dashed = false)
        {
            var near = _owner.Ink(_molecule.Atoms[bond.From].Number);
            var far = _owner.Ink(_molecule.Atoms[bond.To].Number);

            if (dashed)
            {
                const int dashes = 5;
                for (var k = 0; k < dashes; k++)
                {
                    var s = a + (b - a) * ((double)k / dashes);
                    var e = a + (b - a) * ((k + 0.55) / dashes);
                    Segment(build, s, e, k < dashes / 2 ? near : far, Stroke);
                }

                return;
            }

            foreach (var (from, to) in Drawn(bond, a, b))
            {
                var start = a + (b - a) * from;
                var end = a + (b - a) * to;

                if (ReferenceEquals(near, far) || to <= 0.5 || from >= 0.5)
                {
                    Segment(build, start, end, from < 0.5 ? near : far, Stroke);
                    continue;
                }

                var middle = a + (b - a) / 2;
                Segment(build, start, middle, near, Stroke);
                Segment(build, middle, end, far, Stroke);
            }
        }

        private void Segment(PortableBuilder build, LayoutPoint a, LayoutPoint b, LayoutInk? ink, double thickness)
        {
            var along = b - a;
            if (along.Length < 1e-6) return;

            var half = new LayoutVector(-along.Y, along.X) / along.Length * thickness / 2;
            var shape = _owner.Medium.Outline();
            shape.Begin(a + half, filled: true, closed: true, stroked: false);
            shape.LineTo(b + half);
            shape.LineTo(b - half);
            shape.LineTo(a - half);

            build.Draw(new PortableGeometryMark(shape.Close(), ink, null));
        }

        /// <summary>Where a bond between two atoms starts and stops: at their centres, or at the edge of their labels.</summary>
        private (LayoutPoint From, LayoutPoint To) Ends(int a, int b)
        {
            var p = _at[a];
            var q = _at[b];
            if (_labels[a] is { } la) p = la.Clip(p, q);
            if (_labels[b] is { } lb) q = lb.Clip(q, p);
            return (p, q);
        }
    }

    // ── Atom labels ─────────────────────────────────────────────────────────

    /// <summary>An atom's label runs: symbol centred on the atom, hydrogens on the side clear of its bonds, charge after, mass number before.</summary>
    private sealed class Label
    {
        private readonly List<(IShapedText Text, LayoutPoint At)> _runs = [];
        private readonly LayoutInk? _ink;
        private LayoutRect _symbol;

        private Label(LayoutInk? ink) => _ink = ink;

        public LayoutRect Bounds { get; private set; }

        public static Label? For(PortableSmilesBuilder owner, MoleculeNode molecule, AtomNode atom, LayoutPoint[] at, double scale)
        {
            var degree = molecule.BondsAt(atom.Index).Count;
            var shown = atom.Number != 6 || atom.Charge != 0 || atom.Isotope is not null || degree == 0;
            if (!shown) return null;

            var ink = owner.Ink(atom.Number);
            var label = new Label(ink);
            var size = LabelSize * scale;
            var small = size * 0.72;
            var centre = at[atom.Index];

            var symbol = owner.Text(atom.Number == 0 ? "*" : atom.Symbol, size, LabelFont, ink);
            var symbolAt = new LayoutPoint(centre.X - symbol.Width / 2, centre.Y - symbol.Height / 2);
            label.Add(symbol, symbolAt);
            label._symbol = new LayoutRect(symbolAt.X, centre.Y - symbol.Height * 0.36, symbol.Width, symbol.Height * 0.72);

            // Which way the bonds leave, so the hydrogens can go the other way.
            var leaving = new LayoutVector();
            foreach (var bond in molecule.BondsAt(atom.Index))
            {
                var d = at[molecule.Bonds[bond].Other(atom.Index)] - centre;
                if (d.Length > 1e-6) leaving += d / d.Length;
            }

            var hydrogens = atom.TotalHydrogens;
            var right = symbolAt.X + symbol.Width;

            if (hydrogens > 0)
            {
                var h = owner.Text("H", size, LabelFont, ink);
                var count = hydrogens > 1 ? owner.Text(hydrogens.ToString(CultureInfo.InvariantCulture), small, LabelFont, ink) : null;
                var groupWidth = h.Width + (count?.Width ?? 0);
                var sub = size * 0.35;

                LayoutPoint hAt;
                if (degree > 1 && Math.Abs(leaving.X) < 0.3 && Math.Abs(leaving.Y) > 0.5)
                {
                    // Bonds leaving straight up/down (e.g. a ring's NH) put hydrogens on the far side, not left-right.
                    var y = leaving.Y > 0 ? symbolAt.Y - symbol.Height * 0.85 : symbolAt.Y + symbol.Height * 0.85;
                    hAt = new LayoutPoint(centre.X - groupWidth / 2, y);
                }
                else if (degree > 0 && leaving.X > 0)
                {
                    hAt = new LayoutPoint(symbolAt.X - groupWidth, symbolAt.Y);
                }
                else
                {
                    hAt = new LayoutPoint(right, symbolAt.Y);
                    right += groupWidth;
                }

                label.Add(h, hAt);
                if (count is not null) label.Add(count, new LayoutPoint(hAt.X + h.Width, hAt.Y + sub));
            }

            if (atom.Charge != 0)
            {
                var magnitude = Math.Abs(atom.Charge);
                var sign = atom.Charge > 0 ? "+" : "−";
                var charge = owner.Text(magnitude > 1 ? magnitude + sign : sign, small, LabelFont, ink);
                label.Add(charge, new LayoutPoint(right, symbolAt.Y - size * 0.3));
            }

            if (atom.Isotope is { } isotope)
            {
                var mass = owner.Text(isotope.ToString(CultureInfo.InvariantCulture), small, LabelFont, ink);
                label.Add(mass, new LayoutPoint(symbolAt.X - mass.Width, symbolAt.Y - size * 0.3));
            }

            return label;
        }

        private void Add(IShapedText text, LayoutPoint at)
        {
            _runs.Add((text, at));
            var box = new LayoutRect(at.X, at.Y, text.Width, text.Height);
            Bounds = _runs.Count == 1 ? box : LayoutRect.Union(Bounds, box);
        }

        public void Move(LayoutVector by)
        {
            for (var i = 0; i < _runs.Count; i++) _runs[i] = (_runs[i].Text, _runs[i].At + by);
            _symbol = LayoutRect.Offset(_symbol, by);
            Bounds = LayoutRect.Offset(Bounds, by);
        }

        public void Draw(PortableBuilder build)
        {
            foreach (var (text, at) in _runs) build.Draw(new PortableTextMark(text, at, _ink));
        }

        /// <summary>Where a line from inside to outside leaves the symbol, with a little air so a bond stops short of the letter.</summary>
        public LayoutPoint Clip(LayoutPoint inside, LayoutPoint outside)
        {
            var box = LayoutRect.Inflate(_symbol, 1.5, 1.5);

            var d = outside - inside;
            var t = 1.0;
            if (Math.Abs(d.X) > 1e-9) t = Math.Min(t, ((d.X > 0 ? box.Right : box.Left) - inside.X) / d.X);
            if (Math.Abs(d.Y) > 1e-9) t = Math.Min(t, ((d.Y > 0 ? box.Bottom : box.Top) - inside.Y) / d.Y);

            return inside + d * Math.Clamp(t, 0, 1);
        }
    }

    // ── Inks ────────────────────────────────────────────────────────────────

    /// <summary>An element's colour, or null for carbon and anything uncoloured — the theme's own ink.</summary>
    private LayoutInk? Ink(int number) => Style.Elements.TryGetValue(Elements.Symbol(number), out var ink) ? ink : null;

    private IShapedText Text(string text, double size, string font, LayoutInk? ink) => Medium.Shape(text, Style.Face(font), size, ink ?? Style.Text);

    /// <summary>How a block sets the source it could not lay out at all: as the lines it was written as.</summary>
    protected override PortableCharacters Characters => new(Style.Face(SourceFont), SourceSize, Style.Text);
}
