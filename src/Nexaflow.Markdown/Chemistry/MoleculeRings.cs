namespace Nexaflow.Markdown.Chemistry;

/// <summary>
/// Which bonds are in rings, and which rings a molecule is made of.
///
/// <para>
/// <b>A ring bond is a bond that is not a bridge</b> — one whose removal leaves its two atoms still connected.
/// That needs no ring to be found first, and it is all <see cref="Stages.Kekulize"/> asks.
/// </para>
/// <para>
/// <b>The rings are the smallest set of smallest rings</b>: as many as the ring bonds need
/// (bonds − atoms + pieces), each as short as it can be, none made of the others. It is what a chemist means by
/// "the rings of naphthalene" — two hexagons, not the decagon round the outside — and what a drawing is built out
/// of. Found by Horton's method: every cycle made of two shortest paths from one atom and the bond joining their
/// ends is a candidate, and the shortest candidates that are independent of each other, counted over the bonds
/// they use, are the set.
/// </para>
/// </summary>
internal static class MoleculeRings
{
    /// <summary>For each bond, whether it is in a ring.</summary>
    public static bool[] RingBonds(MoleculeNode molecule)
    {
        var atoms = molecule.Atoms.Count;
        var bonds = molecule.Bonds.Count;
        var ring = new bool[bonds];
        Array.Fill(ring, true);

        var order = new int[atoms];
        var low = new int[atoms];
        Array.Fill(order, -1);
        var counter = 0;

        // Tarjan's bridges, walked with a stack of its own: a polymer written as one long chain is deeper than a
        // thread's stack should be asked to go.
        var stack = new Stack<(int Atom, int Via, int Next)>();

        for (var root = 0; root < atoms; root++)
        {
            if (order[root] >= 0) continue;

            order[root] = low[root] = counter++;
            stack.Push((root, -1, 0));

            while (stack.Count > 0)
            {
                var (atom, via, next) = stack.Pop();
                var around = molecule.BondsAt(atom);

                if (next < around.Count)
                {
                    stack.Push((atom, via, next + 1));

                    var bond = around[next];
                    if (bond == via) continue;

                    var other = molecule.Bonds[bond].Other(atom);
                    if (order[other] < 0)
                    {
                        order[other] = low[other] = counter++;
                        stack.Push((other, bond, 0));
                    }
                    else
                    {
                        low[atom] = Math.Min(low[atom], order[other]);
                    }

                    continue;
                }

                if (via < 0) continue;

                var parent = molecule.Bonds[via].Other(atom);
                low[parent] = Math.Min(low[parent], low[atom]);
                if (low[atom] > order[parent]) ring[via] = false;
            }
        }

        return ring;
    }

    /// <summary>The smallest set of smallest rings, each as its atoms in order round it.</summary>
    public static IReadOnlyList<int[]> Smallest(MoleculeNode molecule)
    {
        var ring = RingBonds(molecule);
        var rings = new List<int[]>();

        foreach (var system in Systems(molecule, ring))
            rings.AddRange(Smallest(molecule, ring, system));

        return rings;
    }

    /// <summary>The atoms joined to one another by ring bonds, a set per connected piece of them.</summary>
    private static List<List<int>> Systems(MoleculeNode molecule, bool[] ring)
    {
        var atoms = molecule.Atoms.Count;
        var seen = new bool[atoms];
        var systems = new List<List<int>>();

        for (var start = 0; start < atoms; start++)
        {
            if (seen[start] || !molecule.BondsAt(start).Any(bond => ring[bond])) continue;

            var system = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;

            while (queue.Count > 0)
            {
                var atom = queue.Dequeue();
                system.Add(atom);

                foreach (var bond in molecule.BondsAt(atom))
                {
                    if (!ring[bond]) continue;
                    var other = molecule.Bonds[bond].Other(atom);
                    if (seen[other]) continue;
                    seen[other] = true;
                    queue.Enqueue(other);
                }
            }

            systems.Add(system);
        }

        return systems;
    }

