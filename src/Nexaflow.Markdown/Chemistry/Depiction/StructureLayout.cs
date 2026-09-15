namespace Nexaflow.Markdown.Chemistry.Depiction;

/// <summary>How a single bond is drawn to say which way it leaves a stereocentre.</summary>
public enum Wedge
{
    None,

    /// <summary>A filled wedge: the far atom is towards the reader.</summary>
    Solid,

    /// <summary>A hashed wedge: the far atom is away from the reader.</summary>
    Hashed,
}

/// <summary>Where a molecule's atoms go on the page, and what else a drawing of it needs to know.</summary>
/// <param name="At">Each atom's position, in bond lengths, y up.</param>
/// <param name="Wedges">For each bond, how it is drawn to show a stereocentre, and the atom at its narrow end.</param>
/// <param name="Rings">The smallest set of smallest rings — what a ring's double bond is drawn inside.</param>
/// <param name="Depth">
/// How near the reader each atom of a cage drawn as a solid is — larger is nearer — and nothing for an atom on the
/// flat page. Where two bonds of a solid cross, the one further back is the one broken.
/// </param>
public sealed record Structure(IReadOnlyList<Vec> At, IReadOnlyList<(Wedge Kind, int Narrow)> Wedges, IReadOnlyList<int[]> Rings,
                               IReadOnlyList<double?> Depth);

/// <summary>
/// Works out a 2D drawing of a molecule: where every atom goes so that bonds are one length, angles are even, rings
/// are regular and nothing lands on anything else.
///
/// <para>
/// <b>Rings first, as rigid shapes; chains grown out from them.</b> Each ring system — rings fused along a bond or
/// bridged across several — is laid out on its own as polygons built edge on edge, and then placed whole, the way
/// a chemist draws naphthalene as two hexagons before thinking about what hangs off it. Everything else is grown
/// outward from the largest system, one atom's neighbours at a time, into the widest gap round that atom: a chain
/// comes out as a zig-zag because each atom puts its next neighbour on the far side from the one before, a triple
/// bond comes out straight, and a <c>/</c> or <c>\</c> puts a double bond's substituents on the side SMILES says.
/// </para>
/// <para>
/// <b>Then it looks for trouble.</b> A tree grown atom by atom can fold back into itself, so any two atoms that land
/// too close are pulled apart by turning the smaller side of a single bond between them over, or round, and keeping
/// whatever leaves the page least crowded. A reflection leaves every double bond as cis or as trans as it was, which
/// is why turning over is tried first.
/// </para>
/// <para>
/// <b>Last, it is set straight</b> — its long axis along the page, and turned by the few degrees that put the most
/// bonds on the angles a structure is conventionally drawn at — and wedges are chosen for the stereocentres, since
/// which way a wedge points depends on where everything ended up.
/// </para>
/// </summary>
public static class StructureLayout
{
    /// <summary>Clear space between two molecules written in one string, in bond lengths.</summary>
    private const double Apart = 1.5;

    public static Structure Of(Molecule molecule)
    {
        var rings = MoleculeRings.Smallest(molecule);
        var at = new Vec[molecule.Atoms.Count];
        var depth = new double?[molecule.Atoms.Count];

        var components = Components(molecule);
        var left = 0.0;

        foreach (var component in components)
        {
            var grown = new Grower(molecule, rings, component, at, depth);
            grown.Grow();
            new Untangler(molecule, component, at, depth).Untangle();
            Straighten(molecule, component, at, depth);

            // Side by side along the page, each centred on the same line.
            var (minX, maxX, minY, maxY) = Bounds(component, at);
            var shift = new Vec(left - minX, -(minY + maxY) / 2);
            foreach (var atom in component) at[atom] += shift;

            left += maxX - minX + Apart;
        }

        return new Structure(at, Stereo.Wedges(molecule, at, rings), rings, depth);
    }

    /// <summary>The atoms bonded to one another, a list per molecule written — in the order they were written.</summary>
    private static List<List<int>> Components(Molecule molecule)
    {
        var seen = new bool[molecule.Atoms.Count];
        var components = new List<List<int>>();

        for (var start = 0; start < seen.Length; start++)
        {
            if (seen[start]) continue;

            var component = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;

            while (queue.Count > 0)
            {
                var atom = queue.Dequeue();
                component.Add(atom);
                foreach (var bond in molecule.BondsAt(atom))
                {
                    var other = molecule.Bonds[bond].Other(atom);
                    if (seen[other]) continue;
                    seen[other] = true;
                    queue.Enqueue(other);
                }
            }

            component.Sort();
            components.Add(component);
        }

        return components;
    }

