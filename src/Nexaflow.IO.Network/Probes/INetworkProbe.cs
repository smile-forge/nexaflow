using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Model;

namespace Nexaflow.IO.Network.Probes;

/// <summary>How much traffic a probe puts on the network — what the user is consenting to when they
/// enable it, and what the guard budgets against.</summary>
public enum ProbeCost
{
    /// <summary>Reads an OS table. No packets at all, so it can always run.</summary>
    Passive = 0,

    /// <summary>A handful of multicast or unicast packets — an mDNS browse, an SSDP M-SEARCH.</summary>
    Light = 1,

    /// <summary>One packet per host across a prefix. Visible to an IDS; opt-in per adapter.</summary>
    Sweep = 2,

    /// <summary>Many packets per host — port scanning. Always opt-in, never a default.</summary>
    Heavy = 3,
}

/// <summary>
/// A discovery layer. Implementations ship in their own assembly carrying
/// <c>[Subfeature("network", "&lt;id&gt;")]</c>, reference only this leaf, and are discovered, ordered and
/// lazily loaded by the subfeature framework — the host feature never names them.
///
/// <para>
/// A probe describes what it saw and never decides identity: it emits <see cref="ProbeObservation"/>s and
/// <see cref="DeviceGraph"/> owns correlation. Fifteen plugins each inventing "the same device" is exactly
/// the failure this split prevents.
/// </para>
/// </summary>
public interface INetworkProbe
{
    /// <summary>Stable id, matching the <c>[Subfeature]</c> id and used as
    /// <see cref="DeviceFact.SourceProbe"/> — e.g. <c>network.arp</c>.</summary>
    string ProbeId { get; }

    /// <summary>What this layer contributes, for the settings UI and the AI.</summary>
    string DisplayName { get; }

    /// <summary>How much traffic running it generates.</summary>
    ProbeCost Cost { get; }

    /// <summary>The settings this probe accepts, described rather than typed so the host can render them
    /// generically and the same schema can be handed to the model.</summary>
    IReadOnlyList<ProbeSetting> Settings => [];

    /// <summary>
    /// Grants capability. Called by the <i>host feature</i> after construction — Core never hands a plugin
    /// shell services, so the host can withhold or narrow what a probe may do.
    /// </summary>
    void Attach(IProbeHost host);

    /// <summary>
    /// True if this probe can say anything about <paramref name="adapter"/>. Cheap and pure — no IO.
    /// A wireless-only probe declines a wired adapter here rather than running and finding nothing.
    /// </summary>
    bool AppliesTo(NetworkAdapterInfo adapter) => adapter.IsUsable;

    /// <summary>
    /// Runs one discovery pass over <paramref name="adapter"/>, yielding observations as they arrive so
    /// the UI fills in progressively rather than waiting for the slowest responder.
    /// </summary>
    /// <remarks>
    /// Must honour <paramref name="ct"/> promptly — a user cancelling a sweep expects it to stop, and the
    /// guard's per-run duration ceiling is enforced by cancelling this token.
    /// </remarks>
    IAsyncEnumerable<ProbeObservation> DiscoverAsync(NetworkAdapterInfo adapter, CancellationToken ct);
}
