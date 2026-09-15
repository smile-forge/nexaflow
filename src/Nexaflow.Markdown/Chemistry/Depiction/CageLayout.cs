namespace Nexaflow.Markdown.Chemistry.Depiction;

/// <summary>
/// A ring system drawn as the solid it is: built in three dimensions, then seen from the side that shows it best.
///
/// <para>
/// <b>Why a cage is not a flat drawing.</b> Adamantane, cubane, hexamine, the phosphorus oxides — a whole family of
/// molecules are closed shells whose rings share three atoms or more, and no arrangement of polygons on a page draws
/// one: flattened, some bonds come out twice as long as others and an atom lands on the line between two more. A
/// chemist draws them the way the textbook does, as a picture of the solid from a slightly turned angle, with the
/// bond at the back broken where it passes behind one at the front. That is what this makes.
/// </para>
/// <para>
/// <b>The solid comes from its bonds and angles.</b> Every pair of atoms gets a distance to be: one bond length
/// for a bond, the span of a tetrahedral angle for two atoms sharing a neighbour, and the reach of a zig-zag for
/// anything further, held loosely. Classical scaling of those distances gives a first solid with no arbitrary start
/// in it, and stress majorization settles it. A cage is rigid, so the near distances alone decide its shape.
/// </para>
/// <para>
/// <b>The side it is seen from is chosen by looking.</b> Hundreds of directions round a hemisphere are tried, each
/// projected flat and scored by <see cref="Readability"/> — atoms that coincide, an atom hidden on a bond, a bond seen
/// end on, bonds that cross — and the clearest kept. Seen straight down an axis of symmetry a cage hides half of itself,
/// which is exactly what the score punishes.
/// </para>
/// </summary>
internal static class CageLayout
{
    /// <summary>The span of a tetrahedral angle between two bonds of one length.</summary>
    private const double Tetrahedral = 1.633;

    /// <summary>How many directions round the hemisphere are looked from.</summary>
    private const int Views = 400;

    /// <summary>A cage seen: where each atom lands on the page, how near the reader it is, and which way out of the cage it faces.</summary>
    /// <param name="At">Each atom's place on the page, a bond seen side on one long.</param>
    /// <param name="Depth">How near the reader each atom is, in the same units — larger is nearer.</param>
    /// <param name="Outward">
    /// The direction a substituent leaves each atom by, as it would in the solid, seen on the page; short where it
    /// points at or away from the reader.
    /// </param>
    /// <param name="Pairs">
    /// For an atom with two substituents outside the cage, the two directions they leave by, seen on the page.
    /// </param>
    public sealed record Seen(Dictionary<int, Vec> At, Dictionary<int, double> Depth, Dictionary<int, Vec> Outward,
                              Dictionary<int, Vec[]> Pairs, double Score);

