namespace Nexaflow.IO.Protocol.Resolution;

/// <summary>
/// Settles every facet of every node, or explains precisely why it could not.
///
/// <para><b>Demand-driven, not a rescan.</b> A blocked facet registers a waiter against the specific
/// prerequisite it is missing, and settling a facet wakes exactly the waiters on it. The first design
/// rescanned every node every pass — O(passes × nodes) — and measured 61 passes on the worst corpus
/// protocol. This is O(edges), and the pass count stops being a number anyone has to think about.</para>
///
/// <para><b>Expansion is an action.</b> Settling <see cref="Facet.Realised"/> may bring new nodes into
/// existence, and termination requires that nothing is left unrealised. Without that, the terminal
/// condition "everything emitted" is satisfied by a node set that is simply too small — which is how the
/// previous design would emit one short, structurally valid, wrong message and report success.</para>
///
/// <para><b>Position is not here.</b> Position is a linearisation, not a fixpoint: one ordered sweep once
/// extents are settled. Keeping it in the worklist was most of the measured pass count and bought nothing.</para>
/// </summary>
public sealed class Resolver
{
    private readonly Dictionary<object, ResolutionNode> _nodes = [];
    private readonly Dictionary<FacetRef, object?> _settled = [];
    private readonly Dictionary<FacetRef, List<FacetRef>> _waiters = [];
    private readonly HashSet<FacetRef> _inProgress = [];
    private readonly Queue<FacetRef> _ready = new();
    private readonly List<string> _trace = [];

    /// <summary>Every facet settled, in the order it settled. The dry-run breakdown reads this, and so does
    /// anyone trying to understand why a document behaved as it did.</summary>
    public IReadOnlyList<string> Trace => _trace;

    /// <summary>How many facets were settled. Not a pass count — there are no passes.</summary>
    public int SettledCount => _settled.Count;

    // Not an order. The worklist is demand-driven and each node declares its own edges; this is only the
    // set of questions that may be asked, and a facet missing from it is one nothing can ever depend on.
    private static readonly Facet[] AllFacets =
        [Facet.Realised, Facet.Present, Facet.Extent, Facet.Position, Facet.Value, Facet.Emitted];

    public Resolver Add(params ResolutionNode[] nodes)
    {
        foreach (var n in nodes) Register(n);
        return this;
    }

    private void Register(ResolutionNode n)
    {
        if (!_nodes.TryAdd(n.Id, n))
            throw new InvalidOperationException($"duplicate resolution node '{n.Id}'");

        foreach (var facet in AllFacets)
        {
            var reference = new FacetRef(n.Id, facet);
            if (n.NotApplicable.Contains(facet)) { _settled[reference] = null; continue; }
            Enqueue(reference);
        }
    }

    /// <summary>Runs to a fixed point. Throws <see cref="ResolutionException"/> naming which of the five
    /// failure kinds occurred and what stayed blocked.</summary>
    public IReadOnlyDictionary<FacetRef, object?> Resolve()
    {
        while (_ready.Count > 0)
        {
            var reference = _ready.Dequeue();
            if (_settled.ContainsKey(reference)) continue;

            var node = _nodes[reference.Node];
            var needs = node.DependenciesFor(reference.Facet);

            // Park against the FIRST unmet prerequisite. Waking on that one prerequisite is enough:
            // whichever is settled last is the one that unblocks us, and re-parking is cheap.
            var missing = needs.FirstOrDefault(d => !_settled.ContainsKey(d));
            if (missing != default && !_settled.ContainsKey(missing))
            {
                if (!_nodes.ContainsKey(missing.Node) && !_settled.ContainsKey(missing))
                {
                    // The prerequisite names a node that does not exist yet. Legitimate while something is
                    // still unrealised; a hard error once expansion has finished.
                    Park(missing, reference);
                    continue;
                }
                Park(missing, reference);
                continue;
            }

            if (!_inProgress.Add(reference))
                throw Fail(ResolutionFailure.Cycle, [reference],
                    $"{reference} is required in order to settle itself");

            var inputs = needs.ToDictionary(d => d, d => _settled[d]);
            var result = node.Settle(reference.Facet, inputs);
            _inProgress.Remove(reference);

            Settle(reference, result.Value);

            if (result.Realises is { Count: > 0 })
                foreach (var child in result.Realises) Register(child);
        }

        Verify();
        return _settled;
    }

