using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Chemistry.Stages;

/// <summary>
/// Gives each aromatic ring its alternating double bonds, and says where a ring cannot have them.
///
/// <para>
/// <c>c1ccccc1</c> says six atoms share a ring of electrons and not where the double bonds go, and a structure is
/// drawn with double bonds in it. Every aromatic atom with a bond to spare needs exactly one double bond to another
/// such atom across a ring bond — which is a perfect matching over those atoms, and the question has an answer
/// exactly when the ring was written as something that can exist.
/// </para>
/// <para>
/// <b>A matching, not a walk round the ring.</b> Fused systems share atoms between rings, five-membered rings make
/// the graph impossible to two-colour, and a greedy pass that alternates as it goes paints itself into a corner on
/// naphthalene written the wrong way round. Edmonds' algorithm answers it for any graph, in time nobody notices.
/// </para>
/// <para>
/// Only ring bonds carry a double bond. The bond between two aromatic rings written side by side —
/// <c>c1ccccc1c1ccccc1</c> — is aromatic by the letter of SMILES and single by chemistry, and letting the matching
/// use it draws biphenyl as a quinone.
/// </para>
/// </summary>
public sealed class Kekulize : IAstStage
{
    public string Name => "smiles:kekule";

    public ContentNode Run(ContentNode tree) => SmilesRewrite.Molecules(tree, Alternate);

    private static ContentNode Alternate(ContentNode node)
    {
        var molecule = Molecule.Read(node);
        var atoms = molecule.Atoms;
        if (!atoms.Any(atom => atom.Aromatic)) return node;

        var ring = MoleculeRings.RingBonds(molecule);
        var said = new Dictionary<int, (int Partner, string? Trouble)>();

        // Who needs a double bond: an aromatic atom whose element, at its charge, has a bond left over once its
        // written bonds and hydrogens are counted.
        var needs = new bool[atoms.Count];
        foreach (var atom in atoms)
        {
            if (!atom.Aromatic) continue;

            if (!molecule.BondsAt(atom.Index).Any(bond => ring[bond]))
            {
                said[atom.Index] = (-1, "An aromatic atom has to be in a ring.");
                continue;
            }

            var used = molecule.Valence(atom.Index) + atom.TotalHydrogens;
            var allowed = Elements.Allowed(atom.Number, atom.Charge);
            needs[atom.Index] = allowed is null
                ? atom.Number != 0 && used < 4
                : Elements.Fitting(allowed, used) is { } valence && valence > used;
        }

        var neighbours = new List<int>[atoms.Count];
        for (var i = 0; i < atoms.Count; i++) neighbours[i] = [];

        foreach (var bond in molecule.Bonds)
        {
            if (bond.Order != BondOrder.Aromatic || !ring[bond.Index]) continue;
            if (!needs[bond.From] || !needs[bond.To]) continue;

            neighbours[bond.From].Add(bond.To);
            neighbours[bond.To].Add(bond.From);
        }

        var match = Matching.Maximum(neighbours);

        for (var i = 0; i < atoms.Count; i++)
        {
            if (!needs[i]) continue;

            said[i] = match[i] >= 0
                ? (match[i], null)
                : (-1, Unmatched(molecule));
        }

        if (said.Count == 0) return node;

        return SmilesRewrite.Atoms(node, (index, atom) =>
        {
            if (!said.TryGetValue(index, out var fact)) return atom;

            var told = fact.Partner >= 0
                ? atom.Saying(SmilesKinds.Fact, SmilesRoles.DoubleTo, fact.Partner.ToString())
                : atom;

            return SmilesRewrite.Troubled(told, fact.Trouble);
        });
    }

    /// <summary>
    /// Why an atom was left without its double bond — with the fix, where the molecule has the usual culprit: a
    /// five-membered ring's nitrogen written <c>n</c> that carries a hydrogen nobody wrote. Which atom of an odd ring the
    /// matching leaves over is its own business, so the hint is offered wherever such a nitrogen is.
    /// </summary>
    private static string Unmatched(Molecule molecule)
    {
        const string said = "This ring cannot be drawn with alternating double bonds.";

        var culprit = molecule.Atoms.Any(atom => atom is { Number: 7, Aromatic: true, Charge: 0, Bracketed: false }
                                                 && molecule.Valence(atom.Index) == 2);

        return culprit ? $"{said} If a nitrogen in it carries a hydrogen, write it [nH]." : said;
    }
}

/// <summary>
/// Edmonds' blossom algorithm: the largest set of edges, no two sharing a vertex, in a graph that need not be
/// bipartite.
/// </summary>
internal static class Matching
{
    /// <summary>For each vertex, the vertex it is matched to, or -1.</summary>
    public static int[] Maximum(IReadOnlyList<List<int>> graph)
    {
        var n = graph.Count;
        var match = new int[n];
        Array.Fill(match, -1);

        // A greedy start leaves the search only the vertices it could not pair, which on a real molecule is few.
        for (var v = 0; v < n; v++)
        {
            if (match[v] >= 0) continue;
            foreach (var to in graph[v])
            {
                if (match[to] >= 0) continue;
                match[v] = to;
                match[to] = v;
                break;
            }
        }

        var parent = new int[n];
        var @base = new int[n];
        var used = new bool[n];
        var blossom = new bool[n];
        var queue = new Queue<int>();

        for (var root = 0; root < n; root++)
        {
            if (match[root] >= 0 || graph[root].Count == 0) continue;

            var end = FindPath(root);
            while (end >= 0)
            {
                var previous = parent[end];
                var next = match[previous];
                match[end] = previous;
                match[previous] = end;
                end = next;
            }
        }

        return match;

        int FindPath(int root)
        {
            Array.Fill(used, false);
            Array.Fill(parent, -1);
            for (var i = 0; i < n; i++) @base[i] = i;

            used[root] = true;
            queue.Clear();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var v = queue.Dequeue();

                foreach (var to in graph[v])
                {
                    if (@base[v] == @base[to] || match[v] == to) continue;

                    if (to == root || (match[to] >= 0 && parent[match[to]] >= 0))
                    {
                        var shared = Ancestor(v, to);
                        Array.Fill(blossom, false);
                        Mark(v, shared, to);
                        Mark(to, shared, v);

                        for (var i = 0; i < n; i++)
                        {
                            if (!blossom[@base[i]]) continue;
                            @base[i] = shared;
                            if (used[i]) continue;
                            used[i] = true;
                            queue.Enqueue(i);
                        }
                    }
                    else if (parent[to] < 0)
                    {
                        parent[to] = v;
                        if (match[to] < 0) return to;

                        used[match[to]] = true;
                        queue.Enqueue(match[to]);
                    }
                }
            }

            return -1;
        }

        int Ancestor(int a, int b)
        {
            var seen = new bool[n];
            while (true)
            {
                a = @base[a];
                seen[a] = true;
                if (match[a] < 0) break;
                a = parent[match[a]];
            }

            while (true)
            {
                b = @base[b];
                if (seen[b]) return b;
                b = parent[match[b]];
            }
        }

        void Mark(int v, int shared, int child)
        {
            while (@base[v] != shared)
            {
                blossom[@base[v]] = blossom[@base[match[v]]] = true;
                parent[v] = child;
                child = match[v];
                v = parent[match[v]];
            }
        }
    }
}
