namespace Nexaflow.Markdown.Chemistry.Depiction;

/// <summary>
/// Pulls apart atoms a layout put on top of one another, by moving the smaller side of a single bond between them.
///
/// <para>
/// Growing a molecule one atom at a time decides each angle knowing only what is already down, so a long branch
/// can come round and land on something placed earlier. What fixes it is nearly always a turn at some single bond on
/// the way from one of the two atoms to the other: the smaller half of the molecule turned over the bond, or failing
/// that swung round it. Every candidate is scored by how crowded it leaves the page, and the best is kept only if it
/// is better than doing nothing.
/// </para>
/// </summary>
internal sealed class Untangler(MoleculeNode molecule, List<int> component, Vec[] at, double?[] depth)
{
    /// <summary>Two atoms closer than this, in bond lengths, are on top of each other.</summary>
    private const double TooClose = 0.5;

    private const int Passes = 12;

    private static readonly double[] Swings = [Math.PI / 6, -Math.PI / 6, Math.PI / 3, -Math.PI / 3, Math.PI / 2, -Math.PI / 2];

    private readonly bool[] _ring = MoleculeRings.RingBonds(molecule);
    private readonly HashSet<int> _members = [.. component];

    public void Untangle()
    {
        if (component.Count < 4) return;

        for (var pass = 0; pass < Passes; pass++)
        {
            var clashes = Clashes();
            if (clashes.Count == 0) return;

            var moved = false;
            foreach (var (a, b) in clashes)
            {
                if (Vec.Distance(at[a], at[b]) >= TooClose) continue;
                moved |= Separate(a, b);
            }

            if (!moved) return;
        }
    }

    private List<(int A, int B)> Clashes()
    {
        var clashes = new List<(int, int, double)>();

        for (var i = 0; i < component.Count; i++)
            for (var j = i + 1; j < component.Count; j++)
            {
                var (a, b) = (component[i], component[j]);
                var d = Vec.Distance(at[a], at[b]);
                if (d < TooClose && molecule.Between(a, b) is null) clashes.Add((a, b, d));
            }

        return [.. clashes.OrderBy(c => c.Item3).Select(c => (c.Item1, c.Item2))];
    }

    /// <summary>Tries every turn on the way from one atom to the other, and keeps the best if it helps.</summary>
    private bool Separate(int a, int b)
    {
        var path = Path(a, b);
        if (path is null) return false;

        (double Gain, int[] Side, Vec[] To, bool Mirrors)? best = null;

        foreach (var swing in new[] { false, true })
        {
            for (var i = 0; i + 1 < path.Count; i++)
            {
                var bond = molecule.Between(path[i], path[i + 1])!;
                if (_ring[bond.Index] || bond.Order != BondOrder.Single) continue;

                var (pivot, side) = Smaller(path[i], path[i + 1]);
                if (side.Length == 0) continue;

                var axisFrom = at[pivot];
                var axisTo = at[bond.Other(pivot)];

                var moves = swing
                    ? Swings.Select(angle => side.Select(atom => axisFrom + (at[atom] - axisFrom).Rotated(angle)).ToArray())
                    : [side.Select(atom => at[atom].Reflected(axisFrom, axisTo - axisFrom)).ToArray()];

                foreach (var to in moves)
                {
                    var gain = Crowding(side, side.Select(atom => at[atom]).ToArray()) - Crowding(side, to);
                    if (gain > 1e-6 && (best is null || gain > best.Value.Gain)) best = (gain, side, to, !swing);
                }
            }

            // A reflection keeps every angle a chemist would draw; a swing does not, so it is only reached for when
            // no reflection helped.
            if (best is not null) break;
        }

        if (best is not { } chosen) return false;

        // A solid turned over on the page is the same solid seen from behind, so what was nearest is now furthest.
        for (var k = 0; k < chosen.Side.Length; k++)
        {
            at[chosen.Side[k]] = chosen.To[k];
            if (chosen.Mirrors && depth[chosen.Side[k]] is { } near) depth[chosen.Side[k]] = -near;
        }
        return true;
    }

    /// <summary>
    /// The smaller of the two sides of the bond between <paramref name="x"/> and <paramref name="y"/>, and the atom at
    /// the other end of the bond, which is what it turns about.
    /// </summary>
    private (int Pivot, int[] Side) Smaller(int x, int y)
    {
        var fromY = Reach(y, x);
        var fromX = Reach(x, y);
        return fromY.Count <= fromX.Count ? (x, [.. fromY]) : (y, [.. fromX]);
    }

    /// <summary>Every atom reached from <paramref name="start"/> without crossing to <paramref name="avoid"/>.</summary>
    private List<int> Reach(int start, int avoid)
    {
        var seen = new HashSet<int> { start, avoid };
        var reached = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var atom = queue.Dequeue();
            reached.Add(atom);
            foreach (var bond in molecule.BondsAt(atom))
            {
                var other = molecule.Bonds[bond].Other(atom);
                if (seen.Add(other)) queue.Enqueue(other);
            }
        }

        return reached;
    }

    /// <summary>How crowded the atoms of <paramref name="side"/> at <paramref name="positions"/> are by everything not in it.</summary>
    private double Crowding(int[] side, Vec[] positions)
    {
        var moving = new HashSet<int>(side);
        var sum = 0.0;

        for (var k = 0; k < side.Length; k++)
            foreach (var other in component)
            {
                if (moving.Contains(other)) continue;
                var d = Vec.Distance(positions[k], at[other]);
                if (d < 2.5) sum += 1 / (d * d + 0.01);
            }

        return sum;
    }

    private List<int>? Path(int from, int to)
    {
        var previous = new Dictionary<int, int> { [from] = -1 };
        var queue = new Queue<int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var atom = queue.Dequeue();
            if (atom == to) break;

            foreach (var bond in molecule.BondsAt(atom))
            {
                var other = molecule.Bonds[bond].Other(atom);
                if (!_members.Contains(other) || previous.ContainsKey(other)) continue;
                previous[other] = atom;
                queue.Enqueue(other);
            }
        }

        if (!previous.ContainsKey(to)) return null;

        var path = new List<int>();
        for (var atom = to; atom >= 0; atom = previous[atom]) path.Add(atom);
        path.Reverse();
        return path;
    }
}
