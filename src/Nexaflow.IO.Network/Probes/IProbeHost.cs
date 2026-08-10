using Nexaflow.Elevation.Contracts;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Guard;

namespace Nexaflow.IO.Network.Probes;

/// <summary>Diagnostic sink a probe writes to. Surfaces in the feature's audit/diagnostics view, so a
/// probe that found nothing can explain why instead of just returning an empty list.</summary>
public interface IProbeLog
{
    void Info(string message);
    void Warn(string message);

    /// <summary>An error the probe handled. A probe should not throw for an expected failure — a refused
    /// multicast join on a VPN adapter is normal, and an exception there would abort the whole sweep.</summary>
    void Error(string message, Exception? ex = null);
}

/// <summary>A value the probe needs from the user at run time, described well enough that the AI can ask
/// for it in its own words.</summary>
/// <param name="Name">Key.</param>
/// <param name="Description">What to ask. Literally what the model says when it asks.</param>
/// <param name="Secret">True to route through DPAPI-at-rest storage and redact from logs and transcripts.</param>
public sealed record ValuePrompt(string Name, string Description, bool Secret = false);

/// <summary>
/// Everything a probe is allowed to do, granted by the <b>host feature</b> rather than by Core.
///
/// <para>
/// This is the security story in one sentence: Core discovers, orders and constructs plugins but never
/// hands one <c>IShellServices</c>. Capability arrives through <see cref="INetworkProbe.Attach"/>, from the
/// feature that owns the page — so the host can narrow it, withhold it, or revoke it, and a plugin's reach
/// is bounded by what this interface exposes rather than by what it can find.
/// </para>
/// </summary>
public interface IProbeHost
{
    /// <summary>Adapters the user has not excluded. Excluded adapters are absent here <i>and</i> from the
    /// guard's allow-list — hidden and not-a-legal-target are the same decision.</summary>
    IReadOnlyList<NetworkAdapterInfo> Adapters { get; }

    /// <summary>The only route to a socket. Every send passes the guard's target allow-list and volume
    /// ceilings; there is no bypass, because a probe cannot reference a socket type from this leaf.</summary>
    IGuardedTransport Transport { get; }

    /// <summary>Diagnostics.</summary>
    IProbeLog Log { get; }

    /// <summary>This probe's settings, resolved from the host's config against the probe's declared
    /// <see cref="INetworkProbe.Settings"/>. Returns the declared default when unset.</summary>
    string Setting(string name);

    /// <summary>Asks the user for a value. Returns null if they decline.</summary>
    Task<string?> PromptAsync(ValuePrompt prompt, CancellationToken ct);

    /// <summary>Asks the user to confirm something consequential — an opt-in sweep, a first multicast bind
    /// that will raise a firewall prompt.</summary>
    Task<bool> ConfirmAsync(string title, string message, CancellationToken ct);

    /// <summary>Runs a privileged operation through the shell's privilege bridge. One UAC prompt per call
    /// and no persistent elevated channel, so batch rather than loop.</summary>
    Task<ElevatedResult> RunElevatedAsync(ElevatedRequest request, CancellationToken ct);
}
