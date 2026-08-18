using System.Net;
using Nexaflow.IO.Network.Actions;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Plugins;

namespace Nexaflow.Features.Network.Actions;

/// <summary>
/// Asks a device whether it is there, right now.
/// </summary>
/// <remarks>
/// The cheapest possible action and the one worth having first, because <b>every fact in the graph is old</b>.
/// ARP reports what the kernel remembers and SSDP reports what answered a minute ago; neither says the
/// device is there at the moment somebody is looking at it. This does, and it writes that down with a
/// short lifetime so it stops being believed on its own.
/// </remarks>
[Subfeature("network", "ping",
    DisplayName = "Ping",
    Description = "Asks the device to answer right now, and times how long it takes. Confirms it is still "
                + "there — everything else on this page is a memory of when it last was.",
    Order = 0)]
public sealed class PingAction : IDeviceAction
{
    public string ActionId => "network.ping";
    public string DisplayName => "Ping";
    public string Icon => "◉";

    public string Description =>
        "Ask the device to answer right now and time the round trip. Confirms it is still there and how "
      + "far away it is, which nothing else on this page can tell you — every other fact is a memory.";

    public ProbeCost Cost => ProbeCost.Light;

    /// <summary>Anything with an address to aim at.</summary>
    public bool AppliesTo(DeviceNode device) => Address(device) is not null;

    public async Task<DeviceActionResult> PerformAsync(DeviceNode device, IDeviceActionHost host,
                                                       CancellationToken ct)
    {
        if (Address(device) is not { } target)
            return DeviceActionResult.Failed("This device has no address to ping.");

        var (ok, rtt) = await host.Transport.PingAsync(target, TimeSpan.FromSeconds(2), ct)
                                  .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var obs = new ProbeObservation { SourceProbe = ActionId, ObservedUtc = now };

        // Identified by the very claim that named the address, scope and all. An identity is keyed by
        // kind + SCOPE + value — the same address on two isolated segments is two devices — so a claim
        // rebuilt with a scope guessed from somewhere else resolves to nothing and the ping's findings
        // land on a second node beside the one that was clicked.
        obs.Identities.Add(Claim(device, target.ToString()));

        // A short life on purpose. "It answered" is true of a moment, and a reachability fact that outlived
        // the moment would make a device that has since been unplugged look present.
        obs.Facts.Add(Fact(new FactKey("net", "reachable"), FactValue.OfBool(ok), now,
                           ok ? Confidence.Asserted : Confidence.Strong,
                           ok ? "answered a ping" : "did not answer a ping",
                           ttl: TimeSpan.FromMinutes(2)));

        if (ok)
            obs.Facts.Add(Fact(new FactKey("net", "rtt"), FactValue.OfNumber(rtt.TotalMilliseconds), now,
                               Confidence.Asserted, "ping round trip", ttl: TimeSpan.FromMinutes(2)));

        return ok
            ? DeviceActionResult.Worked($"{target} answered in {rtt.TotalMilliseconds:0} ms.", obs)
            // Not a failure of the action: it asked and got silence, which is an answer and worth recording.
            // Plenty of devices drop ICMP by policy while being perfectly reachable.
            : DeviceActionResult.Worked(
                $"{target} did not answer within 2 seconds. Some devices refuse pings by policy.", obs);
    }

    private static IPAddress? Address(DeviceNode device)
        => device.Best(new FactKey("net", "ipv4"))?.Value.Text is { Length: > 0 } v4
           && IPAddress.TryParse(v4, out var parsed) ? parsed : null;

    /// <summary>The device's own claim on this address, so what is learned resolves back to it.</summary>
    private static IdentityClaim Claim(DeviceNode device, string address)
    {
        foreach (var claim in device.Identities)
            if (claim.Kind == IdentityKind.Ip && claim.Value == address) return claim;

        // Nothing claimed it, which means the address came from a fact rather than an identity. Scope it
        // the way the device is scoped rather than leaving it unscoped, which would fuse two segments.
        var scope = device.Identities.Select(i => i.Scope).FirstOrDefault(s => s.Length > 0) ?? "";
        return new IdentityClaim(IdentityKind.Ip, address, scope, Confidence.Asserted);
    }

    private DeviceFact Fact(FactKey key, FactValue value, DateTimeOffset now, Confidence confidence,
                            string detail, TimeSpan? ttl = null)
        => new()
        {
            Key = key, Value = value, SourceProbe = ActionId, SourceDetail = detail,
            ObservedUtc = now, Confidence = confidence, Ttl = ttl,
            Layer = FactOntology.Describe(key).Layer,
        };
}
