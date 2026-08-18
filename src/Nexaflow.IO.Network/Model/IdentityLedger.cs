namespace Nexaflow.IO.Network.Model;

/// <summary>One identity key's history: which node it names, since when, and whether it has been retired.</summary>
public sealed class LedgerEntry
{
    public required string Key { get; init; }
    public required string NodeId { get; set; }
    public required DateTimeOffset FirstSeenUtc { get; init; }
    public DateTimeOffset LastSeenUtc { get; set; }

    /// <summary>Set when the key was taken away from <see cref="NodeId"/> — a DHCP address handed to a
    /// different device. The row is kept so the rebind is auditable rather than a silent overwrite.</summary>
    public DateTimeOffset? RetiredUtc { get; set; }
}

/// <summary>
/// The record of which identity key has named which node, and when.
///
/// <para>
/// This is what makes "a new device appeared on your network" a truthful claim rather than a cache miss:
/// a key that has never been seen before is genuinely new, whereas a key we retired and re-issued is a
/// rebind. Without the ledger those two are indistinguishable, and the alert becomes noise.
/// </para>
/// </summary>
public sealed class IdentityLedger
{
    private readonly Dictionary<string, LedgerEntry> _live = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LedgerEntry> _retired = [];

    /// <summary>The node currently named by <paramref name="claim"/>, or null.</summary>
    public string? Resolve(IdentityClaim claim)
        => _live.TryGetValue(claim.Key, out var e) ? e.NodeId : null;

    /// <summary>True if this key has never been seen live or retired — i.e. genuinely new.</summary>
    public bool IsUnknown(IdentityClaim claim)
        => !_live.ContainsKey(claim.Key)
           && !_retired.Any(r => string.Equals(r.Key, claim.Key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Binds <paramref name="claim"/> to <paramref name="nodeId"/>, or refreshes an existing
    /// binding's last-seen. Returns true if this created a new binding.</summary>
    public bool Bind(IdentityClaim claim, string nodeId, DateTimeOffset nowUtc)
    {
        if (_live.TryGetValue(claim.Key, out var e))
        {
            e.LastSeenUtc = nowUtc;
            e.NodeId = nodeId;
            return false;
        }

        _live[claim.Key] = new LedgerEntry
        {
            Key = claim.Key, NodeId = nodeId,
            FirstSeenUtc = nowUtc, LastSeenUtc = nowUtc,
        };
        return true;
    }

    /// <summary>Takes the key away from whatever node held it, keeping the row as history. Used when an IP
    /// moves to a different MAC — never a delete, so the old binding stays visible.</summary>
    public void Retire(IdentityClaim claim, DateTimeOffset nowUtc)
    {
        if (!_live.Remove(claim.Key, out var e)) return;
        e.RetiredUtc = nowUtc;
        _retired.Add(e);
    }

    /// <summary>Re-points every key currently naming <paramref name="fromNodeId"/> at
    /// <paramref name="toNodeId"/>. Called when two nodes merge.</summary>
    public void Repoint(string fromNodeId, string toNodeId)
    {
        foreach (var e in _live.Values)
            if (string.Equals(e.NodeId, fromNodeId, StringComparison.Ordinal))
                e.NodeId = toNodeId;
    }

    /// <summary>Live bindings, for persistence and the audit view.</summary>
    public IReadOnlyCollection<LedgerEntry> Live => _live.Values;

    /// <summary>Retired bindings — the rebind history.</summary>
    public IReadOnlyList<LedgerEntry> Retired => _retired;
}
