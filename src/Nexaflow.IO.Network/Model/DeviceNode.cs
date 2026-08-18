namespace Nexaflow.IO.Network.Model;

/// <summary>Whether we currently believe the device is there.</summary>
public enum Presence
{
    /// <summary>Confirmed this session by a probe that actually reached it.</summary>
    Present = 0,

    /// <summary>Loaded from cache and not yet re-confirmed, or last seen longer ago than its TTL.</summary>
    Stale = 1,

    /// <summary>Actively looked for and not found. <b>Never deleted</b> — a laptop that sleeps must not
    /// re-announce itself as a new device every morning, and the "what's new on my network" claim is only
    /// truthful if absence is remembered.</summary>
    Absent = 2,
}

/// <summary>
/// One device in the graph: a stable id, the identity claims that resolve to it, and the append-only
/// facts asserted about it by every layer.
/// </summary>
public sealed class DeviceNode
{
    /// <summary>Stable, opaque id. Actions, protocol runs and the audit log all reference this, so it must
    /// survive a merge — see <see cref="DeviceGraph"/>, which keeps merged ids as aliases.</summary>
    public required string Id { get; init; }

    /// <summary>Every identity claim currently resolving to this node.</summary>
    public List<IdentityClaim> Identities { get; } = [];

    /// <summary>Every fact ever asserted, including superseded and conflicting ones. Append-only.</summary>
    public List<DeviceFact> Facts { get; } = [];

    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public Presence Presence { get; set; } = Presence.Present;

    /// <summary>True until the user has acknowledged it — drives the "new device on your network" alert.
    /// Set once, on genuine first creation, never on a cache reload.</summary>
    public bool IsNew { get; set; }

    /// <summary>
    /// The winning value for <paramref name="key"/>, or null if never asserted. Resolution is confidence,
    /// then recency, then probe id for determinism — so a rerun with the same data yields the same answer.
    /// Superseded facts are excluded.
    /// </summary>
    public DeviceFact? Best(FactKey key)
        => Facts
            .Where(f => f.Key.Equals(key) && f.SupersededUtc is null)
            .OrderByDescending(f => f.Confidence)
            .ThenByDescending(f => f.ObservedUtc)
            .ThenBy(f => f.SourceProbe, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>Every live (non-superseded) value for a multi-valued key — open ports, service instances —
    /// deduped on the rendered value, keeping the most confident assertion of each.</summary>
    public IReadOnlyList<DeviceFact> AllOf(FactKey key)
        => Facts
            .Where(f => f.Key.Equals(key) && f.SupersededUtc is null)
            .GroupBy(f => f.Value.Text, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(f => f.Confidence).ThenByDescending(f => f.ObservedUtc).First())
            .OrderBy(f => f.Value.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>How many distinct probes asserted <paramref name="key"/> — what the UI's "3 sources"
    /// affordance counts, and the cheapest signal that a value is contested.</summary>
    public int SourceCount(FactKey key)
        => Facts.Where(f => f.Key.Equals(key) && f.SupersededUtc is null)
                .Select(f => f.SourceProbe)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

    /// <summary>True when two probes disagree about a single-valued key. Surfaced rather than hidden.</summary>
    public bool IsContested(FactKey key)
        => !FactOntology.Describe(key).MultiValued
           && Facts.Where(f => f.Key.Equals(key) && f.SupersededUtc is null)
                   .Select(f => f.Value.Text)
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .Count() > 1;

    /// <summary>Best available human label, in descending order of how much a person would recognise it.</summary>
    public string DisplayName
    {
        get
        {
            foreach (var k in (FactKey[])[new("name", "hostname"), new("snmp", "sysName"),
                                          new("svc", "instance"), new("dev", "model"), new("net", "ipv4")])
                if (Best(k) is { } f && !string.IsNullOrWhiteSpace(f.Value.Text)) return f.Value.Text;

            return Best(new FactKey("link", "mac"))?.Value.Text ?? Id;
        }
    }

    public override string ToString() => $"{DisplayName} ({Id})";
}