    /// <param name="labelled">Which of <paramref name="atoms"/> are drawn as a symbol.</param>
    /// <param name="hanging">
    /// For each of <paramref name="atoms"/>, what hangs off it outside the cage — each drawn as a symbol or not. The view
    /// is chosen with those in it too: a phosphorus oxide seen with one P=O pointing at the reader puts that oxygen on
    /// top of the cage.
    /// </param>
    public static Seen Of(IReadOnlyList<int> atoms, IReadOnlyList<(int A, int B)> bonds, IReadOnlyList<int[]> rings,
                          IReadOnlyList<bool> labelled, IReadOnlyList<IReadOnlyList<bool>> hanging)
    {
        var n = atoms.Count;
        var index = new Dictionary<int, int>();
        for (var i = 0; i < n; i++) index[atoms[i]] = i;

        var local = bonds.Select(b => (index[b.A], index[b.B])).ToList();
        var hops = Hops(n, local);

        // The corners across a four-membered ring are a square's diagonal apart, not a tetrahedral angle's span: a
        // cubane's faces are squares, and a cube is what it has to come out as.
        var across = new HashSet<(int, int)>();
        foreach (var ring in rings.Where(ring => ring.Length == 4 && ring.All(index.ContainsKey)))
        {
            across.Add(Pair(index[ring[0]], index[ring[2]]));
            across.Add(Pair(index[ring[1]], index[ring[3]]));
        }

        var solid = Settle(Scale(hops, across), hops, across);

        // Which way out of the solid each atom faces: away from its neighbours in the cage.
        var away = new (double X, double Y, double Z)[n];
        for (var i = 0; i < n; i++)
        {
            var sum = (X: 0.0, Y: 0.0, Z: 0.0);
            foreach (var (a, b) in local)
            {
                if (a != i && b != i) continue;
                var d = Unit(Minus(solid[i], solid[a == i ? b : a]));
                sum = (sum.X + d.X, sum.Y + d.Y, sum.Z + d.Z);
            }

            away[i] = Unit(sum);
        }

        // What hangs off the cage, stood one bond out along that way, so the view is judged with it in place.
        var scene = solid.ToList();
        var sceneBonds = local.ToList();
        var sceneLabels = labelled.ToList();
        for (var i = 0; i < n; i++)
        {
            for (var k = 0; k < hanging[i].Count && k < 2; k++)
            {
                var direction = hanging[i].Count == 1 ? away[i] : Fanned(solid, local, i, away[i], k == 0 ? 1 : -1);
                sceneBonds.Add((i, scene.Count));
                scene.Add((solid[i].X + direction.X, solid[i].Y + direction.Y, solid[i].Z + direction.Z));
                sceneLabels.Add(hanging[i][k]);
            }
        }

        var sceneArray = scene.ToArray();

        // The pairs of substituents that leave one atom — a gem-dimethyl — whose two bonds have to read as a V.
        var twins = new List<(int Atom, int One, int Other)>();
        for (var b = local.Count; b + 1 < sceneBonds.Count; b++)
            if (sceneBonds[b].Item1 == sceneBonds[b + 1].Item1)
                twins.Add((sceneBonds[b].Item1, sceneBonds[b].Item2, sceneBonds[b + 1].Item2));

        var best = (Score: double.MaxValue, Along: (X: 0.0, Y: 0.0, Z: 1.0));
        foreach (var direction in Hemisphere(Views))
        {
            var (flat, _) = Project(sceneArray, direction);
            var score = Readability.Score(flat, sceneBonds, sceneLabels);

            // Seen with the atom between them, a pair of substituents is one straight line through it.
            foreach (var (atom, one, other) in twins)
            {
                var angle = Math.Acos(Math.Clamp(Vec.Dot((flat[one] - flat[atom]).Unit, (flat[other] - flat[atom]).Unit), -1, 1));
                if (angle > 140 * Math.PI / 180) score += 25;
            }

            if (score < best.Score - 1e-9) best = (score, direction);
        }

        var (page, depth) = Project(solid, best.Along);

        // One bond seen side on is one long: the mean of what the bonds project to is scaled to the page's bond.
        var mean = local.Count == 0 ? 1 : local.Average(b => Vec.Distance(page[b.Item1], page[b.Item2]));
        var scale = mean > 1e-9 ? 1 / mean : 1;

        var outward = new Dictionary<int, Vec>();
        var pairs = new Dictionary<int, Vec[]>();
        var at = new Dictionary<int, Vec>();
        var near = new Dictionary<int, double>();
        var (u, w, _) = Basis(best.Along);

        for (var i = 0; i < n; i++)
        {
            at[atoms[i]] = page[i] * scale;
            near[atoms[i]] = depth[i] * scale;
            outward[atoms[i]] = new Vec(Dot(away[i], u), Dot(away[i], w));

            if (hanging[i].Count != 2) continue;
            pairs[atoms[i]] = [.. new[] { 1, -1 }.Select(side => Fanned(solid, local, i, away[i], side)).Select(d => new Vec(Dot(d, u), Dot(d, w)))];
        }

        return new Seen(at, near, outward, pairs, best.Score);
    }

    /// <summary>
    /// One of the two directions a pair of substituents leaves a two-bonded cage atom by: tilted from the way out towards
    /// either side of the plane its two cage bonds lie in, as far as a tetrahedron puts them.
    /// </summary>
    private static (double X, double Y, double Z) Fanned((double X, double Y, double Z)[] solid, List<(int, int)> bonds, int atom,
                                                         (double X, double Y, double Z) away, int side)
    {
        var neighbours = bonds.Where(b => b.Item1 == atom || b.Item2 == atom).Select(b => b.Item1 == atom ? b.Item2 : b.Item1).Take(2).ToList();
        if (neighbours.Count < 2) return away;

        var a = Minus(solid[neighbours[0]], solid[atom]);
        var b = Minus(solid[neighbours[1]], solid[atom]);
        var normal = Unit((a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X));

        const double tilt = 54.75 * Math.PI / 180;
        return Unit((away.X * Math.Cos(tilt) + normal.X * side * Math.Sin(tilt),
                     away.Y * Math.Cos(tilt) + normal.Y * side * Math.Sin(tilt),
                     away.Z * Math.Cos(tilt) + normal.Z * side * Math.Sin(tilt)));
    }

