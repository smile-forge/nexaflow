using System.Net;
using Nexaflow.Elevation.Contracts;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Guard;
using Nexaflow.IO.Network.Model;

namespace Nexaflow.IO.Network.Probes;

/// <summary>What a completed sweep came to.</summary>
/// <param name="Observations">How many the probes handed over.</param>
/// <param name="Log">What the probes said while they worked.</param>
public readonly record struct SweepResult(int Observations, IReadOnlyList<string> Log);

/// <summary>
/// Runs every probe across every adapter and hands what they find to whoever owns the graph.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of the orchestration, and it is deliberately small: a probe is asked which adapters
/// it applies to, then asked for observations, and the graph decides what those observations mean. Nothing
/// here knows what ARP or SSDP are.
/// </para>
/// <para>
/// It is also where the capability story lands. Core builds probes but never hands one <c>IShellServices</c>;
/// each probe is attached to a host of its own — adapters, a guarded transport, a log and its own settings —
/// from the feature that owns the page. What a probe can reach is bounded by that host rather than by what it
/// can find, and the transport on it stamps every intent with the probe's declared cost, so the consent a
/// sweep needs is decided on what the probe <i>is</i> rather than on what it says about each packet.
/// </para>
/// <para>
/// One probe failing is not a sweep failing. A VPN adapter that refuses a multicast join, a probe that
/// throws on one adapter — the exception is logged against that probe and the others carry on, because
/// a discovery that returns nothing because one layer misbehaved is worse than a partial answer.
/// </para>
/// </remarks>
public sealed class DiscoveryRun : IProbeLog
{
    /// <summary>How many findings travel together. Small enough that a slow layer's early answers show
    /// before its window closes; large enough that a full neighbour table is not a hundred hand-offs.</summary>
    private const int BatchSize = 32;

    private readonly List<string> _log = [];
    private readonly Func<string, string, string> _setting;
    private readonly Func<ValuePrompt, CancellationToken, Task<string?>>? _prompt;
    private readonly Func<string, string, CancellationToken, Task<bool>>? _confirm;
    private readonly IReadOnlyCollection<(string AdapterId, string Segment)> _sweepNetworks;

    /// <param name="adapters">Adapters to sweep. The same list the guard was given, so what is hidden is
    /// also not a legal target.</param>
    /// <param name="transport">The only route to the wire.</param>
    /// <param name="setting">Resolves (probeId, name) to a configured value, or empty to take the default the
    /// probe itself declared.</param>
    /// <param name="graph">The graph a headless sweep folds into. A caller that keeps one across sweeps passes
    /// it in, so a device seen last time and missing now is absent rather than forgotten.</param>
    /// <param name="sweepNetworks">The (adapter, subnet) pairs the user has agreed may be swept. A probe
    /// whose cost is <see cref="ProbeCost.Sweep"/> or more is not run anywhere else — the guard would refuse
    /// every packet it tried, and one line in the log says that better than two hundred refusals.</param>
    public DiscoveryRun(IReadOnlyList<NetworkAdapterInfo> adapters,
                        IGuardedTransport transport,
                        Func<string, string, string>? setting = null,
                        Func<ValuePrompt, CancellationToken, Task<string?>>? prompt = null,
                        Func<string, string, CancellationToken, Task<bool>>? confirm = null,
                        DeviceGraph? graph = null,
                        IReadOnlyCollection<(string AdapterId, string Segment)>? sweepNetworks = null)
    {
        Adapters = adapters;
        Transport = transport;
        Graph = graph ?? new DeviceGraph();
        _setting = setting ?? ((_, _) => "");
        _prompt = prompt;
        _confirm = confirm;
        _sweepNetworks = sweepNetworks ?? [];
    }

    public IReadOnlyList<NetworkAdapterInfo> Adapters { get; }
    public IGuardedTransport Transport { get; }

    /// <summary>The graph a headless sweep folds into. Kept across runs, so a device seen last time and
    /// missing now is <i>absent</i> rather than forgotten.</summary>
    public DeviceGraph Graph { get; }

    public IReadOnlyList<string> Messages => _log;

    /// <summary>
    /// Sweeps once, folding what is found into <see cref="Graph"/> on the calling thread — for a caller that
    /// owns no other thread the graph must stay on.
    /// </summary>
    public async Task<SweepResult> SweepAsync(IReadOnlyList<INetworkProbe> probes, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var applier = new GraphApplier(Graph);

        var result = await SweepAsync(probes, applier, ct).ConfigureAwait(false);

        // Anything the graph knew and nothing saw this time is absent, not gone. A device that was off is
        // a fact worth keeping — deleting it would make every sweep look like a first one.
        applier.Finish(started);
        return result;
    }

