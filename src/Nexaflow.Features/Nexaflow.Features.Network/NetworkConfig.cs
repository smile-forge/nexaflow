using Nexaflow.Features.Common;
using Nexaflow.Features.Network.Views;
using Nexaflow.IO.Network.Guard;

namespace Nexaflow.Features.Network;

/// <summary>A network the user has agreed may be swept: one adapter, on one subnet.</summary>
/// <remarks>
/// Both halves, because consent to one is not consent to the other — see
/// <see cref="NetworkGuard.SetSweepConsent"/>. Kept when the adapter moves on, so coming home to the subnet
/// the permission was given for is the same network again, while the office never was.
/// </remarks>
public sealed class SweepNetwork
{
    public string AdapterId { get; set; } = "";

    /// <summary>The subnet, spelled as <c>NetworkAdapterInfo.SegmentId</c> spells it — <c>192.168.1.0/24</c>.</summary>
    public string Segment { get; set; } = "";
}

/// <summary>
/// What the Network page may send, and where — Options → Network.
/// </summary>
/// <remarks>
/// <para>
/// The guard's own settings: the kill switch its refusals already pointed at, which adapters are places
/// anything may be sent, the networks an address sweep may run on, and one run's ceilings. Global rather than
/// per workspace, because they are about this machine's networks — a second workspace must not be a way
/// round a network the user declined to sweep.
/// </para>
/// <para>
/// <see cref="LayerEnabled"/> is the page's own memory of which layers were switched on. The page writes it;
/// the Options editor leaves it alone.
/// </para>
/// </remarks>
[CustomControl(typeof(NetworkOptionsView))]
public sealed class NetworkConfig : IFeatureConfig
{
    public const int FewestPacketsPerRun = 64;
    public const int MostPacketsPerRun = 4096;
    public const int ShortestRunSeconds = 10;
    public const int LongestRunSeconds = 600;

    public string ConfigName   => "network";
    public string FriendlyName => "Network";

    /// <summary>The kill switch. Off, the page still shows what the OS already knows and sends nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Adapters the user stopped using: left out of discovery, and no longer anywhere a send may
    /// go through.</summary>
    public List<string> ExcludedAdapterIds { get; set; } = [];

    public List<SweepNetwork> SweepNetworks { get; set; } = [];

    public int MaxPacketsPerRun { get; set; } = 512;
    public int MaxRunSeconds { get; set; } = 60;

    /// <summary>Which discovery layers are switched on, by subfeature id. A layer with no entry takes its
    /// own default.</summary>
    public Dictionary<string, bool> LayerEnabled { get; set; } = [];

    public bool IsExcluded(string adapterId)
        => ExcludedAdapterIds.Contains(adapterId, StringComparer.OrdinalIgnoreCase);

    /// <summary>The guard's ceilings, clamped — a file edited by hand cannot lift them past what Options
    /// offers.</summary>
    public GuardLimits Limits() => new()
    {
        MaxPacketsPerRun = Math.Clamp(MaxPacketsPerRun, FewestPacketsPerRun, MostPacketsPerRun),
        MaxRunDuration = TimeSpan.FromSeconds(Math.Clamp(MaxRunSeconds, ShortestRunSeconds, LongestRunSeconds)),
    };

    public IReadOnlyList<(string AdapterId, string Segment)> SweptNetworks()
        => [.. SweepNetworks.Select(n => (n.AdapterId, n.Segment))];
}
