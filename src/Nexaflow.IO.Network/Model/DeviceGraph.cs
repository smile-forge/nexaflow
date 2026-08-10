namespace Nexaflow.IO.Network.Model;

/// <summary>
/// The correlated view of everything every probe has seen.
///
/// <para><b>The identity rule, and why it is what it is.</b> Layers name devices by different keys, so
/// identity is a lattice of claims rather than a primary key. An observation resolves each of its claims;
/// when they land on different nodes the graph must decide whether that means "same device". It resolves
/// that by preferring <i>edges over merges</i>, always:</para>
/// <list type="bullet">
///   <item><description><b>Two different MAC values never merge.</b> A router's LAN, WAN and WLAN
///   interfaces are three nodes joined by a <see cref="EdgeKind.SameDevice"/> edge. A merge is monotone
///   and effectively irreversible mid-session: a wrong one destroys history and points an action at
///   hardware the user did not mean. A wrong edge is cosmetic.</description></item>
///   <item><description><b>An IP seen with a different MAC is rebound, not merged.</b> The previous holder
///   keeps the address as a superseded fact and the live binding moves. DHCP churn must never fuse two
///   devices — that failure mode is silent and permanent.</description></item>
/// </list>
/// </summary>
public sealed class DeviceGraph
{
    private readonly Dictionary<string, DeviceNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeviceEdge> _edges = new(StringComparer.Ordinal);

    /// <summary>The identity history — what makes "new device" truthful and a rebind auditable.</summary>
    public IdentityLedger Ledger { get; } = new();

    public IReadOnlyCollection<DeviceNode> Nodes => _nodes.Values;
    public IReadOnlyCollection<DeviceEdge> Edges => _edges.Values;

    /// <summary>Resolves a node by id, following merge aliases so an id captured before a merge still works
    /// — an action or an audit record must never dangle because two nodes fused afterwards.</summary>
    public DeviceNode? Find(string nodeId)
    {
        var id = nodeId;
        for (int hops = 0; _aliases.TryGetValue(id, out var target) && hops < 32; hops++) id = target;
        return _nodes.TryGetValue(id, out var n) ? n : null;
    }

    /// <summary>
    /// Folds an observation in, creating, attaching, merging or rebinding as the rules above dictate.
    /// Returns the node it was attributed to, or null if the observation carried no identity at all.
    /// </summary>
    public DeviceNode? Observe(ProbeObservation obs)
    {
        if (obs.Identities.Count == 0) return null;
        var now = obs.ObservedUtc;

        // Rebinds must be settled BEFORE resolution, or the stale binding drags this observation onto the
        // previous holder of the address and the two devices fuse.
        ApplyRebinds(obs, now);

        var resolved = obs.Identities
            .Select(c => (Claim: c, NodeId: Ledger.Resolve(c)))
            .ToList();

        var candidates = resolved
            .Where(r => r.NodeId is not null)
            .Select(r => Find(r.NodeId!))
            .Where(n => n is not null)
            .Select(n => n!)
            .DistinctBy(n => n.Id)
            .ToList();

        DeviceNode node;
        bool brandNew = false;

        if (candidates.Count == 0)
        {
            node = Create(obs, now);
            brandNew = true;
        }
        else
        {
            node = ChoosePrimary(candidates);

            // Anything that resolved elsewhere either merges into the primary or, when merging would fuse
            // two distinct MACs, stays its own node joined by a SameDevice edge.
            foreach (var other in candidates.Where(c => !ReferenceEquals(c, node)))
            {
                if (WouldFuseDistinctMacs(node, other))
                    LinkSameDevice(node, other, obs.SourceProbe, now);
                else
                    Merge(into: node, from: other);
            }
        }

        foreach (var claim in obs.Identities)
        {
            // A claim already bound elsewhere and deliberately NOT merged (the distinct-MAC case) must keep
            // its own binding, or the next observation would drag the two nodes together after all.
            var existing = Ledger.Resolve(claim);
            if (existing is not null && Find(existing) is { } holder && !ReferenceEquals(holder, node)) continue;

            Ledger.Bind(claim, node.Id, now);
            if (!node.Identities.Any(i => i.Key == claim.Key)) node.Identities.Add(claim);
        }

        foreach (var fact in obs.Facts) AddFact(node, Stamp(fact, obs));

        node.LastSeenUtc = now;
        node.Presence = Presence.Present;
        if (brandNew) node.IsNew = true;

        foreach (var pending in obs.Edges) ResolveEdge(node, pending, obs.SourceProbe, now);

        return node;
    }

