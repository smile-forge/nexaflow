using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Chemistry;
using Nexaflow.Markdown.Chemistry.Depiction;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Chemistry;

/// <summary>
/// Lays a <c>smiles</c> block out: each molecule as a skeletal structure with its caption beneath, flowing left to
/// right and wrapping to the room given. <see cref="StructureLayout"/> decides atom positions; this decides how a
/// chemist draws them (bare-corner carbon, half-and-half bond colouring, wedges for stereocentres). Every atom and
/// bond is a piece carrying the source it was drawn from, so selection yields SMILES text and errors underline the
/// offending atom. A molecule with trouble still draws as much of itself as reads, with the reason in red beneath
/// its caption; unparseable source falls back to its own characters, struck through.
/// </summary>
internal sealed class SmilesBuilder : ContentBuilder
{
    private static readonly FontFamily LabelFont = new("Segoe UI, Arial, sans-serif");
    private static readonly FontFamily SourceFont = new("Cascadia Code, Consolas, monospace");

    private const double BondLength = 28;

    private const double LabelSize = 14;
    private const double CaptionSize = 12.5;
    private const double ReasonSize = 12;

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

    /// <summary>The narrowest a reason is set to, so a small structure does not stack it a word to a line.</summary>
    private const double ReasonRoom = 260;
    internal SmilesBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly)
        : base(reading, state, style, isReadOnly) { }

    /// <summary>Lays a block's source out to fit <paramref name="room"/>. Never null, and never throws.</summary>
    internal static Laid Lay(string source, StyleFormat style, double room = double.PositiveInfinity, int at = 0) =>
        new SmilesBuilder(ContentReading.Of(SmilesPipeline.Read(source), at), EditState.For(source), style, isReadOnly: true)
            .Lay(room);

    protected override Laid? Build()
    {
        var entries = Reading.Root.SelfAndDescendants()
            .Where(part => part.Kind == SmilesKinds.Entry || (part.Kind == Kinds.Verbatim && part.Parent?.Kind == SmilesKinds.Line))
            .Select(Sketch)
            .ToList();

        if (entries.Count == 0) return null;

        // One piece holds the block, because a layout has one root and every entry is a piece inside it.
        var build = new LayoutBuilder();
        build.Open(MoleculePiece.Block, Reading.Root);
        var size = Flow(build, entries);
        build.Close();
        var trouble = Reading.Root.SelfAndDescendants()
            .Where(part => part.Trouble is not null && !part.Derived)
            .Select(part => new Diagnostic(part.Start, Math.Max(part.Length, 1), DiagnosticSeverity.Error, part.Trouble!))
            .ToList();

        return new Laid(build.Seal(), size, trouble);
    }

    // ── Entries ─────────────────────────────────────────────────────────────

    /// <summary>One entry, measured but not yet placed: what it draws, and how much room that takes.</summary>
    private sealed class Sketched
    {
        public required ContentPart Part { get; init; }
        public Drawing? Structure { get; init; }
        public ContentPart? StandIn { get; init; }
        public FormattedText? Caption { get; init; }
        public ContentPart? CaptionPart { get; init; }
        public FormattedText? Reason { get; init; }

        public Size Body => Structure?.Size ?? (StandIn is null ? new Size(0, 0) : new Size(StandInText!.Width, StandInText.Height));
        public FormattedText? StandInText { get; init; }

        public double Width => Math.Max(Body.Width, Math.Max(Caption?.Width ?? 0, Reason?.Width ?? 0));
    }

    private Sketched Sketch(ContentPart entry)
    {
        var molecule = entry.Children.FirstOrDefault(child => child.Kind == SmilesKinds.Molecule);
        var label = entry.Children.FirstOrDefault(child => child.Kind == SmilesKinds.Label)?.Part(SmilesRoles.Label);

        var troubles = entry.SelfAndDescendants()
            .Select(part => part.Trouble)
            .OfType<string>()
            .Distinct()
            .ToList();

        var reasonRoom = ReasonRoom;
        Drawing? drawing = null;
        FormattedText? standIn = null;

        if (molecule is not null)
        {
            var read = Nexaflow.Markdown.Chemistry.Molecule.Read(molecule.Node);
            if (read.Atoms.Count > 0)
            {
                drawing = new Drawing(this, molecule, read);
                reasonRoom = Math.Max(ReasonRoom, drawing.Size.Width);
            }
        }

        if (drawing is null)
            standIn = Text((molecule ?? entry).Print(), SourceSize, SourceFont, Style.TextMuted);

        return new Sketched
        {
            Part = entry,
            Structure = drawing,
            StandIn = drawing is null ? molecule ?? entry : null,
            StandInText = standIn,
            Caption = label is null ? null : Text(label.Text, CaptionSize, LabelFont, Style.TextMuted),
            CaptionPart = label,
            Reason = troubles.Count == 0 ? null : Reason(string.Join(" ", troubles), reasonRoom),
        };
    }

    private const double SourceSize = 13;

    /// <summary>Rows entries left to right, wrapping when the next won't fit; captions share the line beneath the tallest.</summary>
    private Size Flow(LayoutBuilder build, List<Sketched> entries)
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
                build.Open(MoleculePiece.Entry, entry.Part, new Point(left, top));

                var bodyTop = (body - entry.Body.Height) / 2;
                var bodyLeft = (entry.Width - entry.Body.Width) / 2;

                if (entry.Structure is { } structure)
                {
                    build.Open(MoleculePiece.Molecule, structure.Part, new Point(bodyLeft, bodyTop));
                    structure.Lay(build);
                    build.Close();
                }
                else if (entry.StandInText is { } text)
                {
                    build.Open(MoleculePiece.StandIn, entry.StandIn, new Point(bodyLeft, bodyTop));
                    build.Draw(new TextMark(text, default, Style.TextMuted));
                    build.Draw(new RuleMark(new Rect(0, text.Height / 2 - 0.75, text.Width, 1.5), Style.Danger));
                    build.Close();
                }

                var below = body + (entry.Caption is null ? 0 : 6);

                if (entry.Caption is { } caption)
                {
                    build.Open(MoleculePiece.Caption, entry.CaptionPart, new Point((entry.Width - caption.Width) / 2, below));
                    build.Draw(new TextMark(caption, default, Style.TextMuted));
                    build.Close();
                    below += caption.Height;
                }

                if (entry.Reason is { } reason)
                {
                    below += 4;
                    build.Open(MoleculePiece.Trouble, part: null, new Point(0, below));
                    build.Draw(new TextMark(reason, default, Style.Danger));
                    build.Close();
                    below += reason.Height;
                }

                build.Close();

                bottom = Math.Max(bottom, below);
                left += entry.Width + Across;
            }

            widest = Math.Max(widest, left - Across);
            top += bottom + Down;
        }

        return new Size(widest, Math.Max(0, top - Down));
    }

    // ── One structure ───────────────────────────────────────────────────────

    /// <summary>A molecule worked out and measured — atom positions, labels, bond endpoints — ready to lay down.</summary>
    private sealed class Drawing
    {
        private readonly SmilesBuilder _owner;
        private readonly Nexaflow.Markdown.Chemistry.Molecule _molecule;
        private readonly Structure _structure;
        private readonly IReadOnlyList<ContentPart> _atoms;
        private readonly Point[] _at;
        private readonly Label?[] _labels;
        private readonly double _scale;

        /// <summary>How much longer than usual this structure's bonds are drawn, so its symbols clear one another.</summary>
        private readonly double _spread;

        /// <summary>The most a structure's bonds are lengthened to keep its symbols apart.</summary>
        private const double LongestSpread = 1.5;

        /// <summary>For each bond of a solid that passes behind another, where on the page it does.</summary>
        private readonly Dictionary<int, List<Point>> _gaps;

        public Drawing(SmilesBuilder owner, ContentPart part, Nexaflow.Markdown.Chemistry.Molecule molecule)
        {
            _owner = owner;
            _molecule = molecule;
            Part = part;
            _structure = StructureLayout.Of(molecule);
            _atoms = [.. part.SelfAndDescendants().Where(p => p.Kind == SmilesKinds.Atom && !p.Derived)];

            var positions = _structure.At;
            var (minX, maxX, minY, maxY) = (positions.Min(p => p.X), positions.Max(p => p.X), positions.Min(p => p.Y), positions.Max(p => p.Y));

            _spread = Spread(molecule, positions);

            // Shrunk to the room it is given, as far as a structure stays legible.
            var natural = (maxX - minX) * BondLength * _spread + 2 * (Margin + LabelSize);
            _scale = double.IsInfinity(owner.Room) ? 1 : Math.Clamp(owner.Room / natural, SmallestScale, 1);

            var length = Length;

            _at = [.. positions.Select(p => new Point((p.X - minX) * length, (maxY - p.Y) * length))];
            _labels = [.. molecule.Atoms.Select(atom => Label.For(owner, molecule, atom, _at, _scale))];

            // Everything as far as it reaches, labels included, then moved clear of the edge.
            var reach = new Rect(_at[0], _at[0]);
            for (var i = 0; i < _at.Length; i++)
            {
                reach.Union(_at[i]);
                if (_labels[i] is { } label) reach.Union(label.Bounds);
            }

            var shift = new Vector(Margin - reach.X, Margin - reach.Y);
            for (var i = 0; i < _at.Length; i++)
            {
                _at[i] += shift;
                _labels[i]?.Move(shift);
            }

            Size = new Size(reach.Width + 2 * Margin, reach.Height + 2 * Margin);
            _gaps = Gaps();
        }

        public ContentPart Part { get; }

        public Size Size { get; }

        private double Length => BondLength * _scale * _spread;

        private double Stroke => Math.Max(1, BondLength * StrokeShare * _scale);

        public void Lay(LayoutBuilder build)
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
                build.Open(MoleculePiece.Atom, i < _atoms.Count ? _atoms[i] : null);

                if (_labels[i] is { } label)
                {
                    label.Draw(build);
                }
                else
                {
                    // An unlabelled atom still needs a point to aim at; the dot fills the notch square line ends leave.
                    var half = Stroke / 2;
                    build.Draw(GeometryMark.Filled(Frozen(new EllipseGeometry(_at[i], half, half)), _owner.Ink(0)));
                    build.Covers(new Rect(_at[i].X - Length * 0.18, _at[i].Y - Length * 0.18, Length * 0.36, Length * 0.36));
                }

                build.Close();
            }
        }

        /// <summary>Where crossing bonds of a solid pass behind one another — the far bond is broken, the usual convention for drawing a bridged cage (e.g. adamantane).</summary>
        private Dictionary<int, List<Point>> Gaps()
        {
            var gaps = new Dictionary<int, List<Point>>();
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
        private IEnumerable<(double From, double To)> Drawn(MoleculeBond bond, Point a, Point b)
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
        private static double Spread(Nexaflow.Markdown.Chemistry.Molecule molecule, IReadOnlyList<Vec> at)
        {
            static bool Labelled(MoleculeAtom atom) => atom.Number != 6 || atom.Charge != 0;

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

        private ContentPart? PartOf(MoleculeBond bond)
        {
            if (bond.Node is null) return null;
            return Part.SelfAndDescendants().FirstOrDefault(p => ReferenceEquals(p.Node, bond.Node));
        }

        private void DrawBond(LayoutBuilder build, MoleculeBond bond)
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
            var across = (to - from).Length > 1e-6 ? new Vector(-(to - from).Y, (to - from).X) / (to - from).Length : new Vector(0, 1);

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
        private void DrawDouble(LayoutBuilder build, MoleculeBond bond, Point from, Point to, Vector across, double gap, bool dashed)
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
            var inset = along.Length > 1e-6 ? along / along.Length * InsetShare * Length : new Vector();
            var a = from + across * gap * side + (_labels[bond.From] is null ? inset : new Vector());
            var b = to + across * gap * side - (_labels[bond.To] is null ? inset : new Vector());
            Line(build, bond, a, b, dashed);
        }

        /// <summary>Which side the double bond's second line goes on: +1/-1, or 0 to straddle the axis.</summary>
        private int Inside(MoleculeBond bond)
        {
            var p = _at[bond.From];
            var q = _at[bond.To];
            var axis = q - p;

            int SideOf(Point point) => Math.Sign(axis.X * (point.Y - p.Y) - axis.Y * (point.X - p.X));

            var ring = _structure.Rings
                .Where(r => Adjacent(r, bond.From, bond.To))
                .OrderBy(r => r.Length)
                .FirstOrDefault();

            if (ring is not null)
            {
                var centre = new Point(ring.Average(atom => _at[atom].X), ring.Average(atom => _at[atom].Y));
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
        private void DrawWedge(LayoutBuilder build, Point narrow, Point wide, Wedge kind, Brush? near, Brush? far)
        {
            var along = wide - narrow;
            if (along.Length < 1e-6) return;

            var across = new Vector(-along.Y, along.X) / along.Length * Length * 0.12;

            if (kind == Wedge.Solid)
            {
                var shape = new StreamGeometry();
                using (var ctx = shape.Open())
                {
                    ctx.BeginFigure(narrow, isFilled: true, isClosed: true);
                    ctx.LineTo(wide + across, isStroked: false, isSmoothJoin: false);
                    ctx.LineTo(wide - across, isStroked: false, isSmoothJoin: false);
                }

                build.Draw(GeometryMark.Filled(Frozen(shape), near ?? far));
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

        private void Line(LayoutBuilder build, MoleculeBond bond, Point a, Point b, bool dashed = false)
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

        private static void Segment(LayoutBuilder build, Point a, Point b, Brush? ink, double thickness)
        {
            var along = b - a;
            if (along.Length < 1e-6) return;

            var half = new Vector(-along.Y, along.X) / along.Length * thickness / 2;
            var shape = new StreamGeometry();
            using (var ctx = shape.Open())
            {
                ctx.BeginFigure(a + half, isFilled: true, isClosed: true);
                ctx.LineTo(b + half, isStroked: false, isSmoothJoin: false);
                ctx.LineTo(b - half, isStroked: false, isSmoothJoin: false);
                ctx.LineTo(a - half, isStroked: false, isSmoothJoin: false);
            }

            build.Draw(GeometryMark.Filled(Frozen(shape), ink));
        }

        /// <summary>Where a bond between two atoms starts and stops: at their centres, or at the edge of their labels.</summary>
        private (Point From, Point To) Ends(int a, int b)
        {
            var p = _at[a];
            var q = _at[b];
            if (_labels[a] is { } la) p = la.Clip(p, q);
            if (_labels[b] is { } lb) q = lb.Clip(q, p);
            return (p, q);
        }

        private static Geometry Frozen(Geometry geometry)
        {
            geometry.Freeze();
            return geometry;
        }
    }

    // ── Atom labels ─────────────────────────────────────────────────────────

    /// <summary>An atom's label runs: symbol centred on the atom, hydrogens on the side clear of its bonds, charge after, mass number before.</summary>
    private sealed class Label
    {
        private readonly List<(FormattedText Text, Point At)> _runs = [];
        private readonly Brush? _ink;
        private Rect _symbol;

        private Label(Brush? ink) => _ink = ink;

        public Rect Bounds { get; private set; }

        public static Label? For(SmilesBuilder owner, Nexaflow.Markdown.Chemistry.Molecule molecule, MoleculeAtom atom, Point[] at, double scale)
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
            var symbolAt = new Point(centre.X - symbol.Width / 2, centre.Y - symbol.Height / 2);
            label.Add(symbol, symbolAt);
            label._symbol = new Rect(symbolAt.X, centre.Y - symbol.Height * 0.36, symbol.Width, symbol.Height * 0.72);

            // Which way the bonds leave, so the hydrogens can go the other way.
            var leaving = new Vector();
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

                Point hAt;
                if (degree > 1 && Math.Abs(leaving.X) < 0.3 && Math.Abs(leaving.Y) > 0.5)
                {
                    // Bonds leaving straight up/down (e.g. a ring's NH) put hydrogens on the far side, not left-right.
                    var y = leaving.Y > 0 ? symbolAt.Y - symbol.Height * 0.85 : symbolAt.Y + symbol.Height * 0.85;
                    hAt = new Point(centre.X - groupWidth / 2, y);
                }
                else if (degree > 0 && leaving.X > 0)
                {
                    hAt = new Point(symbolAt.X - groupWidth, symbolAt.Y);
                }
                else
                {
                    hAt = new Point(right, symbolAt.Y);
                    right += groupWidth;
                }

                label.Add(h, hAt);
                if (count is not null) label.Add(count, new Point(hAt.X + h.Width, hAt.Y + sub));
            }

            if (atom.Charge != 0)
            {
                var magnitude = Math.Abs(atom.Charge);
                var sign = atom.Charge > 0 ? "+" : "−";
                var charge = owner.Text(magnitude > 1 ? magnitude + sign : sign, small, LabelFont, ink);
                label.Add(charge, new Point(right, symbolAt.Y - size * 0.3));
            }

            if (atom.Isotope is { } isotope)
            {
                var mass = owner.Text(isotope.ToString(CultureInfo.InvariantCulture), small, LabelFont, ink);
                label.Add(mass, new Point(symbolAt.X - mass.Width, symbolAt.Y - size * 0.3));
            }

            return label;
        }

        private void Add(FormattedText text, Point at)
        {
            _runs.Add((text, at));
            var box = new Rect(at, new Size(text.Width, text.Height));
            Bounds = _runs.Count == 1 ? box : Rect.Union(Bounds, box);
        }

        public void Move(Vector by)
        {
            for (var i = 0; i < _runs.Count; i++) _runs[i] = (_runs[i].Text, _runs[i].At + by);
            _symbol.Offset(by);
            Bounds = Rect.Offset(Bounds, by);
        }

        public void Draw(LayoutBuilder build)
        {
            foreach (var (text, at) in _runs) build.Draw(new TextMark(text, at, _ink));
        }

        /// <summary>Where a line from inside to outside leaves the symbol, with a little air so a bond stops short of the letter.</summary>
        public Point Clip(Point inside, Point outside)
        {
            var box = _symbol;
            box.Inflate(1.5, 1.5);

            var d = outside - inside;
            var t = 1.0;
            if (Math.Abs(d.X) > 1e-9) t = Math.Min(t, ((d.X > 0 ? box.Right : box.Left) - inside.X) / d.X);
            if (Math.Abs(d.Y) > 1e-9) t = Math.Min(t, ((d.Y > 0 ? box.Bottom : box.Top) - inside.Y) / d.Y);

            return inside + d * Math.Clamp(t, 0, 1);
        }
    }

    // ── Painting ────────────────────────────────────────────────────────────

    /// <summary>An element's colour, or null for carbon and anything uncoloured — the theme's own ink.</summary>
    private Brush? Ink(int number) =>
        Style.Elements.TryGetValue(Elements.Symbol(number), out var brush) ? brush : null;

    private FormattedText Text(string text, double size, FontFamily font, Brush? ink) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Style.Face(font),
            size,
            ink ?? Style.Text,
            Editing.LayoutText.Density);

    private FormattedText Reason(string trouble, double room) =>
        new(trouble,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Style.Face(LabelFont),
            ReasonSize,
            Style.Danger,
            Editing.LayoutText.Density)
        {
            MaxTextWidth = room,
        };

    /// <summary>How a block sets the source it could not lay out at all: as the lines it was written as.</summary>
    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Style.Face(SourceFont),
            SourceSize,
            Style.Text,
            Editing.LayoutText.Density);
}
