using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Guard;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Plugins;

namespace Nexaflow.Features.Network.Arp;

/// <summary>
/// Pings every address on an adapter's subnet so the neighbour table fills, then reads it.
/// </summary>
/// <remarks>
/// <para>
/// The only way to find a device that never talks to this machine — a phone, a printer, anything idle on the
/// Wi-Fi. The echo itself is not the point: before it can be sent the operating system has to resolve the
/// address to a hardware one, and that exchange is answered even by a device that drops pings. So a reply is
/// a bonus, and the table is the result.
/// </para>
/// <para>
/// A layer of its own rather than a setting on ARP, because it is a different kind of thing to do. ARP reads
/// what the kernel already knows and sends nothing; this sends a packet to every address on the segment,
/// which intrusion detection sees. It runs only on a network allowed in Options → Network, and the guard
/// refuses its packets anywhere else whatever this class does.
/// </para>
/// <para>
/// It reads the table itself instead of leaving that to ARP, because a probe may not depend on another having
/// run — the user can switch either off.
/// </para>
/// </remarks>
[Subfeature("network", "sweep",
    DisplayName = "Address sweep",
    Description = "Pings every address on the adapter's subnet so the neighbour table fills, then reads it. "
                + "Finds devices that never talk to this PC — phones, printers, anything idle on Wi-Fi. "
                + "Intrusion detection sees it, so it runs only on networks allowed in Options → Network.",
    DefaultEnabled = false,
    Order = 0)]
public sealed class AddressSweepProbe : INetworkProbe
{
    /// <summary>How long the OS is left to finish resolving before the table is read. Windows retries an
    /// unanswered resolution before it gives up, and a row read mid-retry is Incomplete.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    private readonly Func<IReadOnlyList<NeighborEntry>> _readTable;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;
    private IProbeHost? _host;

    public AddressSweepProbe() : this(NativeMethods.ReadNeighborTable, Task.Delay) { }

    /// <summary>The seams a test runs through: a table instead of the kernel's, and waiting that does not.</summary>
    internal AddressSweepProbe(Func<IReadOnlyList<NeighborEntry>> readTable,
                               Func<TimeSpan, CancellationToken, Task> wait)
    {
        _readTable = readTable;
        _wait = wait;
    }

    public string ProbeId => "network.sweep";
    public string DisplayName => "Address sweep";

    /// <summary>One packet for every address on the subnet: the cost the consent is for.</summary>
    public ProbeCost Cost => ProbeCost.Sweep;

    public IReadOnlyList<ProbeSetting> Settings =>
    [
        new ProbeSetting(
            "maxHosts",
            "The most addresses one sweep will ping. A /24 is 254; a bigger subnet is refused rather than swept "
          + "slowly, because a sweep that takes minutes is one nobody is watching.",
            ProbeSettingType.Int, Default: "254", Min: 1, Max: 1022),

        new ProbeSetting(
            "concurrency",
            "How many pings may be waiting for an answer at once.",
            ProbeSettingType.Int, Default: "32", Min: 1, Max: 64),

        new ProbeSetting(
            "rate",
            "The most pings sent in a second. Kept under the guard's own ceiling for a run, so a sweep paces "
          + "itself rather than being refused.",
            ProbeSettingType.Int, Default: "50", Min: 1, Max: 100),

        new ProbeSetting(
            "pingTimeoutMs",
            "How long to wait for each echo, in milliseconds. Short on purpose: the address resolution before "
          + "the echo is what fills the table, and it has happened whether or not the device replies.",
            ProbeSettingType.Int, Default: "500", Min: 100, Max: 2000),
    ];

    public void Attach(IProbeHost host) => _host = host;

    /// <summary>An adapter with an IPv4 subnet that has hosts on it. IPv6 is not swept: a /64 is not a list
    /// anybody pings through.</summary>
    public bool AppliesTo(NetworkAdapterInfo adapter) => adapter.IsUsable && Subnet(adapter) is not null;

    public async IAsyncEnumerable<ProbeObservation> DiscoverAsync(
        NetworkAdapterInfo adapter,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_host is null || Subnet(adapter) is not { } subnet) yield break;

        int maxHosts = Number("maxHosts");
        int concurrency = Number("concurrency");
        var pace = TimeSpan.FromSeconds(1.0 / Number("rate"));
        var timeout = TimeSpan.FromMilliseconds(Number("pingTimeoutMs"));

        if (subnet.HostCount > maxHosts)
        {
            _host.Log.Warn($"{ProbeId}: {adapter.SegmentId} has {subnet.HostCount} addresses, more than the "
                         + $"{maxHosts} one sweep may ping (the maxHosts setting). Not swept.");
            yield break;
        }

        var own = adapter.Addresses.Select(a => a.Address).ToHashSet();
        var targets = subnet.Hosts().Where(h => !own.Contains(h)).ToList();

        var tally = new Tally();
        using var slots = new SemaphoreSlim(concurrency);
        List<Task> echoes = [];

