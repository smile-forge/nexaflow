namespace Nexaflow.Markdown.Chemistry.Depiction;

/// <summary>
/// Chooses a wedge for each stereocentre written with <c>@</c> or <c>@@</c>, pointing whichever way makes the
/// drawing mean what the string says.
///
/// <para>
/// <b>SMILES says it by order.</b> Looking from the first neighbour an atom was written with, <c>@</c> means the rest
/// go round anticlockwise and <c>@@</c> clockwise — the neighbours in the order written, with a hydrogen in the
/// brackets standing right after the atom before it. A drawing says it by depth: one bond drawn as a wedge coming
/// out of the page, or hashed going into it. So the question is which of the two a chosen bond has to be, and it is
/// answered by building the centre in three dimensions — the page flat, the wedged neighbour lifted, a hydrogen
/// nobody drew standing opposite the rest — and asking which way round its neighbours go.
/// </para>
/// <para>
/// The bond chosen is the least committed one: a single bond out of the ring rather than round it, to an atom that
/// is not a stereocentre itself and has the fewest neighbours — and never a bond another centre has already taken,
/// because a wedge's narrow end says which atom it is about.
/// </para>
/// </summary>
internal static class Stereo
{
    public static IReadOnlyList<(Wedge Kind, int Narrow)> Wedges(MoleculeNode molecule, IReadOnlyList<Vec> at, IReadOnlyList<int[]> rings)
    {
        var wedges = new (Wedge, int)[molecule.Bonds.Count];
        var ring = MoleculeRings.RingBonds(molecule);

        var centres = molecule.Atoms
            .Where(atom => atom.Chirality is "@" or "@@" or "@TH1" or "@TH2")
            .Select(atom => atom.Index)
            .ToHashSet();

        foreach (var centre in centres.Order())
        {
            var atom = molecule.Atoms[centre];
            var order = Order(molecule, atom);
            if (order.Count != 4) continue;

            var candidates = molecule.BondsAt(centre)
                .Select(bond => molecule.Bonds[bond])
                .Where(bond => bond.Order == BondOrder.Single && wedges[bond.Index].Item1 == Wedge.None)
                .OrderBy(bond => ring[bond.Index] ? 1 : 0)
                .ThenBy(bond => centres.Contains(bond.Other(centre)) ? 1 : 0)
                .ThenBy(bond => molecule.BondsAt(bond.Other(centre)).Count)
                .ThenBy(bond => bond.Index)
                .ToList();

            if (candidates.Count == 0) continue;

            var chosen = candidates[0];
            var lifted = chosen.Other(centre);

            var volume = Volume(order, at, centre, lifted);
            if (Math.Abs(volume) < 1e-6) continue;

            // Anticlockwise from the first neighbour is a negative volume, this way round.
            var anticlockwise = atom.Chirality is "@" or "@TH1";
            wedges[chosen.Index] = ((volume < 0) == anticlockwise ? Wedge.Solid : Wedge.Hashed, centre);
        }

        return wedges;
    }

    /// <summary>
    /// The neighbours of a stereocentre in the order its handedness is read against, -1 standing for the hydrogen
    /// in its brackets — or, where there are only three, for the lone pair or hydrogen nobody wrote, in the same place.
    /// </summary>
    private static List<int> Order(MoleculeNode molecule, AtomNode atom)
    {
        var order = molecule.NeighboursAsWritten(atom.Index).ToList();
        var implied = atom.Hydrogens is 1 || (order.Count == 3 && atom.Hydrogens is null or 0);

        if (implied) order.Insert(molecule.Preceded(atom.Index) ? 1 : 0, -1);
        return order;
    }

    /// <summary>
    /// The signed volume of the centre's neighbours, taken in order, with <paramref name="lifted"/> out of the page
    /// and an undrawn neighbour opposite all the others.
    /// </summary>
    private static double Volume(List<int> order, IReadOnlyList<Vec> at, int centre, int lifted)
    {
        var vectors = new (double X, double Y, double Z)[order.Count];
        var sum = (X: 0.0, Y: 0.0, Z: 0.0);

        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] < 0) continue;
            var d = (at[order[i]] - at[centre]).Unit;
            vectors[i] = (d.X, d.Y, order[i] == lifted ? 1 : 0);
            sum = (sum.X + vectors[i].X, sum.Y + vectors[i].Y, sum.Z + vectors[i].Z);
        }

        for (var i = 0; i < order.Count; i++)
            if (order[i] < 0) vectors[i] = (-sum.X, -sum.Y, -sum.Z);

        var a = Minus(vectors[1], vectors[0]);
        var b = Minus(vectors[2], vectors[0]);
        var c = Minus(vectors[3], vectors[0]);
        return Determinant(a, b, c);
    }

    private static (double X, double Y, double Z) Minus((double X, double Y, double Z) p, (double X, double Y, double Z) q) =>
        (p.X - q.X, p.Y - q.Y, p.Z - q.Z);

    private static double Determinant((double X, double Y, double Z) a, (double X, double Y, double Z) b, (double X, double Y, double Z) c) =>
        a.X * (b.Y * c.Z - b.Z * c.Y) - a.Y * (b.X * c.Z - b.Z * c.X) + a.Z * (b.X * c.Y - b.Y * c.X);
}
