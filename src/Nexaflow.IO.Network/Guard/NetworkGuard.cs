using Nexaflow.IO.Network.Adapters;
using System.Net;
using System.Net.Sockets;

namespace Nexaflow.IO.Network.Guard;

/// <summary>Process-wide ceilings a protocol document cannot raise. Declared limits inside a document are
/// checked <i>as well</i>, never <i>instead</i>.</summary>
public sealed class GuardLimits
{
    public int MaxPacketsPerRun { get; init; } = 512;
    public long MaxBytesPerRun { get; init; } = 1024 * 1024;
    public TimeSpan MaxRunDuration { get; init; } = TimeSpan.FromSeconds(60);
    public int MaxPacketsPerSecondPerTarget { get; init; } = 20;
    public int MaxBroadcastsPerSecondPerAdapter { get; init; } = 2;

    /// <summary>An unreviewed AI draft gets a far smaller budget than anything else — enough to prove a
    /// protocol works, nowhere near enough to sweep or flood.</summary>
    public int MaxPacketsPerDraftRun { get; init; } = 8;
}

/// <summary>
/// The only code in the system that decides whether bytes may leave the machine.
///
/// <para>
/// Containment here is <b>structural before it is procedural</b>: the protocol engine lives in a leaf with
/// no socket API in scope at all, so it cannot open one even by accident. It produces bytes and an intent;
/// this decides. There is no second path.
/// </para>
///
/// <para>
/// The rules are deliberately conservative about one thing above all — an address the model invented is
/// never a legal target. A send may only go to a device already in the graph on a locally attached prefix,
/// to a local adapter's directed broadcast, or to something the user typed themselves.
/// </para>
/// </summary>
public sealed class NetworkGuard(GuardLimits? limits = null)
{
    private readonly GuardLimits _limits = limits ?? new GuardLimits();
    private readonly object _lock = new();

    private IReadOnlyList<NetworkAdapterInfo> _adapters = [];
    private HashSet<IPAddress> _localAddresses = new();
    private readonly HashSet<string> _userApprovedTargets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The kill switch. False makes every send refuse, discovery included.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Adapters the user has <b>not</b> excluded. Excluding an adapter removes it from the allow-list as
    /// well as from the UI — a VPN or Hyper-V adapter the user hid must not remain a legal target, or
    /// hiding it would be cosmetic.
    /// </summary>
    public void SetAdapters(IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        lock (_lock)
        {
            _adapters = adapters;
            _localAddresses = adapters
                .SelectMany(a => a.Addresses.Select(x => x.Address))
                .ToHashSet();
        }
    }

    /// <summary>Records a target the user typed in this session. Only a user gesture may add one — a
    /// document, and therefore a model, can never widen its own allow-list.</summary>
    public void ApproveUserTarget(IPAddress address)
    {
        lock (_lock) _userApprovedTargets.Add(address.ToString());
    }