    private void Park(FacetRef prerequisite, FacetRef blocked)
    {
        if (!_waiters.TryGetValue(prerequisite, out var list)) _waiters[prerequisite] = list = [];
        if (!list.Contains(blocked)) list.Add(blocked);
    }

    private void Settle(FacetRef reference, object? value)
    {
        _settled[reference] = value;
        _trace.Add($"{reference} = {value ?? "—"}");

        if (!_waiters.Remove(reference, out var woken)) return;
        foreach (var w in woken) Enqueue(w);
    }

    private void Enqueue(FacetRef reference)
    {
        if (!_settled.ContainsKey(reference)) _ready.Enqueue(reference);
    }

    /// <summary>
    /// Classifies a stall. The distinction that matters most is between a genuine cycle and everything
    /// else — a document author sent looking for a loop that does not exist will not find one.
    /// </summary>
    private void Verify()
    {
        var blocked = _nodes.Values
            .SelectMany(n => AllFacets.Select(f => new FacetRef(n.Id, f)))
            .Where(r => !_settled.ContainsKey(r))
            .ToList();

        if (blocked.Count == 0) return;

        // A prerequisite naming a node that never came into existence is under-expansion, not a cycle.
        var phantom = blocked
            .SelectMany(r => _nodes[r.Node].DependenciesFor(r.Facet))
            .Where(d => !_nodes.ContainsKey(d.Node))
            .Distinct()
            .ToList();

        if (phantom.Count > 0)
            throw Fail(ResolutionFailure.Unrealised, blocked,
                $"these prerequisites name nodes that were never realised: {string.Join(", ", phantom)}. "
              + "A repetition, choice or optional did not expand — which would otherwise emit a short but "
              + "structurally valid message and report success.");

        if (FindCycle(blocked) is { Count: > 0 } cycle)
            throw Fail(ResolutionFailure.Cycle, cycle,
                "these facets depend on each other: " + string.Join(" → ", cycle) + $" → {cycle[0]}");

        throw Fail(ResolutionFailure.Unsatisfiable, blocked,
            "no cycle exists and every prerequisite names a real node, so a dependency cannot yield the "
          + "facet asked of it. Check that each blocked node declares a way to settle the facet listed.");
    }

    /// <summary>A cycle among the blocked facets, or null. Iterative depth-first — the graph is a document's
    /// and could be deep.</summary>
    private List<FacetRef>? FindCycle(List<FacetRef> blocked)
    {
        var onPath = new HashSet<FacetRef>();
        var done = new HashSet<FacetRef>();

        foreach (var start in blocked)
        {
            var found = Walk(start, onPath, done, []);
            if (found is not null) return found;
        }
        return null;

        List<FacetRef>? Walk(FacetRef at, HashSet<FacetRef> path, HashSet<FacetRef> finished, List<FacetRef> stack)
        {
            if (path.Contains(at)) return [.. stack.SkipWhile(x => !x.Equals(at))];
            if (!finished.Add(at) || !_nodes.ContainsKey(at.Node)) return null;

            path.Add(at);
            stack.Add(at);

            foreach (var next in _nodes[at.Node].DependenciesFor(at.Facet))
                if (!_settled.ContainsKey(next))
                {
                    var found = Walk(next, path, finished, stack);
                    if (found is not null) return found;
                }

            path.Remove(at);
            stack.RemoveAt(stack.Count - 1);
            return null;
        }
    }

    private static ResolutionException Fail(ResolutionFailure kind, IReadOnlyList<FacetRef> blocked, string why)
        => new(new ResolutionDiagnostic(kind, blocked, why));
}
