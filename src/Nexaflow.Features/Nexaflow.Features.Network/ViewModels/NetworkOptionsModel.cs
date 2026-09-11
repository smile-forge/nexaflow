using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.IO.Network.Adapters;

namespace Nexaflow.Features.Network.ViewModels;

/// <summary>One adapter, as Options → Network shows it.</summary>
public sealed partial class AdapterOption : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>The subnet it is on now — what a sweep permission given here is for.</summary>
    public required string Segment { get; init; }

    /// <summary>False for an adapter with no IPv4 subnet to sweep.</summary>
    public required bool CanSweep { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SweepAvailable))]
    private bool _use;

    [ObservableProperty] private bool _allowSweep;

    /// <summary>A sweep is offered only through an adapter that is in use and has a subnet.</summary>
    public bool SweepAvailable => Use && CanSweep;
}

/// <summary>
/// The Options → Network editor's state, kept apart from the config until Save.
/// </summary>
/// <remarks>
/// <para>
/// An edit model rather than bindings onto the config, because the Options panel hands a custom control the
/// <i>live</i> config: bound directly, a change the user then cancelled would already have happened.
/// </para>
/// <para>
/// <see cref="Apply"/> writes only what this editor shows. The page records which layers are on in the same
/// object, and an adapter that is not plugged in right now keeps whatever was decided about it — as does a
/// sweep permission for an adapter on some other network than the one it is on today.
/// </para>
/// </remarks>
public sealed partial class NetworkOptionsModel : ObservableObject
{
    private readonly NetworkConfig _config;
    private readonly string _saved0;
    private string _saved;

    public NetworkOptionsModel(NetworkConfig config, IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        _config = config;

        foreach (var adapter in adapters.Where(a => !a.IsLoopbackOrTunnel))
        {
            bool hasSubnet = adapter.Addresses.Any(x => x.Address.AddressFamily == AddressFamily.InterNetwork);

            var row = new AdapterOption
            {
                Id = adapter.Id,
                Name = adapter.Name,
                Description = adapter.Description,
                Segment = hasSubnet ? adapter.SegmentId : "",
                CanSweep = hasSubnet,
                Use = !config.IsExcluded(adapter.Id),
                AllowSweep = hasSubnet && config.SweepNetworks.Any(n => Same(n, adapter.Id, adapter.SegmentId)),
            };

            row.PropertyChanged += (_, _) => Touched();
            Adapters.Add(row);
        }

        Enabled = config.Enabled;
        MaxPacketsPerRun = config.MaxPacketsPerRun.ToString(CultureInfo.InvariantCulture);
        MaxRunSeconds = config.MaxRunSeconds.ToString(CultureInfo.InvariantCulture);

        _saved = _saved0 = Fingerprint();
    }

    public ObservableCollection<AdapterOption> Adapters { get; } = [];

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _maxPacketsPerRun = "";
    [ObservableProperty] private string _maxRunSeconds = "";

    public bool HasChanges => Fingerprint() != _saved;
    public bool IsValid => Packets() is not null && Seconds() is not null;

    /// <summary>Shown while a limit is out of range, so a Save that will not happen says why.</summary>
    public bool ShowLimitsHint => !IsValid;

    public string LimitsHint =>
        $"A run may send {NetworkConfig.FewestPacketsPerRun}–{NetworkConfig.MostPacketsPerRun} packets and "
      + $"take {NetworkConfig.ShortestRunSeconds}–{NetworkConfig.LongestRunSeconds} seconds.";

    /// <summary>Raised whenever <see cref="HasChanges"/> or <see cref="IsValid"/> may have moved.</summary>
    public event EventHandler? Changed;

    partial void OnEnabledChanged(bool value) => Touched();
    partial void OnMaxPacketsPerRunChanged(string value) => Touched();
    partial void OnMaxRunSecondsChanged(string value) => Touched();

    /// <summary>Writes what this editor shows into the config, and nothing else.</summary>
    public void Apply()
    {
        if (!IsValid) return;

        _config.Enabled = Enabled;
        _config.MaxPacketsPerRun = Packets()!.Value;
        _config.MaxRunSeconds = Seconds()!.Value;

        _config.ExcludedAdapterIds =
        [
            .. _config.ExcludedAdapterIds.Where(id => !Adapters.Any(
                   a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))),
            .. Adapters.Where(a => !a.Use).Select(a => a.Id),
        ];

        // Only the (adapter, subnet) pairs on screen are this editor's to change. A permission for the same
        // card on another subnet — the office, seen from home — stays exactly as it was.
        _config.SweepNetworks =
        [
            .. _config.SweepNetworks.Where(n => !Adapters.Any(a => a.CanSweep && Same(n, a.Id, a.Segment))),
            .. Adapters.Where(a => a.AllowSweep && a.SweepAvailable)
                       .Select(a => new SweepNetwork { AdapterId = a.Id, Segment = a.Segment }),
        ];

        _saved = Fingerprint();
        Touched();
    }

    private int? Packets()
        => Within(MaxPacketsPerRun, NetworkConfig.FewestPacketsPerRun, NetworkConfig.MostPacketsPerRun);

    private int? Seconds()
        => Within(MaxRunSeconds, NetworkConfig.ShortestRunSeconds, NetworkConfig.LongestRunSeconds);

    private static int? Within(string text, int least, int most)
        => int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
           && n >= least && n <= most
            ? n
            : null;

    private static bool Same(SweepNetwork n, string adapterId, string segment)
        => string.Equals(n.AdapterId, adapterId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(n.Segment, segment, StringComparison.Ordinal);

    private string Fingerprint()
    {
        List<string> parts = [Enabled ? "on" : "off", MaxPacketsPerRun, MaxRunSeconds];
        parts.AddRange(Adapters.Select(a => $"{a.Id}:{a.Use}:{a.AllowSweep}"));
        return string.Join("|", parts);
    }

    private void Touched()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ShowLimitsHint));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
