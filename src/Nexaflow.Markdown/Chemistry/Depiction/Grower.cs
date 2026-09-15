namespace Nexaflow.Markdown.Chemistry.Depiction;

/// <summary>
/// Lays one connected molecule out: its largest ring system placed first, and every atom after it put down beside
/// an atom already placed, into the widest gap round it.
/// </summary>
internal sealed class Grower
{
    private const double Tau = 2 * Math.PI;

    private readonly Molecule _molecule;
    private readonly List<int> _component;
    private readonly Vec[] _at;
    private readonly bool[] _placed;

    /// <summary>For each atom, the atom it was put down beside, or -1.</summary>
    private readonly int[] _from;

    private readonly List<int[]> _systems = [];
    private readonly List<int>[] _systemsOf;
    private readonly bool[] _systemPlaced;
    private readonly Dictionary<int, Dictionary<int, Vec>> _shapes = [];
    private readonly Dictionary<int, List<int[]>> _systemRings = [];

    private readonly Queue<int> _queue = new();

    /// <summary>The ring systems drawn as solids, and how each was seen.</summary>
    private readonly Dictionary<int, CageLayout.Seen> _cages = [];

    /// <summary>How near the reader each atom of a solid is drawn — nothing for an atom on the flat page.</summary>
    private readonly double?[] _depth;

    /// <summary>For an atom of a solid, the way out of it on the page, which is the way its substituents leave by.</summary>
    private readonly Vec?[] _outward;

    /// <summary>For an atom of a solid with two substituents, the two ways they leave it on the page.</summary>
    private readonly Vec[]?[] _pairs;

    /// <summary>A system drawn as a solid has at most this many atoms; past it, a flat drawing is all a page can hold.</summary>
    private const int LargestCage = 60;

    public Grower(Molecule molecule, IReadOnlyList<int[]> rings, List<int> component, Vec[] at, double?[] depth)
    {
        _molecule = molecule;
        _component = component;
        _at = at;
        _depth = depth;
        _outward = new Vec?[molecule.Atoms.Count];
        _pairs = new Vec[]?[molecule.Atoms.Count];
        _placed = new bool[molecule.Atoms.Count];
        _from = new int[molecule.Atoms.Count];
        Array.Fill(_from, -1);

        _systemsOf = new List<int>[molecule.Atoms.Count];
        for (var i = 0; i < _systemsOf.Length; i++) _systemsOf[i] = [];

        var inComponent = new HashSet<int>(component);
        FindSystems([.. rings.Where(ring => inComponent.Contains(ring[0]))]);
        _systemPlaced = new bool[_systems.Count];
    }

    /// <summary>
    /// Rings fused or bridged into one shape: those sharing two atoms or more. Two rings sharing a single atom — a
    /// spiro centre — are two shapes that meet at a point, and are placed one after the other.
    /// </summary>
    private void FindSystems(List<int[]> rings)
    {
        var group = Enumerable.Range(0, rings.Count).ToArray();
        int Find(int x) => group[x] == x ? x : group[x] = Find(group[x]);

        for (var i = 0; i < rings.Count; i++)
            for (var j = i + 1; j < rings.Count; j++)
                if (rings[i].Intersect(rings[j]).Count() >= 2)
                    group[Find(i)] = Find(j);

        foreach (var members in Enumerable.Range(0, rings.Count).GroupBy(Find).OrderBy(g => g.Min()))
        {
            var index = _systems.Count;
            var ringsOf = members.Select(i => rings[i]).ToList();
            var atoms = ringsOf.SelectMany(ring => ring).Distinct().Order().ToArray();

            _systems.Add(atoms);
            _systemRings[index] = ringsOf;
            foreach (var atom in atoms) _systemsOf[atom].Add(index);
        }
    }