    /// <summary>
    /// Sweeps once, handing what is found to <paramref name="sink"/> rather than writing any graph.
    /// </summary>
    /// <param name="probes">Every layer to run, in whatever order they were discovered — a probe may not
    /// depend on another having run, because the user can switch any of them off.</param>
    /// <remarks>
    /// A stop still hands over what arrived before it: it was real when it was found.
    /// </remarks>
    public async Task<SweepResult> SweepAsync(IReadOnlyList<INetworkProbe> probes, IDiscoverySink sink,
                                              CancellationToken ct)
    {
        int observations = 0;

        foreach (var probe in probes)
        {
            probe.Attach(new Host(this, probe));

            foreach (var adapter in Adapters)
            {
                ct.ThrowIfCancellationRequested();

                if (!probe.AppliesTo(adapter)) continue;

                if (!Consented(probe, adapter))
                {
                    Say("", $"{probe.ProbeId}: not run on {adapter.Name} — sweeping {adapter.SegmentId} is "
                          + "not allowed. Allow it for this network in Options → Network.");
                    continue;
                }

                List<ProbeObservation> batch = [];
                try
                {
                    await foreach (var observed in probe.DiscoverAsync(adapter, ct).ConfigureAwait(false))
                    {
                        observations++;
                        batch.Add(observed);

                        if (batch.Count < BatchSize) continue;
                        await sink.ObservedAsync(probe.ProbeId, adapter, batch).ConfigureAwait(false);
                        batch = [];
                    }

                    if (batch.Count > 0) await sink.ObservedAsync(probe.ProbeId, adapter, batch).ConfigureAwait(false);
                    await sink.CompletedAsync(probe.ProbeId, adapter).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (batch.Count > 0) await sink.ObservedAsync(probe.ProbeId, adapter, batch).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    // What it found before it failed is still real; what it did not get to is not "absent".
                    if (batch.Count > 0) await sink.ObservedAsync(probe.ProbeId, adapter, batch).ConfigureAwait(false);
                    Error($"{probe.ProbeId} failed on {adapter.Name}", ex);
                }
            }
        }

        return new SweepResult(observations, [.. _log]);
    }

    /// <summary>A probe that sweeps runs only on a network the user agreed may be swept.</summary>
    private bool Consented(INetworkProbe probe, NetworkAdapterInfo adapter)
        => probe.Cost < ProbeCost.Sweep
        || _sweepNetworks.Any(n => string.Equals(n.AdapterId, adapter.Id, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(n.Segment, adapter.SegmentId, StringComparison.Ordinal));

    void IProbeLog.Info(string message) => Say("", message);
    void IProbeLog.Warn(string message) => Say("warning: ", message);
    void IProbeLog.Error(string message, Exception? ex)
        => Say("error: ", ex is null ? message : $"{message} — {ex.Message}");

    private void Error(string message, Exception ex) => ((IProbeLog)this).Error(message, ex);

    private void Say(string level, string message)
    {
        lock (_log) _log.Add($"{level}{message}");
    }

    /// <summary>
    /// What one probe is handed.
    /// </summary>
    /// <remarks>
    /// One per probe rather than the run itself, so a setting is resolved against the probe that asks — not
    /// against whichever probe happened to be running when it did.
    /// </remarks>
    private sealed class Host(DiscoveryRun run, INetworkProbe probe) : IProbeHost
    {
        public IReadOnlyList<NetworkAdapterInfo> Adapters => run.Adapters;
        public IGuardedTransport Transport { get; } = new Stamped(run.Transport, probe);
        public IProbeLog Log => run;

        /// <summary>The configured value, or else the default the probe declared — what
        /// <see cref="IProbeHost.Setting"/> promises, so a probe need not repeat its own defaults.</summary>
        public string Setting(string name)
            => run._setting(probe.ProbeId, name) is { Length: > 0 } configured
                ? configured
                : probe.Settings.FirstOrDefault(s => s.Name == name)?.Default ?? "";

        public Task<string?> PromptAsync(ValuePrompt prompt, CancellationToken ct)
            => run._prompt is null ? Task.FromResult<string?>(null) : run._prompt(prompt, ct);

        public Task<bool> ConfirmAsync(string title, string message, CancellationToken ct)
            => run._confirm is null ? Task.FromResult(false) : run._confirm(title, message, ct);

        /// <summary>Not granted. A discovery layer that needs administrator rights is a different
        /// conversation from the one this run is having, and handing every probe an elevation channel by
        /// default is how a capability boundary stops being one.</summary>
        public Task<ElevatedResult> RunElevatedAsync(ElevatedRequest request, CancellationToken ct)
            => throw new NotSupportedException(
                "A discovery sweep does not grant elevation. Route an admin action through the page's own "
              + "IShellServices.RunElevatedAsync, where the user can see what is asking.");
    }

    /// <summary>
    /// The transport a probe is handed: everything it sends leaves at the cost the probe declared.
    /// </summary>
    private sealed class Stamped(IGuardedTransport inner, INetworkProbe probe) : IGuardedTransport
    {
        private SendIntent As(SendIntent intent) => intent with { Cost = probe.Cost };

        public Task<GuardDecision> SendUdpAsync(SendIntent intent, ReadOnlyMemory<byte> payload, CancellationToken ct)
            => inner.SendUdpAsync(As(intent), payload, ct);

        public IAsyncEnumerable<ReceivedDatagram> SendAndCollectAsync(
            SendIntent intent, ReadOnlyMemory<byte> payload, TimeSpan window, CancellationToken ct)
            => inner.SendAndCollectAsync(As(intent), payload, window, ct);

        public IAsyncEnumerable<ReceivedDatagram> ListenMulticastAsync(
            IPAddress group, int port, string adapterId, CancellationToken ct)
            => inner.ListenMulticastAsync(group, port, adapterId, ct);

        public Task<IProtocolStream?> ConnectAsync(SendIntent intent, TimeSpan timeout, CancellationToken ct,
                                                   Action<GuardDecision>? decision = null)
            => inner.ConnectAsync(As(intent), timeout, ct, decision);

        public Task<FetchedDocument> FetchAsync(SendIntent intent, Uri url, TimeSpan timeout, CancellationToken ct)
            => inner.FetchAsync(As(intent), url, timeout, ct);

        public Task<PingOutcome> PingAsync(SendIntent intent, TimeSpan timeout, CancellationToken ct)
            => inner.PingAsync(As(intent), timeout, ct);

        public Task<bool> TcpConnectAsync(IPAddress target, int port, TimeSpan timeout, CancellationToken ct)
            => inner.TcpConnectAsync(target, port, timeout, ct);
    }
}
