using System.Net;

namespace Nexaflow.IO.Network.Guard;

/// <summary>Where a send is coming from, which decides how much it is trusted.</summary>
public enum SendInitiator
{
    /// <summary>A built-in discovery probe running its declared protocol.</summary>
    Probe = 0,

    /// <summary>The user pressed a button.</summary>
    User = 1,

    /// <summary>A protocol document the user reviewed and trusted.</summary>
    ReviewedDocument = 2,

    /// <summary>A document the AI wrote and nobody has reviewed. The most constrained case by a wide
    /// margin: never broadcasts, never elevates, one target, a handful of packets.</summary>
    AiDraft = 3,
}

/// <summary>The transport a send wants. Anything below <see cref="Udp"/> needs elevation and is not
/// enabled at all in the first release, which keeps the elevation surface at zero.</summary>
public enum SendLayer { Udp = 0, Tcp = 1, Tls = 2, RawIp = 3, Ethernet = 4 }

/// <summary>
/// Everything the guard needs to decide whether a send may happen. Constructed at the boundary and passed
/// with the bytes — a send whose bytes the guard cannot attribute to a validated source is refused, so
/// there is no path to the wire that skips this.
/// </summary>
public sealed record SendIntent
{
    public required IPAddress Target { get; init; }
    public required int Port { get; init; }
    public required SendLayer Layer { get; init; }
    public required int ByteCount { get; init; }
    public required SendInitiator Initiator { get; init; }

    /// <summary>True for a directed broadcast or a multicast group send.</summary>
    public bool Broadcast { get; init; }

    /// <summary>Probe id or <c>protocol:&lt;id&gt;</c>. Recorded in the audit log.</summary>
    public required string SourceId { get; init; }

    /// <summary>Content hash of the protocol document, when there is one. Trust attaches to bytes, not to
    /// a name — an edited document is a different document.</summary>
    public string? ContentHash { get; init; }

    /// <summary>The device this is aimed at, when known. Null for a broadcast or a first-contact sweep.</summary>
    public string? DeviceNodeId { get; init; }
}