    // ── Rebinding ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Detects an address that has moved to different hardware and takes it away from its previous holder,
    /// superseding that node's fact rather than deleting it.
    /// </summary>
    private void ApplyRebinds(ProbeObservation obs, DateTimeOffset now)
    {
        var macClaim = obs.Identities.FirstOrDefault(c => c.Kind == IdentityKind.Mac);
        if (macClaim.Value is null or "") return;      // no hardware key: nothing authoritative to rebind against

        foreach (var ipClaim in obs.Identities.Where(c => c.Kind == IdentityKind.Ip))
        {
            if (Ledger.Resolve(ipClaim) is not { } holderId || Find(holderId) is not { } holder) continue;

            // Same hardware still holds it — an ordinary refresh, not a rebind.
            var holderMac = holder.Best(new FactKey("link", "mac"))?.Value.Text;
            if (holderMac is null || string.Equals(holderMac, macClaim.Value, StringComparison.OrdinalIgnoreCase))
                continue;

            for (int i = 0; i < holder.Facts.Count; i++)
            {
                var f = holder.Facts[i];
                if (f.SupersededUtc is null
                    && (f.Key.Equals(new FactKey("net", "ipv4")) || f.Key.Equals(new FactKey("net", "ipv6")))
                    && string.Equals(f.Value.Text, ipClaim.Value, StringComparison.OrdinalIgnoreCase))
                    holder.Facts[i] = f with { SupersededUtc = now };
            }

            holder.Identities.RemoveAll(c => c.Key == ipClaim.Key);
            Ledger.Retire(ipClaim, now);
        }
    }

    // ── Merge policy ──────────────────────────────────────────────────────────

    /// <summary>True when merging would put two different MAC values on one node — the case we refuse.</summary>
    private static bool WouldFuseDistinctMacs(DeviceNode a, DeviceNode b)
    {
        var macA = MacsOf(a);
        var macB = MacsOf(b);
        return macA.Count > 0 && macB.Count > 0 && !macA.Overlaps(macB);
    }