        foreach (var target in targets)
        {
            if (tally.Stopped is not null) break;

            await slots.WaitAsync(ct).ConfigureAwait(false);
            echoes.Add(EchoAsync(target, timeout, slots, tally, ct));

            // Paced here rather than by refusals: the guard's run-wide ceiling is a backstop, and a sweep that
            // leaned on it would spend its budget being told no.
            await _wait(pace, ct).ConfigureAwait(false);
        }

        await Task.WhenAll(echoes).ConfigureAwait(false);

        if (tally.Stopped is { } why)
        {
            _host.Log.Warn($"{ProbeId}: stopped on {adapter.Name} — {why}");
            yield break;
        }

        await _wait(Settle, ct).ConfigureAwait(false);

        var rows = await Task.Run(_readTable, ct).ConfigureAwait(false);

        // Only what the sweep itself refreshed: a row the OS merely remembers from before was not found by
        // this, and saying so would make the sweep take credit for the table's memory.
        var devices = NeighborRows.OnAdapter(rows, adapter, includeStale: false, includeUnreachable: false,
                                             within: subnet);
        var shared = NeighborRows.SharedMacs(devices);
        var replied = tally.Answered.ToHashSet();
        var now = DateTimeOffset.UtcNow;

        foreach (var row in devices)
        {
            ct.ThrowIfCancellationRequested();

            var obs = NeighborRows.Observe(row, adapter, ProbeId, now, withMac: !shared.Contains(row.Mac));

            if (replied.Contains(row.Address))
                obs.Facts.Add(NeighborRows.Fact(ProbeId, new FactKey("net", "reachable"), FactValue.OfBool(true),
                                                now, Confidence.Asserted, "answered the sweep's ping",
                                                ttl: TimeSpan.FromMinutes(2)));

            yield return obs;
        }

        foreach (var mac in shared)
            _host.Log.Warn($"{ProbeId}: {mac} answers for more than {NeighborRows.MostAddressesPerMac} addresses on "
                         + $"{adapter.Name} — proxy ARP, or an access point isolating its clients. Those rows are "
                         + "listed by address alone rather than fused into one device.");

        _host.Log.Info($"{ProbeId}: pinged {echoes.Count} address(es) on {adapter.SegmentId}; {replied.Count} "
                     + $"answered, {devices.Count} found in the neighbour table.");
    }

    /// <summary>
    /// One echo, retried while the guard says only "too fast".
    /// </summary>
    /// <remarks>
    /// Any other refusal — switched off, not allowed on this network, out of budget, off the subnet — is an
    /// answer every later address would get too, so it stops the sweep rather than being asked two hundred
    /// more times.
    /// </remarks>
    private async Task EchoAsync(IPAddress target, TimeSpan timeout, SemaphoreSlim slots, Tally tally,
                                 CancellationToken ct)
    {
        try
        {
            for (int attempt = 1; attempt <= 3 && tally.Stopped is null; attempt++)
            {
                var outcome = await _host!.Transport.PingAsync(new SendIntent
                {
                    Target = target, Port = 0, Layer = SendLayer.Icmp, ByteCount = PingOutcome.EchoBytes,
                    Initiator = SendInitiator.Probe, SourceId = ProbeId, Cost = ProbeCost.Sweep,
                }, timeout, ct).ConfigureAwait(false);

                if (outcome.Decision.Allowed)
                {
                    if (outcome.Ok) tally.Answered.Add(target);
                    return;
                }

                if (outcome.Decision.Refusal != GuardRefusal.RateLimited)
                {
                    tally.Stop(outcome.Decision.Reason);
                    return;
                }

                await _wait(TimeSpan.FromMilliseconds(250 * attempt), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            slots.Release();
        }
    }

    /// <summary>A setting as a number, within the bounds it declared — and its declared default when the host
    /// has nothing, or nothing that parses.</summary>
    private int Number(string name)
    {
        var declared = Settings.First(s => s.Name == name);

        int value = int.TryParse(_host?.Setting(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : int.Parse(declared.Default, CultureInfo.InvariantCulture);

        return Math.Clamp(value, (int)(declared.Min ?? value), (int)(declared.Max ?? value));
    }

    /// <summary>The subnet the adapter's segment names — its first IPv4 address, which is what
    /// <c>SegmentId</c> is built from and so what the user's permission was given for.</summary>
    private static AdapterAddress? Subnet(NetworkAdapterInfo adapter)
    {
        foreach (var a in adapter.Addresses)
            if (a.Address.AddressFamily == AddressFamily.InterNetwork)
                return a.HostCount > 0 ? a : null;

        return null;
    }

    /// <summary>What the echoes found, and the first reason to stop sending them.</summary>
    private sealed class Tally
    {
        private string? _stopped;

        public ConcurrentBag<IPAddress> Answered { get; } = [];

        public string? Stopped => Volatile.Read(ref _stopped);

        public void Stop(string reason) => Interlocked.CompareExchange(ref _stopped, reason, null);
    }
}
