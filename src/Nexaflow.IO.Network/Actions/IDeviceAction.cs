using Nexaflow.IO.Network.Guard;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;

namespace Nexaflow.IO.Network.Actions;

/// <summary>
/// What an action came to, and anything it learned on the way.
/// </summary>
/// <remarks>
/// <b>An action is another way of finding out about a device.</b> A ping establishes reachability and a
/// round-trip time; fetching a description establishes a name. Those are facts of exactly the kind a probe
/// produces, so they come back as a <see cref="ProbeObservation"/> and go into the same graph through the
/// same door — rather than as a second channel with its own rules about what may be believed.
/// </remarks>
/// <param name="Ok">Whether it did what it said.</param>
/// <param name="Message">What to tell the user, in their terms. Shown whether it worked or not.</param>
/// <param name="Learned">Facts to fold into the device graph, or null when nothing was learned.</param>
public readonly record struct DeviceActionResult(bool Ok, string Message, ProbeObservation? Learned = null)
{
    public static DeviceActionResult Worked(string message, ProbeObservation? learned = null)
        => new(true, message, learned);

    public static DeviceActionResult Failed(string message) => new(false, message);
}

/// <summary>
/// What an action is allowed to do, granted by the page that owns the device list.
/// </summary>
/// <remarks>
/// The same shape and the same reason as <see cref="IProbeHost"/>: capability arrives from the host rather
/// than being found, so an action's reach is bounded by this interface. It is a <b>separate</b> interface
/// because an action may do one thing a probe may not — hand something to the shell to open — and widening
/// the probe's host to cover it would give every discovery layer a way out to the desktop.
/// </remarks>
public interface IDeviceActionHost
{
    /// <summary>The only route to the wire, guard-checked exactly as a probe's is.</summary>
    IGuardedTransport Transport { get; }

    IProbeLog Log { get; }

    /// <summary>Opens a URL wherever the shell opens things. The one capability a probe does not get.</summary>
    Task OpenAsync(string url, CancellationToken ct);

    /// <summary>Asks the user to agree to something consequential. An action that scans, or that talks to
    /// a device it was not invited to talk to, asks first.</summary>
    Task<bool> ConfirmAsync(string title, string message, CancellationToken ct);
}

/// <summary>
/// Something a user can do to one device, offered only where it makes sense.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the same shape as <c>IFileAction</c>, which does this job for files: an action declares
/// itself, says whether it applies to the thing in hand, and performs. The page holds no list of buttons —
/// it renders whatever applies to the selected device, so a new capability is a new assembly and never an
/// edit to the page.
/// </para>
/// <para>
/// <see cref="AppliesTo"/> is what makes "open the management interface, if it has one" expressible as a
/// contract rather than as a condition inside a button's handler. An action that needs a fact the device
/// has not got simply is not offered, and the user never sees a button that cannot work.
/// </para>
/// </remarks>
public interface IDeviceAction
{
    /// <summary>Stable id, e.g. <c>network.ping</c>. What settings and audit entries are keyed by.</summary>
    string ActionId { get; }

    string DisplayName { get; }

    /// <summary>A glyph for the button.</summary>
    string Icon { get; }

    /// <summary>What it does, in the user's words — the tooltip, and what the model reads when it is
    /// deciding whether this is the thing being asked for.</summary>
    string Description { get; }

    /// <summary>
    /// What running it costs the network, in the same vocabulary a probe uses.
    /// </summary>
    /// <remarks>
    /// One scale for both, because the question is the same one: how much noise does this make and does the
    /// user need to have agreed to it. A ping is <see cref="ProbeCost.Light"/>; a port scan is
    /// <see cref="ProbeCost.Heavy"/> and the host is expected to confirm before running one.
    /// </remarks>
    ProbeCost Cost => ProbeCost.Light;

    /// <summary>True for anything that changes the device rather than asking it something. Nothing here is
    /// yet, but a reboot or a configuration write would be, and the host renders those differently.</summary>
    bool IsDestructive => false;

    /// <summary>Whether this device has what the action needs. Called for every action each time the
    /// selection changes, so it must be a cheap look at facts and never touch the network.</summary>
    bool AppliesTo(DeviceNode device);

    Task<DeviceActionResult> PerformAsync(DeviceNode device, IDeviceActionHost host, CancellationToken ct);
}
