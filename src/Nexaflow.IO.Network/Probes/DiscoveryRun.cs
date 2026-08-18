using Nexaflow.Elevation.Contracts;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Guard;
using Nexaflow.IO.Network.Model;

namespace Nexaflow.IO.Network.Probes;

/// <summary>What a completed sweep came to.</summary>
/// <param name="Observations">How many a probe handed over.</param>
/// <param name="Devices">How many distinct devices the graph resolved them into — fewer, when two probes
/// found the same one, which is the whole point of the graph.</param>
/// <param name="Log">What the probes said while they worked.</param>
public readonly record struct SweepResult(int Observations, int Devices, IReadOnlyList<string> Log);

/// <summary>
/// Runs every probe across every adapter and folds what they find into one device graph.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of the orchestration, and it is deliberately small: a probe is asked which adapters
/// it applies to, then asked for observations, and the graph decides what those observations mean. Nothing
/// here knows what ARP or SSDP are.
/// </para>
/// <para>
/// It is also the <see cref="IProbeHost"/>, which is where the capability story lands. Core builds probes
/// but never hands one <c>IShellServices</c>; a probe gets adapters, a guarded transport, a log and its
/// settings, from the feature that owns the page. What a probe can reach is bounded by this interface
/// rather than by what it can find.
/// </para>
/// <para>
/// One probe failing is not a sweep failing. A VPN adapter that refuses a multicast join, a probe that
/// throws on one adapter — the exception is logged against that probe and the others carry on, because
/// a discovery that returns nothing because one layer misbehaved is worse than a partial answer.
/// </para>
/// </remarks>
public sealed class DiscoveryRun : IProbeHost, IProbeLog
{
    private readonly List<string> _log = [];
    private readonly Func<string, string, string> _setting;
    private readonly Func<ValuePrompt, CancellationToken, Task<string?>>? _prompt;
    private readonly Func<string, string, CancellationToken, Task<bool>>? _confirm;
    private string _running = "";

    /// <param name="adapters">Adapters to sweep. The same list the guard was given, so what is hidden is
    /// also not a legal target.</param>
    /// <param name="transport">The only route to the wire.</param>
    /// <param name="setting">Resolves (probeId, name) to a configured value, or empty for the default.</param>
    /// <param name="graph">The graph to fold findings into. A caller that keeps one across sweeps passes
    /// it in, so a device seen last time and missing now is absent rather than forgotten — and so a fact an
    /// action established survives the next discovery.</param>
    public DiscoveryRun(IReadOnlyList<NetworkAdapterInfo> adapters,
                        IGuardedTransport transport,
                        Func<string, string, string>? setting = null,
                        Func<ValuePrompt, CancellationToken, Task<string?>>? prompt = null,
                        Func<string, string, CancellationToken, Task<bool>>? confirm = null,
                        DeviceGraph? graph = null)
    {
        Adapters = adapters;
        Transport = transport;
        Graph = graph ?? new DeviceGraph();
        _setting = setting ?? ((_, _) => "");
        _prompt = prompt;
        _confirm = confirm;
    }

    public IReadOnlyList<NetworkAdapterInfo> Adapters { get; }
    public IGuardedTransport Transport { get; }
    public IProbeLog Log => this;

    /// <summary>The graph every sweep folds into. Kept across runs, so a device seen last time and missing
    /// now is <i>absent</i> rather than forgotten.</summary>
    public DeviceGraph Graph { get; }

    public IReadOnlyList<string> Messages => _log;

    /// <summary>
    /// Sweeps once.
    /// </summary>
    /// <param name="probes">Every layer to run, in whatever order they were discovered — a probe may not
    /// depend on another having run, because the user can switch any of them off.</param>
    public async Task<SweepResult> SweepAsync(IReadOnlyList<INetworkProbe> probes, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        int observations = 0;
        List<string> seen = [];

        foreach (var probe in probes)
        {
            _running = probe.ProbeId;
            probe.Attach(this);

            foreach (var adapter in Adapters)
            {
                if (!probe.AppliesTo(adapter)) continue;

                try
                {
                    await foreach (var observed in probe.DiscoverAsync(adapter, ct).ConfigureAwait(false))
                    {
                        observations++;
                        if (Graph.Observe(observed) is { } node) seen.Add(node.Id);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Error($"{probe.ProbeId} failed on {adapter.Name}", ex);
                }
            }
        }

        _running = "";

        // Anything the graph knew and nothing saw this time is absent, not gone. A device that was off is
        // a fact worth keeping — deleting it would make every sweep look like a first one.
        Graph.MarkAbsentExcept(seen, started);

        return new SweepResult(observations, Graph.Nodes.Count, [.. _log]);
    }

    public string Setting(string name) => _setting(_running, name);

    public Task<string?> PromptAsync(ValuePrompt prompt, CancellationToken ct)
        => _prompt is null ? Task.FromResult<string?>(null) : _prompt(prompt, ct);

    public Task<bool> ConfirmAsync(string title, string message, CancellationToken ct)
        => _confirm is null ? Task.FromResult(false) : _confirm(title, message, ct);

    /// <summary>Not granted. A discovery layer that needs administrator rights is a different conversation
    /// from the one this run is having, and handing every probe an elevation channel by default is how a
    /// capability boundary stops being one.</summary>
    public Task<ElevatedResult> RunElevatedAsync(ElevatedRequest request, CancellationToken ct)
        => throw new NotSupportedException(
            "A discovery sweep does not grant elevation. Route an admin action through the page's own "
          + "IShellServices.RunElevatedAsync, where the user can see what is asking.");

    void IProbeLog.Info(string message) => Say("", message);
    void IProbeLog.Warn(string message) => Say("warning: ", message);
    void IProbeLog.Error(string message, Exception? ex)
        => Say("error: ", ex is null ? message : $"{message} — {ex.Message}");

    private void Error(string message, Exception ex) => ((IProbeLog)this).Error(message, ex);

    private void Say(string level, string message) => _log.Add($"{level}{message}");
}