    private static (int, int) Pair(int a, int b) => (Math.Min(a, b), Math.Max(a, b));

    /// <summary>
    /// The distance two atoms are meant to be: a bond, an angle's span — a square's diagonal across a four-membered
    /// ring — and, for anything further, the chain formula classical scaling starts from.
    /// </summary>
    private static double Ideal(int[,] hops, HashSet<(int, int)> across, int i, int j) =>
        hops[i, j] == 2 && across.Contains(Pair(i, j)) ? Math.Sqrt(2) : Ideal(hops[i, j]);

    // ── The solid ───────────────────────────────────────────────────────────

    /// <summary>How many bonds apart each pair of atoms is.</summary>
    private static int[,] Hops(int n, List<(int, int)> bonds)
    {
        var around = new List<int>[n];
        for (var i = 0; i < n; i++) around[i] = [];
        foreach (var (a, b) in bonds)
        {
            around[a].Add(b);
            around[b].Add(a);
        }

        var hops = new int[n, n];
        for (var s = 0; s < n; s++)
        {
            for (var t = 0; t < n; t++) hops[s, t] = int.MaxValue;
            hops[s, s] = 0;
            var queue = new Queue<int>();
            queue.Enqueue(s);
            while (queue.Count > 0)
            {
                var v = queue.Dequeue();
                foreach (var next in around[v])
                {
                    if (hops[s, next] != int.MaxValue) continue;
                    hops[s, next] = hops[s, v] + 1;
                    queue.Enqueue(next);
                }
            }
        }

        return hops;
    }

    /// <summary>The distance two atoms that many bonds apart are meant to be.</summary>
    private static double Ideal(int hops) => hops switch
    {
        0 => 0,
        1 => 1,
        2 => Tetrahedral,
        _ => hops * Tetrahedral / 2 * 0.9,
    };

    /// <summary>How hard a pair is held to its distance: bonds and angles firmly, the rest only loosely.</summary>
    private static double Weight(int hops) => hops switch
    {
        1 => 4,
        2 => 2,
        _ => 1 / (Ideal(hops) * Ideal(hops)),
    };

    /// <summary>
    /// Classical scaling: the three directions the distances spread furthest along, found by power iteration from a
    /// start fixed by the atoms' order, so the same cage always comes out the same.
    /// </summary>
    private static (double X, double Y, double Z)[] Scale(int[,] hops, HashSet<(int, int)> across)
    {
        var n = hops.GetLength(0);
        var b = new double[n, n];

        var squared = new double[n, n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
            {
                var d = hops[i, j] == int.MaxValue ? 0 : Ideal(hops, across, i, j);
                squared[i, j] = d * d;
            }

        var rows = new double[n];
        var all = 0.0;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++) rows[i] += squared[i, j];
            all += rows[i];
            rows[i] /= n;
        }

        all /= (double)n * n;
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                b[i, j] = -0.5 * (squared[i, j] - rows[i] - rows[j] + all);

        var coordinates = new double[3][];
        for (var axis = 0; axis < 3; axis++)
        {
            var v = new double[n];
            for (var i = 0; i < n; i++) v[i] = Math.Sin(1.7 * i + 0.9 * axis + 0.3) + 0.01 * i;

            var value = 0.0;
            for (var iteration = 0; iteration < 300; iteration++)
            {
                var next = new double[n];
                for (var i = 0; i < n; i++)
                    for (var j = 0; j < n; j++)
                        next[i] += b[i, j] * v[j];

                // Held apart from the directions already found, so each is a new one.
                for (var k = 0; k < axis; k++)
                {
                    var along = 0.0;
                    for (var i = 0; i < n; i++) along += next[i] * coordinates[k][i];
                    var norm = coordinates[k].Sum(x => x * x);
                    if (norm > 1e-12) for (var i = 0; i < n; i++) next[i] -= along / norm * coordinates[k][i];
                }

                var length = Math.Sqrt(next.Sum(x => x * x));
                if (length < 1e-12) break;
                value = length;
                for (var i = 0; i < n; i++) v[i] = next[i] / length;
            }

            coordinates[axis] = [.. v.Select(x => x * Math.Sqrt(Math.Max(value, 1e-6)))];
        }