    private static HashSet<string> MacsOf(DeviceNode n)
        => n.Identities
            .Where(i => i.Kind == IdentityKind.Mac)
            .Select(i => i.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The node that survives a merge: the one with the strongest identity, then the most
    /// established. Deterministic, so replaying the same observations yields the same graph.</summary>
    private static DeviceNode ChoosePrimary(List<DeviceNode> candidates)
        => candidates
            .OrderByDescending(n => n.Identities.Any(i => i.Kind == IdentityKind.Mac))
            .ThenByDescending(n => n.Identities.Count)
            .ThenBy(n => n.FirstSeenUtc)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .First();

    private void Merge(DeviceNode into, DeviceNode from)
    {
        foreach (var c in from.Identities)
            if (!into.Identities.Any(i => i.Key == c.Key)) into.Identities.Add(c);

        into.Facts.AddRange(from.Facts);

        if (from.FirstSeenUtc < into.FirstSeenUtc) into.FirstSeenUtc = from.FirstSeenUtc;
        if (from.LastSeenUtc > into.LastSeenUtc) into.LastSeenUtc = from.LastSeenUtc;
        into.IsNew |= from.IsNew;

        // Edges follow the surviving node; a self-edge left by the merge is meaningless and dropped.
        foreach (var key in _edges.Keys.ToList())
        {
            var e = _edges[key];
            if (e.FromId != from.Id && e.ToId != from.Id) continue;
            _edges.Remove(key);

            var moved = e with
            {
                FromId = e.FromId == from.Id ? into.Id : e.FromId,
                ToId = e.ToId == from.Id ? into.Id : e.ToId,
            };
            if (moved.FromId != moved.ToId) _edges[moved.Key] = moved;
        }

        Ledger.Repoint(from.Id, into.Id);
        _nodes.Remove(from.Id);
        _aliases[from.Id] = into.Id;   // ids captured before the merge keep resolving
    }

    private void LinkSameDevice(DeviceNode a, DeviceNode b, string probe, DateTimeOffset now)
    {
        // Undirected in meaning, so record it once under a stable orientation rather than twice.
        var (from, to) = string.CompareOrdinal(a.Id, b.Id) <= 0 ? (a, b) : (b, a);
        AddEdge(new DeviceEdge(from.Id, to.Id, EdgeKind.SameDevice, probe, now, Confidence.Likely,
                               "shares an identity key"));
    }

    // ── Construction and facts ────────────────────────────────────────────────

    private DeviceNode Create(ProbeObservation obs, DateTimeOffset now)
    {
        // Id derives from the strongest available claim so it is stable across sessions — the device cache,
        // the audit log and any saved protocol binding all reference it.
        var seed = obs.Identities
            .OrderBy(c => c.Kind)          // Mac(0) < Uuid(1) < ServiceInstance(2) < Serial(3) < Hostname(4) < Ip(5)
            .ThenByDescending(c => c.Confidence)
            .ThenBy(c => c.Value, StringComparer.Ordinal)
            .First();

        var id = $"{seed.Kind.ToString().ToLowerInvariant()}:{seed.Value}";
        if (seed.Scope.Length > 0 && seed.Kind is IdentityKind.Ip or IdentityKind.Hostname)
            id += $"@{seed.Scope}";

        // Defensive: a retired-then-reused id would otherwise silently adopt the old node's history.
        if (_nodes.ContainsKey(id)) id = $"{id}#{_nodes.Count}";

        var node = new DeviceNode { Id = id, FirstSeenUtc = now, LastSeenUtc = now };
        _nodes[id] = node;
        return node;
    }

    private static DeviceFact Stamp(DeviceFact f, ProbeObservation obs)
    {
        var stamped = f;
        if (string.IsNullOrEmpty(stamped.SourceProbe)) stamped = stamped with { SourceProbe = obs.SourceProbe };
        if (stamped.ObservedUtc == default) stamped = stamped with { ObservedUtc = obs.ObservedUtc };
        if (string.IsNullOrEmpty(stamped.Layer))
            stamped = stamped with { Layer = FactOntology.Describe(stamped.Key).Layer };
        return stamped;
    }

    /// <summary>
    /// Appends a fact. The same probe re-asserting the same value refreshes in place rather than growing
    /// the list without bound; a <i>different</i> probe, or a different value, is kept alongside — that
    /// accumulation is the provenance the UI and the AI both read.
    /// </summary>
    private static void AddFact(DeviceNode node, DeviceFact fact)
    {
        for (int i = 0; i < node.Facts.Count; i++)
        {
            var f = node.Facts[i];
            if (f.SupersededUtc is null
                && f.Key.Equals(fact.Key)
                && string.Equals(f.SourceProbe, fact.SourceProbe, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.Value.Text, fact.Value.Text, StringComparison.Ordinal))
            {
                node.Facts[i] = f with { ObservedUtc = fact.ObservedUtc, Confidence = fact.Confidence };
                return;
            }
        }

        // A single-valued key re-asserted by the SAME probe with a DIFFERENT value is a change, not a
        // second opinion — supersede our own previous claim, but never another probe's.
        if (!FactOntology.Describe(fact.Key).MultiValued)
            for (int i = 0; i < node.Facts.Count; i++)
            {
                var f = node.Facts[i];
                if (f.SupersededUtc is null
                    && f.Key.Equals(fact.Key)
                    && string.Equals(f.SourceProbe, fact.SourceProbe, StringComparison.OrdinalIgnoreCase))
                    node.Facts[i] = f with { SupersededUtc = fact.ObservedUtc };
            }

        node.Facts.Add(fact);
    }

    private void ResolveEdge(DeviceNode from, ProbeObservation.PendingEdge pending, string probe, DateTimeOffset now)
    {
        var toId = Ledger.Resolve(pending.To);
        DeviceNode? to = toId is not null ? Find(toId) : null;

        if (to is null)
        {
            // The far end is real — a gateway we routed through, a switch that answered LLDP — even if no
            // probe has described it yet. Create a stub so the topology graph isn't full of holes.
            var stub = new ProbeObservation { SourceProbe = probe, ObservedUtc = now };
            stub.Identities.Add(pending.To);
            to = Observe(stub);
            if (to is null) return;
        }

        if (ReferenceEquals(to, from)) return;
        AddEdge(new DeviceEdge(from.Id, to.Id, pending.Kind, probe, now, pending.Confidence, pending.Label));
    }

    private void AddEdge(DeviceEdge edge)
    {
        // Re-observation refreshes; a weaker assertion never downgrades a stronger one.
        if (_edges.TryGetValue(edge.Key, out var existing) && existing.Confidence > edge.Confidence)
        {
            _edges[edge.Key] = existing with { ObservedUtc = edge.ObservedUtc };
            return;
        }
        _edges[edge.Key] = edge;
    }

    // ── Staleness ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Marks everything not re-confirmed since <paramref name="since"/> as <see cref="Presence.Stale"/>.
    /// Called right after loading the cache so the page renders instantly with honest staleness rather
    /// than presenting yesterday's list as live.
    /// </summary>
    public void MarkStaleBefore(DateTimeOffset since)
    {
        foreach (var n in _nodes.Values)
            if (n.LastSeenUtc < since && n.Presence == Presence.Present)
                n.Presence = Presence.Stale;
    }

    /// <summary>
    /// Demotes nodes a full sweep failed to find. <b>Never deletes</b> — a sleeping laptop must not
    /// re-announce itself as a new device tomorrow, and "what's new on my network" is only meaningful if
    /// absence is remembered.
    /// </summary>
    public void MarkAbsentExcept(IEnumerable<string> seenNodeIds, DateTimeOffset now)
    {
        var seen = seenNodeIds.ToHashSet(StringComparer.Ordinal);
        foreach (var n in _nodes.Values)
            if (!seen.Contains(n.Id) && n.LastSeenUtc < now)
                n.Presence = Presence.Absent;
    }
}
