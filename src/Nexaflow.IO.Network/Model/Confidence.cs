namespace Nexaflow.IO.Network.Model;

/// <summary>
/// How much weight an assertion carries. Used uniformly by identity claims, facts and edges, so a
/// low-confidence guess can never silently outrank something the wire actually told us.
/// </summary>
/// <remarks>
/// The ordering is meaningful and load-bearing: <c>Best()</c> resolution sorts on it first. Values are
/// explicit so persisted JSON survives a reordering of the enum.
/// </remarks>
public enum Confidence
{
    /// <summary>An inference from weak signal — an OUI-based device-class guess, a port-number heuristic.</summary>
    Guess = 0,

    /// <summary>Consistent with what we saw, but the protocol did not state it — a hostname inferred from
    /// a service instance name, a vendor from an OUI prefix.</summary>
    Likely = 1,

    /// <summary>The device told us, but through a channel that can be spoofed or stale — an mDNS TXT
    /// record, an SSDP description, a cached ARP entry.</summary>
    Strong = 2,

    /// <summary>Observed directly on the wire in this exchange and not reasonably deniable — the source MAC
    /// of a frame we received, a TCP connect that succeeded, an ICMP echo reply.</summary>
    Asserted = 3,
}