    private static (double MinX, double MaxX, double MinY, double MaxY) Bounds(IEnumerable<int> atoms, Vec[] at)
    {
        var (minX, maxX, minY, maxY) = (double.MaxValue, double.MinValue, double.MaxValue, double.MinValue);
        foreach (var atom in atoms)
        {
            minX = Math.Min(minX, at[atom].X);
            maxX = Math.Max(maxX, at[atom].X);
            minY = Math.Min(minY, at[atom].Y);
            maxY = Math.Max(maxY, at[atom].Y);
        }

        return (minX, maxX, minY, maxY);
    }

    /// <summary>
    /// Turns a laid-out molecule so its long axis runs along the page, then by up to half a hexagon's corner more,
    /// whichever way puts the most bonds at thirty degrees off a multiple of sixty — the angle a zig-zag chain and a
    /// hexagon standing on its point are both drawn at.
    ///
    /// <para>
    /// <b>A cage is stood on its uprights and seen from above.</b> A picture of a solid reads either way round — a cube
    /// looked down on and looked up at is the same set of lines — and what settles it for a reader is how the drawing
    /// is framed. Parallel strokes drawn straight up the page read as upright edges, and an upright is seen from above
    /// when its top end is nearer the eye, as the top of a post is. So a molecule that is mostly cage is turned until
    /// the family of parallel bonds that shows longest stands straight up the page — the way every textbook stands
    /// adamantane and cubane — and any molecule holding a cage is walked round to its other side when its uprights lean
    /// away from the reader.
    /// </para>
    /// </summary>
    private static void Straighten(Molecule molecule, List<int> component, Vec[] at, double?[] depth)
    {
        if (component.Count < 2) return;

        var centre = component.Aggregate(Vec.Zero, (sum, atom) => sum + at[atom]) / component.Count;
        var bonds = molecule.Bonds.Where(bond => component.BinarySearch(bond.From) >= 0).ToList();
        var cage = bonds.Where(bond => depth[bond.From] is not null && depth[bond.To] is not null).ToList();
        var solid = component.Count(atom => depth[atom] is not null);

        double turn;
        if (cage.Count >= 3 && solid * 2 >= component.Count)
        {
            turn = Math.PI / 2 - Uprights(cage, at);
        }
        else
        {
            double xx = 0, xy = 0, yy = 0;
            foreach (var atom in component)
            {
                var d = at[atom] - centre;
                xx += d.X * d.X;
                xy += d.X * d.Y;
                yy += d.Y * d.Y;
            }

            var axis = 0.5 * Math.Atan2(2 * xy, xx - yy);
            turn = -axis;
            var bestScore = -1;

            for (var step = -30; step <= 30; step++)
            {
                var trial = -axis + step * Math.PI / 180;
                var score = 0;

                foreach (var bond in bonds)
                {
                    var angle = ((at[bond.To] - at[bond.From]).Angle + trial) * 180 / Math.PI;
                    var off = ((angle % 60) + 60) % 60;
                    if (Math.Abs(off - 30) < 2) score++;
                }

                if (score > bestScore || (score == bestScore && Math.Abs(trial + axis) < Math.Abs(turn + axis)))
                {
                    turn = trial;
                    bestScore = score;
                }
            }
        }

        foreach (var atom in component) at[atom] = centre + (at[atom] - centre).Rotated(turn);

        if (cage.Count == 0) return;

        // Its uprights lean away from the reader: seen from underneath. Walked round to its other side — turned about
        // the page's upright, left for right and front for back — which leaves its uprights up and their tops nearer.
        if (Leaning(cage, at, depth) < 0)
            foreach (var atom in component)
            {
                at[atom] = new Vec(2 * centre.X - at[atom].X, at[atom].Y);
                if (depth[atom] is { } near) depth[atom] = -near;
            }

        // And framed the way the textbook frames it, with the nearest upright left of the middle. A reflection of the
        // picture, which is still a picture of the same molecule: wedges and double-bond geometry are read off the page
        // after this, not before.
        var front = cage
            .Where(bond => Math.Abs((at[bond.To] - at[bond.From]).Unit.Y) > Upright)
            .OrderByDescending(bond => depth[bond.From]!.Value + depth[bond.To]!.Value)
            .FirstOrDefault();

        var middle = cage.SelectMany(bond => new[] { bond.From, bond.To }).Distinct().Average(atom => at[atom].X);
        if (front is null || (at[front.From].X + at[front.To].X) / 2 <= middle) return;

        foreach (var atom in component) at[atom] = new Vec(2 * centre.X - at[atom].X, at[atom].Y);
    }