    private static IEnumerable<int[]> Smallest(MoleculeNode molecule, bool[] ring, List<int> system)
    {
        var local = new Dictionary<int, int>();
        for (var i = 0; i < system.Count; i++) local[system[i]] = i;

        var edges = new List<(int A, int B)>();
        var edgeOf = new Dictionary<(int, int), int>();
        foreach (var bond in molecule.Bonds)
        {
            if (!ring[bond.Index] || !local.ContainsKey(bond.From)) continue;
            var key = Key(local[bond.From], local[bond.To]);
            edgeOf[key] = edges.Count;
            edges.Add(key);
        }

        var n = system.Count;
        var need = edges.Count - n + 1;
        if (need <= 0) yield break;

        var around = new List<int>[n];
        for (var i = 0; i < n; i++) around[i] = [];
        foreach (var (a, b) in edges)
        {
            around[a].Add(b);
            around[b].Add(a);
        }

        // A shortest-path tree from every atom: its parent towards the root, and how far.
        var parent = new int[n][];
        var depth = new int[n][];
        for (var root = 0; root < n; root++)
        {
            parent[root] = new int[n];
            depth[root] = new int[n];
            Array.Fill(depth[root], -1);
            parent[root][root] = -1;
            depth[root][root] = 0;

            var queue = new Queue<int>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var v = queue.Dequeue();
                foreach (var to in around[v])
                {
                    if (depth[root][to] >= 0) continue;
                    depth[root][to] = depth[root][v] + 1;
                    parent[root][to] = v;
                    queue.Enqueue(to);
                }
            }
        }

        var candidates = new List<int[]>();
        var seen = new HashSet<string>();

        for (var root = 0; root < n; root++)
        {
            foreach (var (a, b) in edges)
            {
                var da = depth[root][a];
                var db = depth[root][b];
                if (Math.Abs(da - db) > 1) continue;

                var left = PathTo(parent[root], a);
                var right = PathTo(parent[root], b);

                // The two paths may share only the root, or the cycle folds back on itself.
                if (left.Skip(1).Intersect(right.Skip(1)).Any()) continue;
                if (left.Count > 1 && right.Count > 1 && left[1] == right[1]) continue;

                var cycle = new List<int>(left.Count + right.Count - 1);
                cycle.AddRange(left);
                for (var i = right.Count - 1; i >= 1; i--) cycle.Add(right[i]);
                if (cycle.Count < 3) continue;

                var name = string.Join(",", cycle.Order());
                if (seen.Add(name)) candidates.Add([.. cycle]);
            }
        }

        // Shortest first, and among equals the one met first, so the answer does not depend on hashing.
        candidates.Sort((x, y) => x.Length.CompareTo(y.Length));

        var words = (edges.Count + 63) / 64;
        var basis = new List<ulong[]>();
        var pivots = new List<int>();
        var found = 0;

        foreach (var cycle in candidates)
        {
            var vector = new ulong[words];
            for (var i = 0; i < cycle.Length; i++)
            {
                var edge = edgeOf[Key(cycle[i], cycle[(i + 1) % cycle.Length])];
                vector[edge >> 6] ^= 1UL << (edge & 63);
            }

            if (!Independent(vector, basis, pivots)) continue;

            yield return [.. cycle.Select(atom => system[atom])];
            if (++found == need) yield break;
        }
    }

    /// <summary>From <paramref name="to"/> back to the root, root first.</summary>
    private static List<int> PathTo(int[] parent, int to)
    {
        var path = new List<int>();
        for (var at = to; at >= 0; at = parent[at]) path.Add(at);
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Whether <paramref name="vector"/> is not a sum of the basis — and, when it is not, adds it. Elimination over
    /// two elements, each basis vector kept reduced against those before it.
    /// </summary>
    private static bool Independent(ulong[] vector, List<ulong[]> basis, List<int> pivots)
    {
        for (var i = 0; i < basis.Count; i++)
        {
            var pivot = pivots[i];
            if ((vector[pivot >> 6] & (1UL << (pivot & 63))) == 0) continue;
            for (var w = 0; w < vector.Length; w++) vector[w] ^= basis[i][w];
        }

        for (var w = 0; w < vector.Length; w++)
        {
            if (vector[w] == 0) continue;
            pivots.Add(w * 64 + System.Numerics.BitOperations.TrailingZeroCount(vector[w]));
            basis.Add(vector);
            return true;
        }

        return false;
    }

    private static (int, int) Key(int a, int b) => (Math.Min(a, b), Math.Max(a, b));
}
