using System.Net;

namespace Nexaflow.IO.Network.Guard;

/// <summary>What a fetch came back with.</summary>
/// <param name="Ok">Whether the far end answered with something.</param>
/// <param name="Body">The octets. Empty when it did not.</param>
/// <param name="ContentType">What the far end called them, lower-cased, or empty.</param>
/// <param name="Detail">Why, when it did not work — a refusal, a timeout, a status code.</param>
public readonly record struct FetchedDocument(bool Ok, byte[] Body, string ContentType, string Detail)
{
    public static FetchedDocument Nothing(string detail) => new(false, [], "", detail);
}

/// <summary>One datagram received.</summary>
/// <param name="From">Who sent it.</param>
/// <param name="Payload">The bytes.</param>
/// <param name="ReceivedUtc">When.</param>
/// <param name="ViaAdapterId">Which local adapter it arrived on — needed to scope identity claims, since
/// the same address on two segments is two different devices.</param>
public readonly record struct ReceivedDatagram(
    IPEndPoint From, byte[] Payload, DateTimeOffset ReceivedUtc, string ViaAdapterId);

/// <summary>
/// The only route to the wire. Every method takes a <see cref="SendIntent"/> and puts it through
/// <see cref="NetworkGuard"/> first; a refusal is returned as a result, never thrown, so a probe or a
/// protocol run treats it as an outcome rather than a crash.
/// </summary>
/// <remarks>
/// Deliberately narrow. There is no "give me a socket" escape hatch, and no overload that takes bytes
/// without an intent — the guard can only be complete if it cannot be gone around.
/// </remarks>
public interface IGuardedTransport
{
    /// <summary>Sends one datagram. Returns the guard's decision; <c>Allowed == false</c> means nothing
    /// left the machine.</summary>
    Task<GuardDecision> SendUdpAsync(SendIntent intent, ReadOnlyMemory<byte> payload, CancellationToken ct);

    /// <summary>Sends a datagram and collects every reply until <paramref name="window"/> elapses. The
    /// discovery shape: one M-SEARCH or one mDNS query, many responders.</summary>
    IAsyncEnumerable<ReceivedDatagram> SendAndCollectAsync(
        SendIntent intent, ReadOnlyMemory<byte> payload, TimeSpan window, CancellationToken ct);

    /// <summary>Joins a multicast group on one adapter and yields what arrives until cancelled. Used for
    /// passive listening (SSDP NOTIFY, continuous mDNS browse).</summary>
    IAsyncEnumerable<ReceivedDatagram> ListenMulticastAsync(
        IPAddress group, int port, string adapterId, CancellationToken ct);

    /// <summary>Opens a stream connection, guard-checked. Null when refused — the decision is reported
    /// through <paramref name="decision"/>.</summary>
    Task<IProtocolStream?> ConnectAsync(SendIntent intent, TimeSpan timeout,
                                        CancellationToken ct, Action<GuardDecision>? decision = null);

    /// <summary>
    /// Fetches one document over HTTP, guard-checked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A primitive for the same reason <see cref="PingAsync"/> is one: the alternative is every caller
    /// building request lines over a raw stream, and the guard's whole claim is that there is no route to
    /// the wire it has not seen. The <see cref="SendIntent"/> is judged before a connection is opened, so
    /// a document on a host off-segment is refused rather than fetched and then regretted.
    /// </para>
    /// <para>
    /// Bytes rather than text, because what comes back is a description document one moment and a PNG the
    /// next, and deciding which is the caller's business.
    /// </para>
    /// </remarks>
    Task<FetchedDocument> FetchAsync(SendIntent intent, Uri url, TimeSpan timeout, CancellationToken ct);

    /// <summary>ICMP echo. An engine primitive rather than a raw-socket concern: Windows blocks raw
    /// TCP/UDP send from user mode, and offering ping directly removes the commonest reason a protocol
    /// document would ever ask to build IP headers.</summary>
    Task<(bool Ok, TimeSpan Rtt)> PingAsync(IPAddress target, TimeSpan timeout, CancellationToken ct);

    /// <summary>TCP connect probe — open/closed/filtered without sending a payload.</summary>
    Task<bool> TcpConnectAsync(IPAddress target, int port, TimeSpan timeout, CancellationToken ct);
}

/// <summary>A guard-checked stream. Writes re-check the budget, so a long-lived connection cannot be used
/// to escape the per-run ceilings a single send would have been subject to.</summary>
public interface IProtocolStream : IAsyncDisposable
{
    Task<GuardDecision> WriteAsync(SendIntent intent, ReadOnlyMemory<byte> payload, CancellationToken ct);

    /// <summary>Reads up to <paramref name="max"/> bytes, or fewer if the peer pauses. Returns an empty
    /// buffer when the peer closed.</summary>
    Task<byte[]> ReadAsync(int max, TimeSpan timeout, CancellationToken ct);
}
