using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

using Nexaflow.IO.Network.Actions;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Guard;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Plugins;

namespace Nexaflow.Features.Network.Actions;

/// <summary>
/// Wakes a sleeping device by sending its adapter the magic packet.
/// </summary>
/// <remarks>
/// <para>
/// The second thing in the product to run a protocol description, and the first that only writes. Not one
/// octet of the packet is here: <c>Protocols/wol.json</c> says what a magic packet is, and this class says
/// only where it goes — the directed broadcast of the network the device was seen on, sent from this
/// machine's own address there — and how to tell whether it worked.
/// </para>
/// <para>
/// A magic packet is never answered. A device that wakes says so by being there, so after sending, this
/// pings the address the device last had until it answers or the wait runs out. Silence is reported as
/// what it is — the device may still be starting, may refuse pings, or may not have waking enabled — and
/// not as a failure of the action, which did what it was asked.
/// </para>
/// </remarks>
[Subfeature("network", "wol",
    DisplayName = "Wake on LAN",
    Description = "Sends the device's network adapter a Wake-on-LAN magic packet, to switch on a machine that is "
                + "asleep or off, then waits to see whether it answers.",
    Order = 1)]
public sealed class WakeOnLanAction : IDeviceAction
{
    /// <summary>The discard port. The format says nothing about ports — an adapter matches the pattern in
    /// any frame — and 9 is the convention every sender and every router's forwarding rule assumes.</summary>
    private const int Port = 9;

    /// <summary>How long to wait for a woken machine to answer. Inside the guard's default run limit, so a
    /// fresh click's allowance is not spent before the wait is.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(45);

    /// <summary>How long a single ping waits, and how long between them.</summary>
    private static readonly TimeSpan Echo = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Between = TimeSpan.FromSeconds(3);

    private ShippedDescription? _wol;

    public string ActionId => "network.wol";
    public string DisplayName => "Wake";
    public string Icon => "⏻";

    public string Description =>
        "Switch the device on by sending its network adapter a Wake-on-LAN magic packet, then wait for it "
      + "to answer. Works where the device has waking enabled and was last seen on a network this PC is on.";

    public ProbeCost Cost => ProbeCost.Light;

    /// <summary>It switches a machine on. That changes the device rather than asking it something, which is
    /// what the host confirms before running.</summary>
    public bool IsDestructive => true;

    /// <summary>Anything with a hardware address, which is every device the neighbour table found.</summary>
    public bool AppliesTo(DeviceNode device) => Hardware(device) is not null;

