using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Nexaflow.IO.Network.Guard;

/// <summary>
/// The only thing in the product that owns a socket.
/// </summary>
/// <remarks>
/// <para>
/// Every method asks <see cref="NetworkGuard"/> first and records against the <see cref="RunBudget"/> only
/// when a packet actually left. A refusal comes back as a <see cref="GuardDecision"/> rather than an
/// exception, because a probe declining to send is an outcome — a VPN adapter that will not join a group,
/// a target off-segment — and an exception there would abort a whole sweep for something normal.
/// </para>
/// <para>
/// There is no way to get the socket out. That is the containment: a probe references this leaf and cannot
/// reference <c>System.Net.Sockets</c> usefully, so the guard cannot be gone around rather than merely
/// being impolite to bypass.
/// </para>
/// </remarks>
public sealed class UdpTransport(NetworkGuard guard, RunBudget budget) : IGuardedTransport
{
    public async Task<GuardDecision> SendUdpAsync(SendIntent intent, ReadOnlyMemory<byte> payload,
                                                  CancellationToken ct)
    {
        var decision = guard.Evaluate(intent, budget);
        if (!decision.Allowed) return decision;

        using var socket = Bound(intent);

        await socket.SendToAsync(payload, SocketFlags.None,
                                 new IPEndPoint(intent.Target, intent.Port), ct).ConfigureAwait(false);

        budget.Record(intent);
        return decision;
    }

    /// <summary>
    /// One datagram out, every reply in until the window closes.
    /// </summary>
    /// <remarks>
    /// The discovery shape, and the reason it is one method rather than a send and a listen: the replies
    /// come back to the ephemeral port the send went out from, so the socket has to be the same one. A
    /// caller composing send-then-listen would bind a second port and hear nothing.
    /// </remarks>
    public async IAsyncEnumerable<ReceivedDatagram> SendAndCollectAsync(
        SendIntent intent, ReadOnlyMemory<byte> payload, TimeSpan window,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var decision = guard.Evaluate(intent, budget);
        if (!decision.Allowed) yield break;

        using var socket = Bound(intent);
        using var closing = CancellationTokenSource.CreateLinkedTokenSource(ct);
        closing.CancelAfter(window);

        await socket.SendToAsync(payload, SocketFlags.None,
                                 new IPEndPoint(intent.Target, intent.Port), ct).ConfigureAwait(false);
        budget.Record(intent);

        await foreach (var got in Collect(socket, intent.SourceId, closing.Token).ConfigureAwait(false))
            yield return got;
    }

    public async IAsyncEnumerable<ReceivedDatagram> ListenMulticastAsync(
        IPAddress group, int port, string adapterId, [EnumeratorCancellation] CancellationToken ct)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Bind(new IPEndPoint(IPAddress.Any, port));

        try
        {
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                                   new MulticastOption(group, IPAddress.Any));
        }
        catch (SocketException)
        {
            // A group join is refused on plenty of ordinary adapters — VPN, some virtual switches. Nothing
            // arrives, which is the honest answer, and the sweep carries on with the adapters that worked.
            yield break;
        }

        await foreach (var got in Collect(socket, adapterId, ct).ConfigureAwait(false))
            yield return got;
    }

    public Task<(bool Ok, TimeSpan Rtt)> PingAsync(IPAddress target, TimeSpan timeout, CancellationToken ct)
        => Task.Run(() =>
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            try
            {
                var reply = ping.Send(target, (int)timeout.TotalMilliseconds);
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success
                    ? (true, TimeSpan.FromMilliseconds(reply.RoundtripTime))
                    : (false, TimeSpan.Zero);
            }
            catch (System.Net.NetworkInformation.PingException) { return (false, TimeSpan.Zero); }
        }, ct);

    public async Task<bool> TcpConnectAsync(IPAddress target, int port, TimeSpan timeout, CancellationToken ct)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(ct);
        giveUp.CancelAfter(timeout);

        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, port), giveUp.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            // Closed and filtered are both "no", and telling them apart is a different question from the
            // one this answers.
            return false;
        }
    }

    /// <summary>
    /// Not built, and it says so rather than pretending.
    /// </summary>
    /// <remarks>
    /// A stream needs the budget re-checked per write and a lifetime that outlives one call, and nothing in
    /// the product opens one yet — discovery is entirely datagrams. Returning a refusal here would be worse
    /// than throwing: a caller cannot tell "the guard said no" from "this was never written", and the first
    /// is a decision while the second is an absence.
    /// </remarks>
    public Task<IProtocolStream?> ConnectAsync(SendIntent intent, TimeSpan timeout, CancellationToken ct,
                                               Action<GuardDecision>? decision = null)
        => throw new NotSupportedException(
            "Stream connections are not built. Discovery is datagram-only, and a guarded stream needs the "
          + "run budget re-checked per write — see IProtocolStream.");

    /// <summary>
    /// A socket bound for this send, with broadcast or multicast enabled only when the intent says so.
    /// </summary>
    /// <remarks>
    /// The option is set from the <b>intent the guard just approved</b>, not from the address. Deriving it
    /// from the address would let a send become a broadcast after the decision was taken on the basis that
    /// it was not one.
    /// </remarks>
    private static Socket Bound(SendIntent intent)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        if (intent.Broadcast)
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

            // Low but not zero: one hop reaches the local segment, which is as far as a discovery is meant
            // to go, and zero would not leave the machine.
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
        }

        socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        return socket;
    }

    /// <summary>
    /// Everything that arrives until the token cancels.
    /// </summary>
    /// <remarks>
    /// Cancellation is the normal exit, not an error: the window closing is how a discovery ends. Pumped
    /// through a channel so the receive loop is one place, and so an iterator can be cancelled between
    /// datagrams without a receive being left half-done.
    /// </remarks>
    private static async IAsyncEnumerable<ReceivedDatagram> Collect(
        Socket socket, string adapterId, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<ReceivedDatagram>();

        var pump = Task.Run(async () =>
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    var got = await socket.ReceiveFromAsync(buffer, SocketFlags.None, from, ct)
                                          .ConfigureAwait(false);

                    if (got.ReceivedBytes <= 0) continue;

                    await channel.Writer.WriteAsync(new ReceivedDatagram(
                        (IPEndPoint)got.RemoteEndPoint,
                        buffer[..got.ReceivedBytes],
                        DateTimeOffset.UtcNow,
                        adapterId), CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // The window closed, or the socket went away with it. Both are the end of a collection.
            }
            finally { channel.Writer.TryComplete(); }
        }, CancellationToken.None);

        await foreach (var got in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            yield return got;

        await pump.ConfigureAwait(false);
    }
}
