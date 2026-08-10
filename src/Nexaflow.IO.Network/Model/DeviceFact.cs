namespace Nexaflow.IO.Network.Model;

/// <summary>A namespaced fact name. The namespace groups related keys and keeps a probe from squatting on
/// a name it doesn't own — an unrecognised namespace is rendered separately rather than blessed.</summary>
/// <param name="Ns">Short namespace: <c>link</c>, <c>net</c>, <c>dev</c>, <c>svc</c>, <c>snmp</c>, <c>iot</c>.</param>
/// <param name="Name">Key within the namespace, e.g. <c>mac</c>, <c>ipv4</c>, <c>vendor</c>.</param>
public readonly record struct FactKey(string Ns, string Name)
{
    public override string ToString() => $"{Ns}.{Name}";

    /// <summary>Parses <c>"ns.name"</c>. Anything without a dot lands in the <c>x-</c> namespace, which is
    /// where unknown keys are rendered — never dropped, never treated as first-class.</summary>
    public static FactKey Parse(string dotted)
    {
        int i = dotted.IndexOf('.');
        return i <= 0 ? new FactKey("x-", dotted) : new FactKey(dotted[..i], dotted[(i + 1)..]);
    }
}

/// <summary>What kind of thing a fact's value is. Drives rendering and keeps the AI's view typed rather
/// than a wall of strings.</summary>
public enum FactValueKind { Text, Number, Bool, Bytes, Address, Timestamp, Ref }

/// <summary>
/// A typed fact value. Deliberately a small closed union rather than <c>object</c>: the UI formats by kind,
/// the AI tool serialises by kind, and a probe cannot smuggle an arbitrary CLR type into the graph.
/// </summary>
public readonly record struct FactValue(FactValueKind Kind, string Text, double Number = 0, byte[]? Bytes = null)
{
    public static FactValue OfText(string v) => new(FactValueKind.Text, v);
    public static FactValue OfNumber(double v) => new(FactValueKind.Number, v.ToString(System.Globalization.CultureInfo.InvariantCulture), v);
    public static FactValue OfBool(bool v) => new(FactValueKind.Bool, v ? "true" : "false", v ? 1 : 0);
    public static FactValue OfBytes(byte[] v) => new(FactValueKind.Bytes, Convert.ToHexString(v).ToLowerInvariant(), 0, v);
    public static FactValue OfAddress(string v) => new(FactValueKind.Address, v);
    public static FactValue OfTimestamp(DateTimeOffset v) => new(FactValueKind.Timestamp, v.ToString("O"));
    public static FactValue OfRef(string nodeId) => new(FactValueKind.Ref, nodeId);

    public override string ToString() => Text;
}

/// <summary>
/// One claim about a device, by one probe, at one time.
///
/// <para>
/// Facts are <b>append-only and multi-valued</b>: when two probes assert the same key with different
/// values, both are kept and <see cref="DeviceNode.Best"/> picks a winner. That is the provenance — a
/// conflict surfaces in the UI as "3 sources" instead of being silently squashed, which is exactly what
/// makes the graph trustworthy enough to hand to a model.
/// </para>
/// </summary>
public sealed record DeviceFact
{
    /// <summary>What is being asserted.</summary>
    public required FactKey Key { get; init; }

    /// <summary>The asserted value.</summary>
    public required FactValue Value { get; init; }

    /// <summary>Who asserted it — a probe id (<c>network.arp</c>) or a protocol document
    /// (<c>protocol:wake-on-lan</c>). Uniform, so a document-sourced fact is as traceable as a compiled one.</summary>
    public required string SourceProbe { get; init; }

    /// <summary>Where within that source — an OID, a DNS record type, a header name. What lets a user
    /// (or the model) judge the claim rather than take it on trust.</summary>
    public string SourceDetail { get; init; } = "";

    /// <summary>When it was observed.</summary>
    public required DateTimeOffset ObservedUtc { get; init; }

    /// <summary>How much it should count against a competing value for the same key.</summary>
    public required Confidence Confidence { get; init; }

    /// <summary>How long before this should be treated as stale. Null means "no natural expiry" — a
    /// vendor name derived from an OUI does not go stale, a DHCP lease does.</summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>Which discovery layer this belongs to — <c>link</c>, <c>name</c>, <c>service</c>,
    /// <c>mgmt</c>, <c>iot</c>. Purely a UI grouping: one card per layer.</summary>
    public string Layer { get; init; } = "";

    /// <summary>Set when a later observation replaced this value (a DHCP rebind). Kept rather than deleted
    /// so history survives and a rebind is auditable.</summary>
    public DateTimeOffset? SupersededUtc { get; init; }

    /// <summary>True once <see cref="Ttl"/> has elapsed. Stale is a display state, not a deletion — nothing
    /// is removed from the graph on expiry.</summary>
    public bool IsStale(DateTimeOffset nowUtc)
        => Ttl is { } ttl && nowUtc - ObservedUtc > ttl;
}