    /// <summary>Decides one send. Pure with respect to the counters — call <see cref="RunBudget.Record"/>
    /// on the returned budget only when the send actually happens.</summary>
    public GuardDecision Evaluate(SendIntent intent, RunBudget budget)
    {
        if (!Enabled)
            return GuardDecision.Deny(GuardRefusal.Disabled,
                "Network actions are switched off. Turn them back on in Options → Network.");

        // ── Layer ────────────────────────────────────────────────────────────
        if (intent.Layer is SendLayer.RawIp or SendLayer.Ethernet)
            return GuardDecision.Deny(GuardRefusal.LayerNotPermitted,
                $"{intent.Layer} sends require elevation and are not enabled in this release. "
                + "Use a UDP or TCP layer, or the built-in icmp/tcpConnect probes.");

        // ── The local machine is never a target ──────────────────────────────
        if (IPAddress.IsLoopback(intent.Target))
            return GuardDecision.Deny(GuardRefusal.TargetIsLocalMachine,
                "Loopback is never a permitted target.");

        lock (_lock)
        {
            if (_localAddresses.Contains(intent.Target))
                return GuardDecision.Deny(GuardRefusal.TargetIsLocalMachine,
                    $"{intent.Target} is an address of this machine; sending to it is not permitted.");
        }

        if (intent.Target.Equals(IPAddress.Any) || intent.Target.Equals(IPAddress.IPv6Any))
            return GuardDecision.Deny(GuardRefusal.Forbidden, "The unspecified address is not a target.");

        // ── Broadcast / multicast ────────────────────────────────────────────
        if (intent.Broadcast)
        {
            if (intent.Initiator == SendInitiator.AiDraft)
                return GuardDecision.Deny(GuardRefusal.DraftRestriction,
                    "An unreviewed draft may not broadcast. Review and trust the protocol first, then it can.");

            if (!IsLocalBroadcastOrMulticast(intent.Target))
                return GuardDecision.Deny(GuardRefusal.Forbidden,
                    $"{intent.Target} is not a broadcast address of a locally attached network. "
                    + "Broadcasting to a remote prefix is never permitted.");

            if (budget.BroadcastsThisSecond >= _limits.MaxBroadcastsPerSecondPerAdapter)
                return GuardDecision.Deny(GuardRefusal.RateLimited,
                    $"Broadcast rate limit reached ({_limits.MaxBroadcastsPerSecondPerAdapter}/s per adapter).");
        }
        else if (!IsPermittedUnicastTarget(intent.Target))
        {
            return GuardDecision.Deny(GuardRefusal.TargetNotLocal,
                $"{intent.Target} is not on a locally attached network and was not entered by you. "
                + "Only devices on your own network segments can be contacted.");
        }

        // ── Budgets ──────────────────────────────────────────────────────────
        int packetCap = intent.Initiator == SendInitiator.AiDraft
            ? Math.Min(_limits.MaxPacketsPerDraftRun, _limits.MaxPacketsPerRun)
            : _limits.MaxPacketsPerRun;

        if (budget.Packets + 1 > packetCap)
            return GuardDecision.Deny(GuardRefusal.RunBudgetExceeded,
                $"This run has reached its packet limit ({packetCap}).");

        if (budget.Bytes + intent.ByteCount > _limits.MaxBytesPerRun)
            return GuardDecision.Deny(GuardRefusal.RunBudgetExceeded,
                $"This run has reached its byte limit ({_limits.MaxBytesPerRun:N0}).");

        if (budget.Elapsed > _limits.MaxRunDuration)
            return GuardDecision.Deny(GuardRefusal.RunBudgetExceeded,
                $"This run has exceeded its time limit ({_limits.MaxRunDuration.TotalSeconds:N0}s).");

        if (budget.PacketsThisSecondTo(intent.Target) >= _limits.MaxPacketsPerSecondPerTarget)
            return GuardDecision.Deny(GuardRefusal.RateLimited,
                $"Rate limit reached for {intent.Target} ({_limits.MaxPacketsPerSecondPerTarget}/s).");

        return GuardDecision.Allow();
    }

    /// <summary>True if the address sits on one of our own prefixes, or the user typed it this session.</summary>
    private bool IsPermittedUnicastTarget(IPAddress target)
    {
        lock (_lock)
        {
            if (_userApprovedTargets.Contains(target.ToString())) return true;

            foreach (var adapter in _adapters)
            {
                if (adapter.IsLoopbackOrTunnel) continue;
                foreach (var addr in adapter.Addresses)
                    if (addr.Contains(target)) return true;
            }
        }
        return false;
    }

    /// <summary>True for a directed broadcast of one of our prefixes, or a link-local multicast group.
    /// <c>255.255.255.255</c> is deliberately excluded: it is not attributable to a segment.</summary>
    private bool IsLocalBroadcastOrMulticast(IPAddress target)
    {
        if (IsLinkLocalMulticast(target)) return true;

        lock (_lock)
            foreach (var adapter in _adapters)
            {
                if (adapter.IsLoopbackOrTunnel) continue;
                foreach (var addr in adapter.Addresses)
                    if (addr.DirectedBroadcast is { } b && b.Equals(target)) return true;
            }
        return false;
    }

    private static bool IsLinkLocalMulticast(IPAddress target)
    {
        if (target.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = target.GetAddressBytes();
            return b[0] == 224 && b[1] == 0 && b[2] == 0;      // 224.0.0.0/24 — mDNS, SSDP, LLMNR
        }
        if (target.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = target.GetAddressBytes();
            return b[0] == 0xFF && (b[1] & 0x0F) is 0x01 or 0x02;   // interface- / link-local scope
        }
        return false;
    }
}
