namespace Nexaflow.IO.Network.Model;

/// <summary>How two devices relate. Edges are how this model expresses "these are related" without the
/// irreversible commitment of a merge.</summary>
public enum EdgeKind
{
    /// <summary>To → is the default gateway for ←From.</summary>
    DefaultGateway = 0,

    /// <summary>Both observed on the same L2 segment.</summary>
    SameSubnet = 1,

    /// <summary>From is attached to a physical port on switch To (LLDP/CDP or the bridge MIB).</summary>
    SwitchPort = 2,

    /// <summary>From is bridged by To.</summary>
    BridgedBy = 3,

    /// <summary>To is From's uplink toward the internet.</summary>
    Uplink = 4,

    /// <summary>To serves DNS for From.</summary>
    DnsServer = 5,

    /// <summary>To serves DHCP for From.</summary>
    DhcpServer = 6,

    /// <summary>From consumes a service published by To.</summary>
    ServiceDependency = 7,

    /// <summary>
    /// Strong evidence these two nodes are interfaces of one physical device — a router's LAN, WAN and
    /// WLAN MACs. Deliberately an <b>edge, not a merge</b>: a wrong merge destroys history and mis-routes
    /// an action at a device the user did not mean, whereas a wrong edge is cosmetic and reversible.
    /// </summary>
    SameDevice = 8,

    /// <summary>From is wirelessly associated to access point To.</summary>
    WirelessAssociation = 9,
}

/// <summary>A directed, provenanced relationship between two device nodes.</summary>
/// <param name="FromId">Source node id.</param>
/// <param name="ToId">Target node id.</param>
/// <param name="Kind">What the relationship is.</param>
/// <param name="SourceProbe">Which probe or protocol document asserted it.</param>
/// <param name="ObservedUtc">When.</param>
/// <param name="Confidence">How much to trust it — drives solid vs dashed in the topology graph.</param>
/// <param name="Label">Optional edge annotation, e.g. a switch port name.</param>
public sealed record DeviceEdge(
    string FromId,
    string ToId,
    EdgeKind Kind,
    string SourceProbe,
    DateTimeOffset ObservedUtc,
    Confidence Confidence,
    string? Label = null)
{
    /// <summary>Identity for dedup: the same relationship re-observed updates rather than duplicates.</summary>
    public string Key => $"{FromId}|{ToId}|{Kind}";
}
