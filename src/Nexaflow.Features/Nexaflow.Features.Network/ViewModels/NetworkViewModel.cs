using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Guard;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Plugins;

namespace Nexaflow.Features.Network.ViewModels;

/// <summary>One device, flattened into what a row shows.</summary>
public sealed partial class DeviceRow : ObservableObject
{
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required string Mac { get; init; }
    public required string Detail { get; init; }

    /// <summary>Which layers contributed. The interesting column: a device both probes found is the graph
    /// doing its job, and a device only SSDP found is the protocol engine earning its keep.</summary>
    public required string FoundBy { get; init; }

    public required bool IsNew { get; init; }
    public required Presence Presence { get; init; }
}

/// <summary>
/// The discovery page.
/// </summary>
/// <remarks>
/// <para>
/// It knows about no protocol and no probe. Layers arrive as subfeature handles and are asked in turn;
/// what they find goes into one <see cref="DeviceGraph"/>, which decides when two findings are one device.
/// A new discovery layer is a new assembly, and — for anything that is merely request and response — a new
/// JSON file rather than any code at all.
/// </para>
/// <para>
/// The guard is given the adapter list before anything runs, so the set of legal targets is exactly the set
/// of segments this machine is on. Nothing here can widen that, and neither can a probe.
/// </para>
/// </remarks>
public sealed partial class NetworkViewModel : ObservableObject
{
    private readonly IReadOnlyList<ISubfeatureHandle<INetworkProbe>> _layers;
    private readonly IShellServices _shell;
    private readonly NetworkGuard _guard = new();

    public NetworkViewModel(IReadOnlyList<ISubfeatureHandle<INetworkProbe>> layers, IShellServices shell)
    {
        _layers = layers;
        _shell = shell;

        foreach (var layer in layers)
            Layers.Add(new LayerRow(layer.Id, layer.DisplayName, layer.Description, layer.DefaultEnabled));

        Adapters = [.. NetworkAdapters.Usable()];
        _guard.SetAdapters(Adapters);

        Status = Adapters.Count == 0
            ? "No usable network adapter — nothing to discover on."
            : $"{Adapters.Count} adapter(s), {Layers.Count} discovery layer(s). Nothing found yet.";
    }

    /// <summary>One installed discovery layer, and whether the user wants it.</summary>
    public sealed partial class LayerRow(string id, string name, string description, bool enabled)
        : ObservableObject
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public string Description { get; } = description;

        [ObservableProperty] private bool _isEnabled = enabled;
    }

    public IReadOnlyList<NetworkAdapterInfo> Adapters { get; }
    public ObservableCollection<LayerRow> Layers { get; } = [];
    public ObservableCollection<DeviceRow> Devices { get; } = [];
    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isSweeping;

    [RelayCommand(CanExecute = nameof(CanDiscover))]
    private async Task DiscoverAsync()
    {
        IsSweeping = true;
        DiscoverCommand.NotifyCanExecuteChanged();
        Status = "Looking…";

        try
        {
            var chosen = Layers.Where(l => l.IsEnabled).Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
            var probes = _layers.Where(h => chosen.Contains(h.Id)).Select(h => h.Value).ToList();

            // Everything below here is off the UI thread: reading a neighbour table takes milliseconds and
            // an SSDP window takes seconds, and a feature never touches the dispatcher itself.
            var (rows, messages, summary) = await Task.Run(() => SweepAsync(probes)).ConfigureAwait(false);

            await _shell.RunOnUiAsync(() =>
            {
                Devices.Clear();
                foreach (var row in rows) Devices.Add(row);

                Log.Clear();
                foreach (var m in messages) Log.Add(m);

                Status = summary;
            });
        }
        finally
        {
            IsSweeping = false;
            await _shell.RunOnUiAsync(() => DiscoverCommand.NotifyCanExecuteChanged());
        }
    }

    private bool CanDiscover() => !IsSweeping && Adapters.Count > 0 && Layers.Any(l => l.IsEnabled);

    private async Task<(List<DeviceRow> Rows, List<string> Log, string Summary)> SweepAsync(
        IReadOnlyList<INetworkProbe> probes)
    {
        var budget = new RunBudget();
        var transport = new UdpTransport(_guard, budget);
        var run = new DiscoveryRun(Adapters, transport);

        var result = await run.SweepAsync(probes, CancellationToken.None).ConfigureAwait(false);

        var rows = run.Graph.Nodes
            .OrderByDescending(n => n.IsNew)
            .ThenBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(Row)
            .ToList();

        string summary = result.Devices == 0
            ? "Nothing answered. The neighbour table may be empty on a quiet network, and some adapters "
            + "refuse a multicast join — the log says which."
            : $"{result.Devices} device(s) from {result.Observations} observation(s) "
            + $"across {probes.Count} layer(s).";

        return (rows, [.. result.Log], summary);
    }

    private static DeviceRow Row(DeviceNode node)
    {
        var addresses = node.AllOf(new FactKey("net", "ipv4")).Select(f => f.Value.Text)
            .Concat(node.AllOf(new FactKey("net", "ipv6")).Select(f => f.Value.Text))
            .Distinct()
            .ToList();

        // Which layers contributed, deduplicated and in a stable order. This is the column the whole
        // exercise is about: 'network.arp, network.ssdp' on one row is two independent findings that the
        // graph decided were one device.
        var sources = node.Facts.Select(f => f.SourceProbe).Distinct().OrderBy(s => s, StringComparer.Ordinal);

        return new DeviceRow
        {
            Name = node.DisplayName,
            Address = addresses.Count > 0 ? string.Join(", ", addresses) : "—",
            Mac = node.Best(new FactKey("link", "mac"))?.Value.Text ?? "—",
            Detail = node.Best(new FactKey("dev", "firmware"))?.Value.Text
                  ?? node.Best(new FactKey("svc", "type"))?.Value.Text
                  ?? node.Best(new FactKey("dev", "class"))?.Value.Text
                  ?? "",
            FoundBy = string.Join(", ", sources),
            IsNew = node.IsNew,
            Presence = node.Presence,
        };
    }
}