    /// <summary>
    /// The direction on the page of the family of parallel bonds that shows longest — bonds that run the same way in the
    /// solid run the same way in any picture of it, so they are found by their angle.
    /// </summary>
    private static double Uprights(List<MoleculeBond> bonds, Vec[] at)
    {
        const int bins = 36;
        var weight = new double[bins];

        foreach (var bond in bonds)
        {
            var along = at[bond.To] - at[bond.From];
            var angle = (along.Angle % Math.PI + Math.PI) % Math.PI;
            weight[(int)(angle / Math.PI * bins) % bins] += along.Length;
        }

        // A family's bonds can straddle two bins, so each bin is judged with its neighbours.
        var best = 0;
        var bestWeight = -1.0;
        for (var bin = 0; bin < bins; bin++)
        {
            var around = weight[(bin + bins - 1) % bins] + weight[bin] + weight[(bin + 1) % bins];
            if (around <= bestWeight) continue;
            best = bin;
            bestWeight = around;
        }

        // The mean direction of the bonds in that neighbourhood, averaged as doubled angles so the two ends of the half
        // turn meet.
        var sum = Vec.Zero;
        foreach (var bond in bonds)
        {
            var along = at[bond.To] - at[bond.From];
            var angle = (along.Angle % Math.PI + Math.PI) % Math.PI;
            var bin = (int)(angle / Math.PI * bins) % bins;
            if (Math.Min(Math.Abs(bin - best), bins - Math.Abs(bin - best)) > 1) continue;
            sum += Vec.FromAngle(2 * angle) * along.Length;
        }

        return sum.Angle / 2;
    }

    /// <summary>How near straight up the page a bond has to be drawn to be read as an upright: within fourteen degrees.</summary>
    private const double Upright = 0.97;

    /// <summary>
    /// Whether a cage's upright bonds lean towards the reader at the top — positive — or away. Only the bonds drawn
    /// within a few degrees of upright are read as uprights, because those are what a reader takes for them: a cage's
    /// steep diagonals often lean the other way, and in hexamine they stand barely twenty degrees off upright. A cage
    /// with nothing upright is judged by every bond, counted steeply by how near upright it is.
    /// </summary>
    private static double Leaning(List<MoleculeBond> bonds, Vec[] at, double?[] depth)
    {
        double Lean(Func<double, double> weight)
        {
            var lean = 0.0;
            foreach (var bond in bonds)
            {
                var along = at[bond.To] - at[bond.From];
                if (along.Length < 1e-6) continue;

                var nearer = depth[bond.To]!.Value - depth[bond.From]!.Value;
                lean += Math.Sign(along.Y) * nearer * weight(Math.Abs(along.Y) / along.Length);
            }

            return lean;
        }

        var upright = Lean(steep => steep > Upright ? 1 : 0);
        return upright != 0 ? upright : Lean(steep => Math.Pow(steep, 8));
    }

    /// <summary>
    /// Which way across the page the atoms of a solid come nearer the reader: the slope of the plane their depths best
    /// fit.
    /// </summary>
    private static Vec Nearer(List<int> atoms, Vec[] at, double?[] depth)
    {
        var centre = atoms.Aggregate(Vec.Zero, (sum, atom) => sum + at[atom]) / atoms.Count;
        var mean = atoms.Average(atom => depth[atom]!.Value);

        double xx = 0, xy = 0, yy = 0, xd = 0, yd = 0;
        foreach (var atom in atoms)
        {
            var p = at[atom] - centre;
            var d = depth[atom]!.Value - mean;
            xx += p.X * p.X;
            xy += p.X * p.Y;
            yy += p.Y * p.Y;
            xd += p.X * d;
            yd += p.Y * d;
        }

        var determinant = xx * yy - xy * xy;
        if (Math.Abs(determinant) < 1e-12) return Vec.Zero;

        return new Vec((xd * yy - yd * xy) / determinant, (yd * xx - xd * xy) / determinant);
    }
}