    public void Grow()
    {
        if (_systems.Count > 0)
        {
            var core = Enumerable.Range(0, _systems.Count)
                .OrderByDescending(s => _systems[s].Length)
                .ThenBy(s => s)
                .First();

            foreach (var (atom, point) in Shape(core))
            {
                Put(atom, point, from: -1);
                if (!_cages.TryGetValue(core, out var cage)) continue;
                _depth[atom] = cage.Depth[atom];
                _outward[atom] = cage.Outward[atom];
                _pairs[atom] = cage.Pairs.GetValueOrDefault(atom);
            }

            _systemPlaced[core] = true;
        }
        else
        {
            Put(FarEnd(), Vec.Zero, from: -1);
        }

        while (_queue.Count > 0) Extend(_queue.Dequeue());
    }

    private void Put(int atom, Vec point, int from)
    {
        _at[atom] = point;
        _placed[atom] = true;
        _from[atom] = from;
        _queue.Enqueue(atom);
    }

    /// <summary>An end of the longest chain, so a molecule with no rings is grown along its backbone.</summary>
    private int FarEnd()
    {
        var start = _component[0];
        var distance = new Dictionary<int, int> { [start] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        var far = start;

        while (queue.Count > 0)
        {
            var atom = queue.Dequeue();
            if (distance[atom] > distance[far]) far = atom;

            foreach (var neighbour in Neighbours(atom))
            {
                if (distance.ContainsKey(neighbour)) continue;
                distance[neighbour] = distance[atom] + 1;
                queue.Enqueue(neighbour);
            }
        }

        return far;
    }

    private IEnumerable<int> Neighbours(int atom) =>
        _molecule.BondsAt(atom).Select(bond => _molecule.Bonds[bond].Other(atom));

    // ── One atom's neighbours ───────────────────────────────────────────────

    private void Extend(int atom)
    {
        // A ring system meeting this one at this atom alone goes into the widest gap round it, before anything else
        // takes that gap.
        foreach (var system in _systemsOf[atom])
        {
            if (_systemPlaced[system]) continue;

            var gap = WidestGap(atom, out _);
            PlaceSystem(system, atom, _at[atom], -Vec.FromAngle(gap), from: _from[atom]);
        }

        var unplaced = Neighbours(atom).Where(n => !_placed[n]).Distinct().ToList();
        if (unplaced.Count == 0) return;

        // The heaviest first: it takes the straightest way on, so a long chain stays a zig-zag and its short
        // branches hang off it.
        var weight = unplaced.ToDictionary(n => n, n => Weight(atom, n));
        unplaced = [.. unplaced.OrderByDescending(n => weight[n]).ThenBy(n => n)];

        var slots = Slots(atom, unplaced.Count);
        var order = AsWritten(atom, unplaced, slots);

        for (var i = 0; i < order.Count; i++)
        {
            var neighbour = order[i];
            var direction = Vec.FromAngle(slots[i]);
            var point = _at[atom] + direction;

            var system = _systemsOf[neighbour].FirstOrDefault(s => !_systemPlaced[s], -1);
            if (system >= 0)
                PlaceSystem(system, neighbour, point, -direction, from: atom);
            else
                Put(neighbour, point, atom);
        }
    }

    /// <summary>How many unplaced atoms lie beyond <paramref name="neighbour"/>, seen from <paramref name="atom"/>.</summary>
    private int Weight(int atom, int neighbour)
    {
        var seen = new HashSet<int> { atom, neighbour };
        var stack = new Stack<int>();
        stack.Push(neighbour);

        while (stack.Count > 0)
            foreach (var next in Neighbours(stack.Pop()))
                if (!_placed[next] && seen.Add(next)) stack.Push(next);

        return seen.Count - 1;
    }

    /// <summary>
    /// The directions round <paramref name="atom"/> its unplaced neighbours may go in, best first — at least
    /// <paramref name="count"/> of them, and two where there is a choice of side to be made.
    /// </summary>
    private List<double> Slots(int atom, int count)
    {
        var placed = Neighbours(atom).Where(n => _placed[n]).Distinct().ToList();

        if (placed.Count == 0)
        {
            if (count == 1) return [0];
            if (count == 2) return [0, Tau / 3];
            return [.. Enumerable.Range(0, count).Select(i => Tau * i / count)];
        }

        if (placed.Count == 1)
        {
            var before = placed[0];
            var back = (_at[before] - _at[atom]).Angle;

            if (count == 1 && IsLinear(atom)) return [back + Math.PI];

            if (count <= 2)
            {
                var one = back + Tau / 3;
                var other = back - Tau / 3;
                return OnTheFarSide(atom, before, one) ? [one, other] : [other, one];
            }

            return [.. Enumerable.Range(1, count)
                .Select(j => back + Tau * j / (count + 1))
                .OrderBy(angle => Math.Abs(Normalise(angle - back - Math.PI)))];
        }

        // An atom of a solid sends its substituents the way out of the solid, as the reader would see them leave it —
        // unless that way points at the reader or away, when the widest gap on the page is all there is to go by.
        if (count == 2 && _pairs[atom] is { } pair && pair.All(d => d.Length > 0.3))
            return [.. pair.Select(d => d.Angle)];

        if (_outward[atom] is { Length: > 0.35 } outward)
        {
            const double fan = 50 * Math.PI / 180;
            return [.. Enumerable.Range(0, count)
                .Select(j => outward.Angle + (j - (count - 1) / 2.0) * fan)
                .OrderBy(angle => Math.Abs(Normalise(angle - outward.Angle)))];
        }

        var start = WidestGap(atom, out var width) - width / 2;
        return [.. Enumerable.Range(1, count)
            .Select(j => start + width * j / (count + 1))
            .OrderBy(angle => Math.Abs(Normalise(angle - start - width / 2)))];
    }

    /// <summary>
    /// Whether a neighbour of <paramref name="atom"/> at <paramref name="angle"/> is on the far side of the bond
    /// from <paramref name="before"/> from whatever <paramref name="before"/> was itself put down beside — the choice
    /// that makes a chain a zig-zag rather than a curl.
    /// </summary>
    private bool OnTheFarSide(int atom, int before, double angle)
    {
        var behind = _from[before] >= 0 && _from[before] != atom
            ? _from[before]
            : Neighbours(before).FirstOrDefault(n => n != atom && _placed[n], -1);

        if (behind < 0) return true;

        var axis = _at[atom] - _at[before];
        var there = Math.Sign(Vec.Cross(axis, _at[behind] - _at[before]));
        var here = Math.Sign(Vec.Cross(axis, _at[atom] + Vec.FromAngle(angle) - _at[before]));
        return here != there;
    }

    /// <summary>A triple bond, or the middle of two double bonds, is straight through.</summary>
    private bool IsLinear(int atom)
    {
        var around = _molecule.BondsAt(atom);
        if (around.Count != 2) return false;

        var orders = around.Select(bond => _molecule.Bonds[bond].Order).ToList();
        return orders.Contains(BondOrder.Triple) || orders.All(order => order == BondOrder.Double);
    }

    /// <summary>
    /// Which unplaced neighbour takes which slot: heaviest first, except that a double bond whose ends were both
    /// written with <c>/</c> or <c>\</c> puts this end's substituent on the side SMILES asked for.
    /// </summary>
    private List<int> AsWritten(int atom, List<int> unplaced, List<double> slots)
    {
        var order = unplaced.ToList();

        var placed = Neighbours(atom).Where(n => _placed[n]).Distinct().ToList();
        if (placed.Count != 1 || slots.Count < 2) return order;

        var other = placed[0];
        if (_molecule.Between(atom, other)?.Order != BondOrder.Double) return order;

        var there = Neighbours(other).FirstOrDefault(n => n != atom && _placed[n] && _molecule.Between(other, n)?.Direction is not null, -1);
        var here = unplaced.FirstOrDefault(n => _molecule.Between(atom, n)?.Direction is not null, -1);
        if (there < 0 || here < 0) return order;

        var together = Outward(other, there) == Outward(atom, here);

        var axis = _at[atom] - _at[other];
        var side = Math.Sign(Vec.Cross(axis, _at[there] - _at[other]));

        for (var slot = 0; slot < Math.Min(2, slots.Count); slot++)
        {
            var at = Math.Sign(Vec.Cross(axis, _at[atom] + Vec.FromAngle(slots[slot]) - _at[other]));
            if ((at == side) != together) continue;

            if (slot < order.Count)
            {
                order.Remove(here);
                order.Insert(slot, here);
            }
            else
            {
                // A lone substituent takes the second choice of side by swapping the first two.
                (slots[0], slots[slot]) = (slots[slot], slots[0]);
            }

            break;
        }

        return order;
    }

    /// <summary>
    /// The direction mark on the bond from <paramref name="atom"/> to <paramref name="substituent"/>, read leaving
    /// the double bond — which is how it was written when the substituent was written after the atom, and the other
    /// way round when it was written before.
    /// </summary>
    private char Outward(int atom, int substituent)
    {
        var bond = _molecule.Between(atom, substituent)!;
        var mark = bond.Direction!.Value;
        return bond.From == atom ? mark : mark == '/' ? '\\' : '/';
    }

    /// <summary>The middle of the widest gap between an atom's placed neighbours, and how wide it is.</summary>
    private double WidestGap(int atom, out double width)
    {
        var angles = Neighbours(atom).Where(n => _placed[n]).Distinct()
            .Select(n => Normalise((_at[n] - _at[atom]).Angle, positive: true))
            .Order()
            .ToList();

        if (angles.Count == 0)
        {
            width = Tau;
            return 0;
        }

        if (angles.Count == 1)
        {
            width = Tau;
            return angles[0] + Math.PI;
        }

        var bestStart = 0.0;
        width = -1;
        for (var i = 0; i < angles.Count; i++)
        {
            var from = angles[i];
            var to = i + 1 < angles.Count ? angles[i + 1] : angles[0] + Tau;
            if (to - from <= width) continue;
            width = to - from;
            bestStart = from;
        }

        return bestStart + width / 2;
    }

    private static double Normalise(double angle, bool positive = false)
    {
        angle %= Tau;
        if (positive) return angle < 0 ? angle + Tau : angle;
        if (angle > Math.PI) angle -= Tau;
        if (angle < -Math.PI) angle += Tau;
        return angle;
    }

    // ── Ring systems ────────────────────────────────────────────────────────

    /// <summary>
    /// Puts a whole ring system down with <paramref name="anchor"/> at <paramref name="point"/> and the side of the
    /// system facing away from its rings pointing along <paramref name="outward"/> — mirrored or not, whichever
    /// crowds what is already on the page less. A solid mirrored on the page is the same solid seen from behind, so
    /// its depths turn over with it.
    /// </summary>
    private void PlaceSystem(int system, int anchor, Vec point, Vec outward, int from)
    {
        var shape = Shape(system);
        _cages.TryGetValue(system, out var cage);

        var exterior = cage is not null && cage.Outward[anchor].Length > 0.35
            ? cage.Outward[anchor].Unit
            : Exterior(system, shape, anchor);
        var origin = shape[anchor];
        var turn = outward.Angle - exterior.Angle;

        Dictionary<int, Vec> Placed(bool mirrored) =>
            shape.ToDictionary(p => p.Key, p =>
            {
                var local = mirrored ? p.Value.Reflected(origin, exterior) : p.Value;
                return point + (local - origin).Rotated(turn);
            });

        var plain = Placed(mirrored: false);
        var mirror = Placed(mirrored: true);
        var flipped = Crowding(mirror, anchor) < Crowding(plain, anchor) - 1e-9;
        var chosen = flipped ? mirror : plain;

        if (!_placed[anchor]) Put(anchor, chosen[anchor], from);

        foreach (var (atom, at) in chosen.OrderBy(p => p.Key))
        {
            if (atom != anchor && !_placed[atom]) Put(atom, at, anchor);
            if (cage is null) continue;

            var away = flipped ? cage.Outward[atom].Reflected(Vec.Zero, exterior) : cage.Outward[atom];
    _outward[atom] = away.Rotated(turn);
            _pairs[atom] = cage.Pairs.TryGetValue(atom, out var pair)
                ? [.. pair.Select(d => (flipped ? d.Reflected(Vec.Zero, exterior) : d).Rotated(turn))]
                : null;
            _depth[atom] = flipped ? -cage.Depth[atom] : cage.Depth[atom];
        }

        _systemPlaced[system] = true;
    }

    /// <summary>How crowded the page would be with these atoms put down: nearby atoms count, near ones a lot.</summary>
    private double Crowding(Dictionary<int, Vec> points, int anchor)
    {
        var sum = 0.0;
        foreach (var (atom, point) in points)
        {
            if (atom == anchor) continue;
            foreach (var other in _component)
            {
                if (!_placed[other] || other == anchor || points.ContainsKey(other)) continue;
                var d = Vec.Distance(point, _at[other]);
                if (d < 3) sum += 1 / (d * d + 0.05);
            }
        }

        return sum;
    }

    /// <summary>Which way is out of the system at <paramref name="atom"/>: away from its neighbours in the rings.</summary>
    private Vec Exterior(int system, Dictionary<int, Vec> shape, int atom)
    {
        var inward = Vec.Zero;
        foreach (var neighbour in Neighbours(atom))
            if (shape.ContainsKey(neighbour) && IsRingBond(system, atom, neighbour))
                inward += (shape[neighbour] - shape[atom]).Unit;

        if (inward.Length > 1e-6) return (-inward).Unit;

        var centre = shape.Values.Aggregate(Vec.Zero, (sum, p) => sum + p) / shape.Count;
        var away = shape[atom] - centre;
        return away.Length > 1e-6 ? away.Unit : new Vec(1, 0);
    }

    private bool IsRingBond(int system, int a, int b) =>
        _systemRings[system].Any(ring =>
        {
            var i = Array.IndexOf(ring, a);
            return i >= 0 && (ring[(i + 1) % ring.Length] == b || ring[(i + ring.Length - 1) % ring.Length] == b);
        });

    /// <summary>
    /// A ring system on its own, about the origin: its first ring a regular polygon, and each ring after it built
    /// onto the atoms already placed — the unplaced stretches of it swung as arcs between the placed atoms either
    /// side, on whichever side is emptier. For a ring fused along one bond that is exactly a regular polygon on that
    /// bond; for a bridge it is the best a flat page can do.
    /// </summary>
    private Dictionary<int, Vec> Shape(int system)
    {
        if (_shapes.TryGetValue(system, out var cached)) return cached;

        var rings = _systemRings[system];
        var shape = new Dictionary<int, Vec>();

        var neighbours = rings.Select(ring => rings.Count(other => other != ring && ring.Intersect(other).Count() >= 2)).ToList();
        var first = Enumerable.Range(0, rings.Count)
            .OrderByDescending(i => neighbours[i])
            .ThenBy(i => Math.Abs(rings[i].Length - 6))
            .ThenBy(i => i)
            .First();

        var ringOne = rings[first];
        var radius = 0.5 / Math.Sin(Math.PI / ringOne.Length);
        for (var i = 0; i < ringOne.Length; i++)
            shape[ringOne[i]] = Vec.FromAngle(Math.PI / 2 + Tau * i / ringOne.Length) * radius;

        while (true)
        {
            var next = -1;
            var bestPlaced = 1;
            var bestUnplaced = int.MaxValue;

            for (var r = 0; r < rings.Count; r++)
            {
                var placedCount = rings[r].Count(shape.ContainsKey);
                var unplacedCount = rings[r].Length - placedCount;
                if (unplacedCount == 0) continue;

                if (placedCount > bestPlaced || (placedCount == bestPlaced && unplacedCount < bestUnplaced && placedCount >= 2))
                {
                    next = r;
                    bestPlaced = placedCount;
                    bestUnplaced = unplacedCount;
                }
            }

            if (next < 0) break;
            Swing(system, rings[next], shape);
        }

        // A ring no placed ring shared two atoms with — possible only when the rings round it were placed in an
        // unlucky order — is placed as a polygon beside the rest rather than left out.
        foreach (var ring in rings)
        {
            if (ring.All(shape.ContainsKey)) continue;
            var right = shape.Count == 0 ? 0 : shape.Values.Max(p => p.X) + 1.5;
            var r = 0.5 / Math.Sin(Math.PI / ring.Length);
            for (var i = 0; i < ring.Length; i++)
                if (!shape.ContainsKey(ring[i]))
                    shape[ring[i]] = new Vec(right + r, 0) + Vec.FromAngle(Tau * i / ring.Length) * r;
        }

        // Polygons on polygons draw most bridges well enough — norbornane's one-carbon bridge stays bent — so a cage
        // is only settled where they folded it onto itself, and the settled shape is kept only if it is less tangled.
        if (Bridged(rings) && Tangle(system, shape) > 0)
        {
            var settled = new Dictionary<int, Vec>(shape);
            Relax(system, settled);
            if (Tangle(system, settled) <= Tangle(system, shape))
                foreach (var (atom, point) in settled) shape[atom] = point;
        }

        // A cage — rings sharing more than a bond, or an atom shared by three rings — is also drawn as the solid it
        // is, and whichever of the two drawings reads more easily is the one kept. A flat drawing that reads cleanly
        // scores nothing, so fused rings and a tidy bicycle stay flat. An aromatic ring is flat in the molecule and is
        // drawn flat by everyone, so a system holding one — morphine, strychnine — is never offered as a solid; and
        // nor is one with a large ring in it, since a cryptand's loops are drawn as loops.
        if (shape.Count <= LargestCage
            && !shape.Keys.Any(atom => _molecule.Atoms[atom].Aromatic)
            && rings.All(ring => ring.Length <= 8)
            && (Bridged(rings) || shape.Keys.Any(atom => rings.Count(ring => ring.Contains(atom)) >= 3)))
        {
            var atoms = shape.Keys.Order().ToList();
            var bonds = new List<(int A, int B)>();
            for (var i = 0; i < atoms.Count; i++)
                for (var j = i + 1; j < atoms.Count; j++)
                    if (IsRingBond(system, atoms[i], atoms[j])) bonds.Add((atoms[i], atoms[j]));

            var labelled = atoms.Select(atom => _molecule.Atoms[atom].Number != 6).ToList();
            var hanging = atoms
                .Select(atom => (IReadOnlyList<bool>)[.. Neighbours(atom).Where(other => !shape.ContainsKey(other))
                    .Select(other => _molecule.Atoms[other].Number != 6)])
                .ToList();
            var cage = CageLayout.Of(atoms, bonds, rings, labelled, hanging);
            // The flat drawing is judged as the solid is: with what hangs off it stood one bond out, here along the
            // way out of its rings on the page — a tropane's bridging nitrogen drawn inside its ring has nowhere to
            // put its methyl but across it.
            var scene = atoms.Select(atom => shape[atom]).ToList();
            var sceneBonds = bonds.Select(b => (atoms.IndexOf(b.A), atoms.IndexOf(b.B))).ToList();
            var sceneLabels = labelled.ToList();
            for (var i = 0; i < atoms.Count; i++)
            {
                var exterior = Exterior(system, shape, atoms[i]);
                for (var k = 0; k < hanging[i].Count && k < 2; k++)
                {
                    var fan = hanging[i].Count == 1 ? 0 : (k == 0 ? 35 : -35) * Math.PI / 180;
                    sceneBonds.Add((i, scene.Count));
                    scene.Add(shape[atoms[i]] + exterior.Rotated(fan));
                    sceneLabels.Add(hanging[i][k]);
                }
            }

            var flat = Readability.Score(scene, sceneBonds, sceneLabels);

            if (cage.Score < flat - 5)
            {
                shape = new Dictionary<int, Vec>(cage.At);
                _cages[system] = cage;
            }
        }

        _shapes[system] = shape;
        return shape;
    }

    /// <summary>Whether two rings of a system share more than a bond — a bridge, which no set of polygons draws flat.</summary>
    private static bool Bridged(List<int[]> rings)
    {
        for (var i = 0; i < rings.Count; i++)
            for (var j = i + 1; j < rings.Count; j++)
                if (rings[i].Intersect(rings[j]).Count() >= 3) return true;
        return false;
    }

    /// <summary>How tangled a system's shape is: atoms on top of one another count most, bonds crossing after.</summary>
    private int Tangle(int system, Dictionary<int, Vec> shape)
    {
        var atoms = shape.Keys.ToArray();
        var bonds = new List<(int A, int B)>();
        var score = 0;

        for (var i = 0; i < atoms.Length; i++)
            for (var j = i + 1; j < atoms.Length; j++)
            {
                if (IsRingBond(system, atoms[i], atoms[j])) bonds.Add((atoms[i], atoms[j]));
                else if (Vec.Distance(shape[atoms[i]], shape[atoms[j]]) < 0.6) score += 10;
            }

        for (var x = 0; x < bonds.Count; x++)
            for (var y = x + 1; y < bonds.Count; y++)
            {
                var (a, b) = bonds[x];
                var (c, d) = bonds[y];
                if (a != c && a != d && b != c && b != d && Vec.Crosses(shape[a], shape[b], shape[c], shape[d])) score++;
            }

        return score;
    }

    /// <summary>
    /// Settles a bridged system by stress majorization: every pair of its atoms is pulled towards the distance a
    /// zig-zag of as many bonds would put between them, nearer pairs pulled harder. Polygons built on polygons give a
    /// cage a starting shape; this is what evens its bonds out and opens its bridges up, the way a chemist draws
    /// adamantane rather than the way a compass would.
    /// </summary>
    private void Relax(int system, Dictionary<int, Vec> shape)
    {
        var atoms = shape.Keys.Order().ToArray();
        var index = atoms.Select((atom, i) => (atom, i)).ToDictionary(p => p.atom, p => p.i);
        var n = atoms.Length;

        var hops = new int[n, n];
        for (var s = 0; s < n; s++)
        {
            for (var t = 0; t < n; t++) hops[s, t] = -1;
            hops[s, s] = 0;
            var queue = new Queue<int>();
            queue.Enqueue(s);
            while (queue.Count > 0)
            {
                var v = queue.Dequeue();
                foreach (var neighbour in Neighbours(atoms[v]))
                {
                    if (!index.TryGetValue(neighbour, out var w) || hops[s, w] >= 0 || !IsRingBond(system, atoms[v], neighbour)) continue;
                    hops[s, w] = hops[s, v] + 1;
                    queue.Enqueue(w);
                }
            }
        }

        var p = atoms.Select(atom => shape[atom]).ToArray();
        Majorize(p, hops);

        for (var i = 0; i < n; i++) shape[atoms[i]] = p[i];
    }

    private static void Majorize(Vec[] p, int[,] hops)
    {
        var n = p.Length;

        for (var iteration = 0; iteration < 300; iteration++)
        {
            for (var i = 0; i < n; i++)
            {
                var sum = Vec.Zero;
                var weights = 0.0;

                for (var j = 0; j < n; j++)
                {
                    if (i == j || hops[i, j] <= 0) continue;

                    var ideal = Zigzag(hops[i, j]);
                    var weight = 1 / (ideal * ideal);
                    var apart = p[i] - p[j];
                    var length = apart.Length;
                    var towards = length > 1e-9 ? apart / length : Vec.FromAngle(i - j);

                    sum += (p[j] + towards * ideal) * weight;
                    weights += weight;
                }

                if (weights > 0) p[i] = sum / weights;
            }
        }
    }

    /// <summary>How far apart the ends of a zig-zag of <paramref name="bonds"/> bonds are.</summary>
    private static double Zigzag(int bonds)
    {
        var along = bonds * Math.Sqrt(3) / 2;
        return bonds % 2 == 0 ? along : Math.Sqrt(along * along + 0.25);
    }

    /// <summary>
    /// Each unplaced stretch of <paramref name="ring"/>, swung between the placed atoms either end of it — on whichever
    /// side, and bowed however far, leaves the system least tangled. A full arc keeps every bond one long; a flatter one
    /// shortens them, which is how the third bridge of bicyclo[2.2.2]octane fits inside the hexagon the other two make
    /// rather than crossing it.
    /// </summary>
    private void Swing(int system, int[] ring, Dictionary<int, Vec> shape)
    {
        var n = ring.Length;
        var start = Array.FindIndex(ring, shape.ContainsKey);

        for (var step = 0; step < n; step++)
        {
            var i = (start + step) % n;
            if (!shape.ContainsKey(ring[i])) continue;

            var run = new List<int>();
            var j = (i + 1) % n;
            while (!shape.ContainsKey(ring[j]))
            {
                run.Add(ring[j]);
                j = (j + 1) % n;
            }

            if (run.Count == 0) continue;

            var from = shape[ring[i]];
            var to = shape[ring[j]];

            (double Score, List<Vec> Points)? best = null;

            foreach (var side in new[] { +1, -1 })
            {
                var arc = Arc(from, to, run.Count, side);

                foreach (var bow in new[] { 1.0, 0.6, 0.4, 0.25 })
                {
                    var points = new List<Vec>(run.Count);
                    for (var k = 0; k < run.Count; k++)
                    {
                        var straight = from + (to - from) * ((k + 1.0) / (run.Count + 1));
                        points.Add(straight + (arc[k] - straight) * bow);
                    }

                    var trial = new Dictionary<int, Vec>(shape);
                    for (var k = 0; k < run.Count; k++) trial[run[k]] = points[k];

                    var stretch = 0.0;
                    var path = new List<Vec> { from };
                    path.AddRange(points);
                    path.Add(to);
                    for (var k = 0; k + 1 < path.Count; k++) stretch += Math.Abs(Vec.Distance(path[k], path[k + 1]) - 1);

                    var score = Tangle(system, trial) * 100 + Crowding(points, shape) + stretch * 4;
                    if (best is null || score < best.Value.Score - 1e-9) best = (score, points);
                }
            }

            for (var k = 0; k < run.Count; k++) shape[run[k]] = best!.Value.Points[k];
        }
    }

    private static double Crowding(IReadOnlyList<Vec> points, Dictionary<int, Vec> shape)
    {
        var sum = 0.0;
        foreach (var point in points)
            foreach (var other in shape.Values)
            {
                var d = Vec.Distance(point, other);
                sum += 1 / (d * d + 0.05);
            }

        return sum;
    }

    /// <summary>
    /// <paramref name="count"/> points between <paramref name="from"/> and <paramref name="to"/>, one bond apart
    /// along a circular arc bulging to <paramref name="side"/> — or along the straight line, stretched, where the
    /// ends are too far apart for any arc of that many bonds to reach.
    /// </summary>
    private static List<Vec> Arc(Vec from, Vec to, int count, int side)
    {
        var chord = Vec.Distance(from, to);
        var segments = count + 1;
        var points = new List<Vec>(count);

        if (chord >= segments - 1e-6 || chord < 1e-9)
        {
            var along = to - from;
            var bulge = along.Perpendicular.Unit * side * (chord < 1e-9 ? 1 : 0);
            for (var k = 1; k <= count; k++) points.Add(from + along * ((double)k / segments) + bulge * Math.Sin(Math.PI * k / segments));
            return points;
        }

        // The angle each bond subtends at the centre, found by bisection: the chord an arc of that many unit bonds
        // spans shrinks steadily as the angle grows, from all of them laid straight to nothing once it closes.
        double low = 1e-9, high = Tau / segments;
        for (var iteration = 0; iteration < 60; iteration++)
        {
            var mid = (low + high) / 2;
            var spans = Math.Sin(segments * mid / 2) / Math.Sin(mid / 2);
            if (spans > chord) low = mid;
            else high = mid;
        }

        var theta = (low + high) / 2;
        var radius = 0.5 / Math.Sin(theta / 2);
        var total = segments * theta;

        var middle = (from + to) / 2;
        var out_ = (to - from).Perpendicular.Unit * side;
        var centre = middle - out_ * (radius * Math.Cos(total / 2));

        var a0 = (from - centre).Angle;
        var sweep = Math.Abs(Normalise(a0 + total / 2 - out_.Angle)) < Math.Abs(Normalise(a0 - total / 2 - out_.Angle)) ? 1 : -1;

        for (var k = 1; k <= count; k++)
            points.Add(centre + Vec.FromAngle(a0 + sweep * theta * k) * radius);

        return points;
    }
}
