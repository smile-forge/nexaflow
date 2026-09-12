using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Probes;
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

    /// <summary>Everything one run sends in a second, whatever it is sent to. A backstop rather than a
    /// setting: the per-target limit cannot see an address sweep, which touches each target once.</summary>
    public int MaxPacketsPerSecondPerRun { get; init; } = 100;

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
    private readonly List<(string AdapterId, string Segment)> _sweepNetworks = [];

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

    /// <summary>
    /// The networks the user has agreed may be swept: an adapter <i>on a subnet</i>, not an adapter.
    /// </summary>
    /// <remarks>
    /// Both halves, because consent to one is not consent to the other. Keyed by the card alone, a laptop's
    /// Wi-Fi allowed at home would sweep the office the next morning — the network an intrusion-detection
    /// system is watching. And the card is not the probe's to choose: an echo carries no source address, so
    /// the route table picks which adapter it leaves by, which is why <see cref="Evaluate"/> asks every
    /// adapter on the target's subnet rather than the one a probe happened to be running for.
    /// </remarks>
    public void SetSweepConsent(IEnumerable<(string AdapterId, string Segment)> networks)
    {
        lock (_lock)
        {
            _sweepNetworks.Clear();
            _sweepNetworks.AddRange(networks);
        }
    }

    /// <summary>Decides one send. Pure with respect to the counters — <see cref="RunBudget.Admit"/> is how a
    /// send is decided and booked together.</summary>
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
        else if (IsSubnetAddress(intent.Target))
        {
            // Both ends of a subnet reach every host on it. A send meant for all of them has to say it is a
            // broadcast, so the broadcast rules — and a draft's ban on them — are the ones that apply.
            return GuardDecision.Deny(GuardRefusal.Forbidden,
                $"{intent.Target} is the network or broadcast address of a local subnet, which reaches every "
                + "host on it. A send meant for all of them has to say it is a broadcast.");
        }

        // ── Sweeps ───────────────────────────────────────────────────────────
        if (intent.Cost >= ProbeCost.Sweep && !SweepPermitted(intent.Target, out var network))
            return GuardDecision.Deny(GuardRefusal.SweepNotPermitted,
                $"Sweeping {network} is not allowed. Allow it for this network in Options → Network.");

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

        if (budget.PacketsThisSecond >= _limits.MaxPacketsPerSecondPerRun)
            return GuardDecision.Deny(GuardRefusal.RateLimited,
                $"This run is sending too fast ({_limits.MaxPacketsPerSecondPerRun}/s).");

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

    /// <summary>True for the first and last address of a local IPv4 subnet — its network address and its
    /// directed broadcast — where the subnet is large enough to have them: in a /31 or a /32 every address
    /// is a host.</summary>
    private bool IsSubnetAddress(IPAddress target)
    {
        lock (_lock)
            foreach (var adapter in _adapters)
            {
                if (adapter.IsLoopbackOrTunnel) continue;
                foreach (var addr in adapter.Addresses)
                    if (addr.PrefixLength <= 30 && addr.Contains(target)
                        && (target.Equals(addr.Network) || target.Equals(addr.DirectedBroadcast)))
                        return true;
            }
        return false;
    }

    /// <summary>True when every adapter on the target's subnet has consent for that subnet.</summary>
    private bool SweepPermitted(IPAddress target, out string network)
    {
        network = target.ToString();

        lock (_lock)
        {
            var on = _adapters
                .Where(a => !a.IsLoopbackOrTunnel && a.Addresses.Any(x => x.Contains(target)))
                .ToList();

            if (on.Count == 0) return false;

            network = on[0].SegmentId;
            return on.All(a => _sweepNetworks.Any(n =>
                string.Equals(n.AdapterId, a.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(n.Segment, a.SegmentId, StringComparison.Ordinal)));
        }
    }

    /// <summary>True for a directed broadcast of one of our prefixes, or a multicast group that cannot
    /// leave the local network by its own definition. <c>255.255.255.255</c> is deliberately excluded: it
    /// is not attributable to a segment.</summary>
    private bool IsLocalBroadcastOrMulticast(IPAddress target)
    {
        if (IsLocalScopeMulticast(target)) return true;

        lock (_lock)
            foreach (var adapter in _adapters)
            {
                if (adapter.IsLoopbackOrTunnel) continue;
                foreach (var addr in adapter.Addresses)
                    if (addr.DirectedBroadcast is { } b && b.Equals(target)) return true;
            }
        return false;
    }

    /// <summary>
    /// Multicast that is confined to the local network by the address itself rather than by a router's
    /// configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two IPv4 blocks, and the second was missing until the first real caller wanted it. <b>224.0.0.0/24</b>
    /// is the link-local control block — RFC 5771 §4: a router must not forward it, whatever the TTL — and
    /// holds mDNS (224.0.0.251) and LLMNR (224.0.0.252). <b>239.0.0.0/8</b> is the administratively scoped
    /// block, RFC 2365 §4: it does not leave the administrative domain it was sent in. SSDP lives there, at
    /// 239.255.255.250, and the comment on the old rule claimed SSDP while the rule excluded it.
    /// </para>
    /// <para>
    /// The widening does not extend reach. Both blocks are unroutable off-site by their own specifications,
    /// which is the same property 224.0.0.0/24 was admitted for; what is still refused is globally scoped
    /// multicast (224.0.1.0 through 238.255.255.255), which a router will happily carry off the premises.
    /// </para>
    /// </remarks>
    private static bool IsLocalScopeMulticast(IPAddress target)
    {
        if (target.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = target.GetAddressBytes();
            return (b[0] == 224 && b[1] == 0 && b[2] == 0) || b[0] == 239;
        }
        if (target.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = target.GetAddressBytes();
            return b[0] == 0xFF && (b[1] & 0x0F) is 0x01 or 0x02;   // interface- / link-local scope
        }
        return false;
    }
}