        return [.. Enumerable.Range(0, n).Select(i => (coordinates[0][i], coordinates[1][i], coordinates[2][i]))];
    }

    /// <summary>
    /// Stress majorization in three dimensions: each atom in turn moved to where its pairs pull it. Bonds and angles
    /// hold firmly; atoms further apart only push away from one another when they come nearer than two atoms three
    /// bonds apart ever are — a cage is rigid, and telling its far corners how far apart to be only bends it.
    /// </summary>
    private static (double X, double Y, double Z)[] Settle((double X, double Y, double Z)[] p, int[,] hops, HashSet<(int, int)> across)
    {
        const double nearest = 1.6;
        var n = p.Length;

        for (var iteration = 0; iteration < 400; iteration++)
        {
            for (var i = 0; i < n; i++)
            {
                var sum = (X: 0.0, Y: 0.0, Z: 0.0);
                var weights = 0.0;

                for (var j = 0; j < n; j++)
                {
                    if (i == j || hops[i, j] == int.MaxValue) continue;

                    var apart = Minus(p[i], p[j]);
                    var length = Math.Sqrt(Dot(apart, apart));

                    var far = hops[i, j] >= 3;
                    if (far && length >= nearest) continue;

                    var ideal = far ? nearest : Ideal(hops, across, i, j);
                    var weight = far ? 2 / (nearest * nearest) : Weight(hops[i, j]);
                    var towards = length > 1e-9
                        ? (apart.X / length, apart.Y / length, apart.Z / length)
                        : (Math.Sin(i - j), Math.Cos(i - j), 0.5);

                    sum = (sum.X + (p[j].X + towards.Item1 * ideal) * weight,
                           sum.Y + (p[j].Y + towards.Item2 * ideal) * weight,
                           sum.Z + (p[j].Z + towards.Item3 * ideal) * weight);
                    weights += weight;
                }

                if (weights > 0) p[i] = (sum.X / weights, sum.Y / weights, sum.Z / weights);
            }
        }

        return p;
    }

    // ── Looking at it ───────────────────────────────────────────────────────

    /// <summary>Directions spread evenly over the half of a sphere facing the reader, by the golden angle.</summary>
    private static IEnumerable<(double X, double Y, double Z)> Hemisphere(int count)
    {
        var golden = Math.PI * (3 - Math.Sqrt(5));
        for (var i = 0; i < count; i++)
        {
            var z = 1 - (i + 0.5) / count;
            var r = Math.Sqrt(1 - z * z);
            yield return (r * Math.Cos(golden * i), r * Math.Sin(golden * i), z);
        }
    }

    /// <summary>Two directions across the page and the one out of it, right-handed, for looking along <paramref name="view"/>.</summary>
    private static ((double X, double Y, double Z) U, (double X, double Y, double Z) W, (double X, double Y, double Z) V) Basis((double X, double Y, double Z) view)
    {
        var v = Unit(view);
        var seed = Math.Abs(v.X) < 0.9 ? (1.0, 0.0, 0.0) : (0.0, 1.0, 0.0);
        var along = Dot(seed, v);
        var u = Unit((seed.Item1 - v.X * along, seed.Item2 - v.Y * along, seed.Item3 - v.Z * along));
        var w = (v.Y * u.Z - v.Z * u.Y, v.Z * u.X - v.X * u.Z, v.X * u.Y - v.Y * u.X);
        return (u, w, v);
    }

    private static (Vec[] Page, double[] Depth) Project((double X, double Y, double Z)[] solid, (double X, double Y, double Z) view)
    {
        var (u, w, v) = Basis(view);
        return ([.. solid.Select(p => new Vec(Dot(p, u), Dot(p, w)))], [.. solid.Select(p => Dot(p, v))]);
    }

    private static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static (double X, double Y, double Z) Minus((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static (double X, double Y, double Z) Unit((double X, double Y, double Z) a)
    {
        var length = Math.Sqrt(Dot(a, a));
        return length > 1e-12 ? (a.X / length, a.Y / length, a.Z / length) : (0, 0, 0);
    }
}

/// <summary>
/// How easily a drawing of a ring system reads — lower is clearer. The one yardstick a flat drawing and a picture of the
/// solid are both measured by, so choosing between them is a comparison rather than a rule about which molecules are
/// cages.
/// </summary>
internal static class Readability
{
    /// <param name="labelled">
    /// Which atoms are drawn as a symbol rather than a corner. A symbol takes room a corner does not, so it needs more
    /// clearance from the atoms and bonds around it before it reads.
    /// </param>
    public static double Score(IReadOnlyList<Vec> at, IReadOnlyList<(int A, int B)> bonds, IReadOnlyList<bool>? labelled = null)
    {
        if (bonds.Count == 0) return 0;

        var lengths = bonds.Select(b => Vec.Distance(at[b.A], at[b.B])).ToList();
        var mean = lengths.Average();
        if (mean < 1e-9) return double.MaxValue;

        var score = 0.0;
        var n = at.Count;
        var bonded = new HashSet<(int, int)>(bonds.Select(b => (Math.Min(b.A, b.B), Math.Max(b.A, b.B))));

        bool Labelled(int atom) => labelled is not null && labelled[atom];

        // Atoms on top of one another: nothing reads. Graded steeply by how far in, because a cage of symbols has
        // pairs just inside their room from every side, and what has to be told apart from that is two symbols
        // printed as one.
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                var room = (Labelled(i) && Labelled(j) ? 0.9 : Labelled(i) || Labelled(j) ? 0.65 : 0.45) * mean;
                var apart = Vec.Distance(at[i], at[j]);
                if (!bonded.Contains((i, j)) && apart < room) score += 5 + 100 * Math.Pow(1 - apart / room, 2);
            }

        // An atom lying on a bond it is not part of: the atom vanishes into the line.
        for (var c = 0; c < n; c++)
            foreach (var (a, b) in bonds)
            {
                if (a == c || b == c) continue;
                var along = at[b] - at[a];
                var t = Vec.Dot(at[c] - at[a], along) / Vec.Dot(along, along);
                if (t is <= 0.08 or >= 0.92) continue;
                var clear = (Labelled(c) ? 0.5 : 0.2) * mean;
                var off = Vec.Distance(at[a] + along * t, at[c]);
                if (off < clear) score += 3 + 40 * Math.Pow(1 - off / clear, 2);
            }

        // A bond seen nearly end on hides the atom at its far end — and when it is the bond to a substituent, the
        // substituent has nowhere to be drawn but on the cage.
        foreach (var length in lengths)
            if (length < 0.4 * mean) score += 45;

        // Bonds crossing: a cage has to show some, but each is a thing to follow — and one crossing hard by an atom
        // reads as a bond to it.
        for (var x = 0; x < bonds.Count; x++)
            for (var y = x + 1; y < bonds.Count; y++)
            {
                var (a, b) = bonds[x];
                var (c, d) = bonds[y];
                if (a == c || a == d || b == c || b == d || !Vec.Crosses(at[a], at[b], at[c], at[d])) continue;

                score += 6;
                var r = at[b] - at[a];
                var s = at[d] - at[c];
                var t = Vec.Cross(at[c] - at[a], s) / Vec.Cross(r, s);
                var point = at[a] + r * t;
                if (new[] { a, b, c, d }.Any(atom => Vec.Distance(point, at[atom]) < (Labelled(atom) ? 0.55 : 0.3) * mean)) score += 20;
            }

        // Two bonds at one atom nearly on top of each other, or nearly straight through a two-bonded one.
        var around = new List<int>[n];
        for (var i = 0; i < n; i++) around[i] = [];
        foreach (var (a, b) in bonds)
        {
            around[a].Add(b);
            around[b].Add(a);
        }

        for (var i = 0; i < n; i++)
        {
            for (var p = 0; p < around[i].Count; p++)
                for (var q = p + 1; q < around[i].Count; q++)
                {
                    var angle = Math.Acos(Math.Clamp(Vec.Dot((at[around[i][p]] - at[i]).Unit, (at[around[i][q]] - at[i]).Unit), -1, 1));
                    if (angle < 0.44) score += 10;

                    // A two-bonded corner opened past a hundred and fifty degrees starts to read as a line with
                    // nothing on it, and the straighter it is the less it reads as an atom at all. A symbol on a
                    // straight line is still a symbol.
                    const double open = 150 * Math.PI / 180;
                    if (around[i].Count == 2 && !Labelled(i) && angle > open) score += (angle - open) / (10 * Math.PI / 180) * 12;
                }
        }

        // And bonds that are not one length.
        var spread = Math.Sqrt(lengths.Average(length => (length - mean) * (length - mean))) / mean;
        return score + 40 * spread;
    }
}
