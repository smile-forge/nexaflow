namespace Nexaflow.IO.Network.Model;

/// <summary>The kind of key an observation offers as identity. Different layers know a device by
/// different names, and none of them is universally available — which is why identity is a lattice of
/// claims rather than a single primary key.</summary>
public enum IdentityKind
{
    /// <summary>Link-layer hardware address. Authoritative <i>within an L2 segment</i>, and the strongest
    /// key we get — but a single physical box legitimately owns several.</summary>
    Mac = 0,

    /// <summary>A UUID the device assigns itself and repeats — SSDP/UPnP <c>uuid:</c>, a Matter node id.</summary>
    Uuid = 1,

    /// <summary>A DNS-SD service instance, e.g. <c>Brother HL-3170._ipp._tcp.local</c>. Stable per service,
    /// and one device commonly publishes many.</summary>
    ServiceInstance = 2,

    /// <summary>A manufacturer serial — SNMP <c>entPhysicalSerialNum</c>, a DHCP client-id.</summary>
    Serial = 3,

    /// <summary>A host name. Weak: reassignable, and two segments can both hold a "printer".</summary>
    Hostname = 4,

    /// <summary>An IP address. Identity ONLY within a <see cref="IdentityClaim.Scope"/> (an L2 segment)
    /// and a time window — DHCP hands the same address to a different device routinely.</summary>
    Ip = 5,
}

/// <summary>
/// One assertion of the form "this observation belongs to whatever is known by <paramref name="Value"/>".
/// A probe emits several per observation; the graph resolves them and decides whether they name the same
/// node.
/// </summary>
/// <param name="Kind">Which key space <paramref name="Value"/> lives in.</param>
/// <param name="Value">The key, already normalised (lower-case MAC with colons, lower-case hostname, …).</param>
/// <param name="Scope">
/// The namespace the key is unique within — an L2 segment id for <see cref="IdentityKind.Ip"/> and
/// <see cref="IdentityKind.Mac"/>, empty for globally-unique kinds like <see cref="IdentityKind.Uuid"/>.
/// Without this, two devices on two different subnets that happen to share <c>192.168.1.10</c> would fuse.
/// </param>
/// <param name="Confidence">How much this claim should count when it conflicts with another.</param>
public readonly record struct IdentityClaim(
    IdentityKind Kind,
    string Value,
    string Scope,
    Confidence Confidence)
{
    /// <summary>The ledger key: kind + scope + value. Two claims with the same key name the same thing.</summary>
    public string Key => $"{Kind}|{Scope}|{Value}";

    public override string ToString() => $"{Kind}={Value}{(Scope.Length > 0 ? $"@{Scope}" : "")}";
}