    public async Task<DeviceActionResult> PerformAsync(DeviceNode device, IDeviceActionHost host,
                                                       CancellationToken ct)
    {
        if (Hardware(device) is not { } mac)
            return DeviceActionResult.Failed("This device has no hardware address to wake.");

        // The network the address was seen on, not whichever adapter the routing table would pick: a
        // broadcast reaches one segment, and a MAC is only meaningful on the one it was found on.
        if (Facing(host.Adapters, mac.Scope) is not { } from)
            return DeviceActionResult.Failed(
                $"{mac.Value} was seen on {mac.Scope}, and no adapter here is on that network now.");

        if (Load(host.Log) is not { } wol)
            return DeviceActionResult.Failed("The Wake-on-LAN description could not be loaded — the log says why.");

        byte[] packet;
        try
        {
            packet = new GraphCodec(wol.Loaded.Graph).Encode(
                new Dictionary<string, ProtoValue>(StringComparer.Ordinal) { ["Target MAC"] = ProtoValue.Of(mac.Value) });
        }
        catch (ProtoTypeException why)
        {
            host.Log.Error($"{ActionId}: wol.json could not write a magic packet for {mac.Value}", why);
            return DeviceActionResult.Failed($"No magic packet could be written for {mac.Value}: {why.Message}");
        }

        var sent = await host.Transport.SendUdpAsync(new SendIntent
        {
            Target = from.DirectedBroadcast!, Port = Port, Layer = SendLayer.Udp, ByteCount = packet.Length,
            Initiator = SendInitiator.User, Broadcast = true, SourceId = ActionId, Via = from.Address,
            ContentHash = wol.ContentHash, DeviceNodeId = device.Id,
        }, packet, ct).ConfigureAwait(false);

        if (!sent.Allowed)
            return DeviceActionResult.Failed(sent.Reason);

        var woke = $"Sent a magic packet for {mac.Value} to {from.DirectedBroadcast}.";

        if (Address(device) is not { } target)
            return DeviceActionResult.Worked(
                $"{woke} Nothing here knows its address, so whether it woke cannot be checked.");

        return await Answered(device, mac, target, woke, host, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Pings the device until it answers or the wait is over, and says which.
    /// </summary>
    private async Task<DeviceActionResult> Answered(DeviceNode device, IdentityClaim mac, IPAddress target,
                                                    string woke, IDeviceActionHost host, CancellationToken ct)
    {
        var began = DateTimeOffset.UtcNow;

        while (true)
        {
            var outcome = await host.Transport.PingAsync(new SendIntent
            {
                Target = target, Port = 0, Layer = SendLayer.Icmp, ByteCount = PingOutcome.EchoBytes,
                Initiator = SendInitiator.User, SourceId = ActionId, DeviceNodeId = device.Id,
            }, Echo, ct).ConfigureAwait(false);

            // The guard stopping the wait is not the device failing to wake. The packet went; only the
            // checking was cut short, and why is the guard's to say.
            if (!outcome.Decision.Allowed)
                return DeviceActionResult.Worked($"{woke} Stopped waiting for it to answer: {outcome.Decision.Reason}");

            var now = DateTimeOffset.UtcNow;
            var waited = now - began;

            if (outcome.Ok)
                return DeviceActionResult.Worked(
                    $"{woke} {target} answered after {waited.TotalSeconds:0} s.",
                    Learned(mac, now, true, $"answered a ping {waited.TotalSeconds:0} s after a wake"));

            if (waited + Between >= Patience)
                return DeviceActionResult.Worked(
                    $"{woke} {target} has not answered a ping in {Patience.TotalSeconds:0} seconds. It may still be "
                  + "starting, may refuse pings, or may not have Wake-on-LAN enabled.",
                    Learned(mac, now, false, "did not answer a ping after a wake"));

            await Task.Delay(Between, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// What the wait found out, identified by the very claim that was woken.
    /// </summary>
    /// <remarks>
    /// The hardware address rather than the IP, because it is what the wake was aimed at and the strongest
    /// key the graph has — and it is the device's own claim, scope and all, so the observation resolves to
    /// the node that was clicked rather than minting a second one beside it.
    /// </remarks>
    private ProbeObservation Learned(IdentityClaim mac, DateTimeOffset now, bool reachable, string detail)
    {
        var obs = new ProbeObservation { SourceProbe = ActionId, ObservedUtc = now };
        obs.Identities.Add(mac);

        // A short life, as a ping's: "it answered" is true of a moment.
        var key = new FactKey("net", "reachable");
        obs.Facts.Add(new DeviceFact
        {
            Key = key, Value = FactValue.OfBool(reachable), SourceProbe = ActionId, SourceDetail = detail,
            ObservedUtc = now, Confidence = reachable ? Confidence.Asserted : Confidence.Strong,
            Ttl = TimeSpan.FromMinutes(2), Layer = FactOntology.Describe(key).Layer,
        });

        return obs;
    }

    private static IdentityClaim? Hardware(DeviceNode device)
        => device.Identities.FirstOrDefault(i => i.Kind == IdentityKind.Mac && i.Value is { Length: > 0 }) is
               { Value: not null } claim ? claim : null;

    /// <summary>
    /// This machine's IPv4 address on the network a device was seen on, which is both where the packet is
    /// sent from and, through its prefix, where it is sent to.
    /// </summary>
    private static AdapterAddress? Facing(IReadOnlyList<NetworkAdapterInfo> adapters, string segment)
    {
        foreach (var adapter in adapters)
        {
            if (!adapter.IsUsable || !string.Equals(adapter.SegmentId, segment, StringComparison.Ordinal)) continue;

            foreach (var address in adapter.Addresses)
                if (address.Address.AddressFamily == AddressFamily.InterNetwork && address.DirectedBroadcast is not null)
                    return address;
        }

        return null;
    }

    private static IPAddress? Address(DeviceNode device)
        => device.Best(new FactKey("net", "ipv4"))?.Value.Text is { Length: > 0 } v4
           && IPAddress.TryParse(v4, out var parsed) ? parsed : null;

    /// <summary>
    /// The description, read once, with the hash of the octets it was read from.
    /// </summary>
    /// <remarks>
    /// The hash travels on every send. Trust attaches to bytes rather than to a name — an edited description
    /// is a different description — so what the guard and an audit are told is which exact file wrote this.
    /// </remarks>
    private ShippedDescription? Load(IProbeLog log)
    {
        if (_wol is not null) return _wol;

        try
        {
            var octets = ProtocolFile.ReadShipped("wol.json");
            return _wol = new ShippedDescription(
                ProtocolFile.Read(System.Text.Encoding.UTF8.GetString(octets)),
                Convert.ToHexString(SHA256.HashData(octets)).ToLowerInvariant());
        }
        catch (Exception ex) when (ex is IOException or ProtoTypeException)
        {
            log.Error($"{ActionId}: could not load {ProtocolFile.ShippedPath("wol.json")}", ex);
            return null;
        }
    }

    private sealed record ShippedDescription(ProtocolFile.Loaded Loaded, string ContentHash);
}
