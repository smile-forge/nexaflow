using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.IO.Network.Actions;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Guard;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Plugins;

namespace Nexaflow.Features.Network.ViewModels;

/// <summary>One device, flattened into what a row shows.</summary>
public sealed partial class DeviceRow : ObservableObject
{
    /// <summary>What to call it. Its own name where it has published one, its address where it has not —
    /// never blank, because a row with no name reads as a row with no device.</summary>
    public required string Name { get; init; }

    /// <summary>Its own icon, already fetched. Null for devices that published none.</summary>
    /// <remarks>
    /// The picture rather than its address, because a picture element given a device-supplied URL fetches
    /// it — past the guard, off-segment, unlogged. What reaches the wire is decided in one place.
    /// </remarks>
    public required ImageSource? Icon { get; init; }

    /// <summary>Its addresses, kept apart and kept as lists.</summary>
    /// <remarks>
    /// Two columns rather than one, because they answer different questions — an IPv4 address is what you
    /// type at something and an IPv6 link-local is what the segment calls it — and lists rather than a
    /// joined string, because a device with three of either is ordinary and a comma-joined cell is a column
    /// nothing can be read out of.
    /// </remarks>
    public required IReadOnlyList<string> IPv4 { get; init; }
    public required IReadOnlyList<string> IPv6 { get; init; }

    public required string Mac { get; init; }
    public required string Detail { get; init; }

    /// <summary>Which layers contributed. The interesting column: a device both probes found is the graph
    /// doing its job, and a device only SSDP found is the protocol engine earning its keep.</summary>
    public required string FoundBy { get; init; }

    public required bool IsNew { get; init; }
    public required Presence Presence { get; init; }

    /// <summary>The graph node behind the row. A grid shows one value per column; the panel reads
    /// everything, so it needs the thing itself rather than a flattening of it.</summary>
    public required DeviceNode Node { get; init; }
}

/// <summary>One fact, with where it came from and how much it is worth.</summary>
/// <remarks>
/// The provenance is the point. A grid cell can only assert; this says which layer said so, how sure it
/// was, when, and whether anything disagreed — which is the shape the graph has had all along and this is
/// the first place it is visible.
/// </remarks>
public sealed class FactRow
{
    public required string Label { get; init; }
    public required string Value { get; init; }

    /// <summary>Where it came from, how sure, and how long ago — one line, so it can wrap.</summary>
    public required string Provenance { get; init; }

    public required bool Contested { get; init; }
}

/// <summary>
/// One tab of the panel.
/// </summary>
/// <remarks>
/// Every tab is a list of facts, which falls out of what an action is: a ping establishes reachability and
/// a round-trip time, and those are facts of the same kind discovery produces. So a run report and a
/// device history need no separate shape — what differs is which facts, and whether the tab can be closed.
/// </remarks>
public sealed partial class PanelTab(string title, string note, bool canClose,
                                     Action<PanelTab>? close = null) : ObservableObject
{
    public string Title { get; } = title;
    public string Note { get; } = note;
    public bool CanClose { get; } = canClose;
    public ObservableCollection<FactRow> Rows { get; } = [];

    [RelayCommand]
    private void Close() => close?.Invoke(this);
}

/// <summary>One action, already bound to the device it was offered for.</summary>
public sealed partial class ActionRow(IDeviceAction action, Func<IDeviceAction, Task> run) : ObservableObject
{
    public IDeviceAction Action { get; } = action;
    public string DisplayName => Action.DisplayName;
    public string Icon => Action.Icon;
    public string Description => Action.Description;
    public bool IsDestructive => Action.IsDestructive;

    [RelayCommand]
    private Task Run() => run(Action);
}

/// <summary>
/// The discovery page.
/// </summary>
/// <remarks>
/// <para>
/// It knows about no protocol, no probe and no action. Layers and actions both arrive as subfeature
/// handles; what the layers find goes into one <see cref="DeviceGraph"/>, which decides when two findings
/// are one device, and the actions are offered for whichever device is selected. A new capability of either
/// kind is a new assembly, and — for a discovery that is merely request and response — a new JSON file.
/// </para>
/// <para>
/// The guard is given the adapter list before anything runs, so the set of legal targets is exactly the set
/// of segments this machine is on. Nothing here can widen that, and neither can a probe or an action.
/// </para>
/// </remarks>
public sealed partial class NetworkViewModel : ObservableObject
{
    private readonly IReadOnlyList<ISubfeatureHandle<INetworkProbe>> _layers;
    private readonly IReadOnlyList<ISubfeatureHandle<IDeviceAction>> _actions;
    private readonly IShellServices _shell;
    private readonly NetworkGuard _guard = new();

    /// <summary>
    /// Everything known about these segments, kept for the life of the page.
    /// </summary>
    /// <remarks>
    /// One graph rather than one per sweep: a device seen last time and missing now is <i>absent</i> rather
    /// than forgotten, and a fact an action established has somewhere to live until the next discovery
    /// either confirms or supersedes it.
    /// </remarks>
    private readonly DeviceGraph _graph = new();

    public NetworkViewModel(IReadOnlyList<ISubfeatureHandle<INetworkProbe>> layers,
                            IReadOnlyList<ISubfeatureHandle<IDeviceAction>> actions,
                            IShellServices shell)
    {
        _layers = layers;
        _actions = actions;
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

    /// <summary>What the panel is about. Null when nothing is selected.</summary>
    [ObservableProperty] private DeviceRow? _selected;

    /// <summary>The panel tabs. The first is what discovery knows; the rest are what actions found.</summary>
    public ObservableCollection<PanelTab> Tabs { get; } = [];

    public ObservableCollection<ActionRow> Actions { get; } = [];

    [ObservableProperty] private PanelTab? _activeTab;

    /// <summary>
    /// How much room the panel's column takes, zero when there is nothing to put in it.
    /// </summary>
    /// <remarks>
    /// A column keeps its width whatever its child does, so collapsing the panel alone left a blank band
    /// the width of the panel — which reads as an empty pane rather than as no pane. Two-way, so a drag on
    /// the splitter writes back here and the width the reader chose survives moving between devices.
    /// </remarks>
    [ObservableProperty] private GridLength _panelWidth = new(0);
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isSweeping;

    // ── The panel ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the panel for whatever is selected.
    /// </summary>
    /// <remarks>
    /// The actions are recomputed rather than filtered at render time, because <c>AppliesTo</c> is the whole
    /// contract: a device that has published no web address is not offered a button for one, so the user
    /// never meets a control that cannot do anything.
    /// </remarks>
    partial void OnSelectedChanged(DeviceRow? value)
    {
        Tabs.Clear();
        Actions.Clear();
        ActiveTab = null;

        if (value is null)
        {
            PanelWidth = new GridLength(0);
            return;
        }

        if (PanelWidth.Value <= 0) PanelWidth = new GridLength(360);

        var discovery = new PanelTab("Discovery", "", canClose: false);
        Fill(discovery, value.Node, value.Node.Facts.Where(f => f.SupersededUtc is null));
        Tabs.Add(discovery);
        ActiveTab = discovery;

        foreach (var handle in _actions)
            if (handle.Value.AppliesTo(value.Node)) Actions.Add(new ActionRow(handle.Value, RunActionAsync));
    }

    /// <summary>Puts facts into a tab, grouped by layer and stable within it.</summary>
    private static void Fill(PanelTab tab, DeviceNode node, IEnumerable<DeviceFact> facts)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var fact in facts
                     .OrderBy(f => f.Layer, StringComparer.Ordinal)
                     .ThenBy(f => f.Key.ToString(), StringComparer.Ordinal))
        {
            var whence = fact.SourceDetail.Length > 0
                ? $"{fact.SourceProbe} — {fact.SourceDetail}"
                : fact.SourceProbe;

            tab.Rows.Add(new FactRow
            {
                Label = Named(fact.Key),
                Value = Shown(fact),
                Provenance = $"{whence} · {fact.Confidence} · {Ago(now - fact.ObservedUtc)}",
                Contested = node.IsContested(fact.Key),
            });
        }
    }

    /// <summary>What to call a fact, and an unblessed key keeps its own name rather than vanishing.</summary>
    private static string Named(FactKey key)
        => FactOntology.IsKnown(key) ? FactOntology.Describe(key).DisplayName : key.ToString();

    /// <summary>What to print. Octets get a summary — a fact list is not the place for hex.</summary>
    private static string Shown(DeviceFact fact)
        => fact.Value.Kind == FactValueKind.Bytes
            ? $"{fact.Value.Bytes?.Length ?? 0} bytes"
            : Unit(fact);

    private static string Unit(DeviceFact fact)
        => FactOntology.IsKnown(fact.Key) && FactOntology.Describe(fact.Key).Unit is { Length: > 0 } unit
            ? $"{fact.Value.Text} {unit}"
            : fact.Value.Text;

    private static string Ago(TimeSpan since)
        => since.TotalSeconds < 90 ? $"{since.TotalSeconds:0}s ago"
         : since.TotalMinutes < 90 ? $"{since.TotalMinutes:0}m ago"
         : $"{since.TotalHours:0}h ago";

    // ── Doing something to one of them ────────────────────────────────────────

    /// <summary>
    /// Runs one action against the selected device and folds anything it learned back into the graph.
    /// </summary>
    /// <remarks>
    /// An action is another way of finding out about a device, so what it returns goes in through the same
    /// door a probe's observation does. A ping that answers becomes a fact with a two-minute life, shown
    /// beside the ones ARP and SSDP contributed and saying which of them said so.
    /// </remarks>
    private async Task RunActionAsync(IDeviceAction action)
    {
        if (Selected is not { } target) return;

        // Anything that changes a device, or makes real noise on the segment, is agreed to first. A ping is
        // neither; a scan would be both.
        if (action.IsDestructive || action.Cost >= ProbeCost.Sweep)
            if (!await _shell.ConfirmAsync(action.DisplayName, action.Description)) return;

        // A fresh budget per click: a user gesture is not part of a sweep's allowance and must not be
        // refused because a discovery earlier spent it.
        var host = new ActionHost(new UdpTransport(_guard, new RunBudget()), _shell, Log);

        try
        {
            var result = await Task.Run(() => action.PerformAsync(target.Node, host, CancellationToken.None))
                                   .ConfigureAwait(false);

            await _shell.RunOnUiAsync(() => Report(action, target, result));
        }
        catch (Exception ex)
        {
            await _shell.RunOnUiAsync(() =>
                Show(new PanelTab(action.DisplayName, $"Failed: {ex.Message}", canClose: true, Drop)));
        }
    }

    /// <summary>
    /// Puts what an action came back with into a tab of its own.
    /// </summary>
    /// <remarks>
    /// A tab rather than a line, because a line was where the result went to die: the previous version set
    /// one and rebuilt the panel two statements later, which cleared it before it had been drawn. A ping
    /// did its work, established two facts, and from the outside looked like a button that does nothing.
    /// </remarks>
    private void Report(IDeviceAction action, DeviceRow target, DeviceActionResult result)
    {
        var tab = new PanelTab(action.DisplayName, result.Message, canClose: true, Drop);

        if (result.Learned is not null)
        {
            // Into the graph first, so what was learned belongs to the device and is dated, then shown
            // here. Discovery is refilled too — a fact learned by an action belongs in the history.
            _graph.Observe(result.Learned);
            Fill(tab, target.Node, result.Learned.Facts);

            if (Tabs.FirstOrDefault(t => !t.CanClose) is { } discovery)
            {
                discovery.Rows.Clear();
                Fill(discovery, target.Node, target.Node.Facts.Where(f => f.SupersededUtc is null));
            }
        }

        Show(tab);
    }

    /// <summary>Shows a tab, replacing an earlier one from the same action rather than stacking them.</summary>
    private void Show(PanelTab tab)
    {
        if (Tabs.FirstOrDefault(t => t.CanClose && t.Title == tab.Title) is { } already) Tabs.Remove(already);

        Tabs.Add(tab);
        ActiveTab = tab;
    }

    private void Drop(PanelTab tab)
    {
        Tabs.Remove(tab);
        ActiveTab = Tabs.FirstOrDefault();
    }

    /// <summary>
    /// What an action is handed, and the only place the shell shows through.
    /// </summary>
    /// <remarks>
    /// Granted by the page rather than found by the action, which is the containment the probes get too: an
    /// action reaches the wire through the guard and the desktop through one method, and has no route to
    /// either besides what is on this object.
    /// </remarks>
    private sealed class ActionHost(IGuardedTransport transport, IShellServices shell,
                                    ObservableCollection<string> log) : IDeviceActionHost, IProbeLog
    {
        public IGuardedTransport Transport => transport;
        public IProbeLog Log => this;

        // "Html" by name rather than by reference: a feature may not reference another feature, and a page
        // kind is the string the shell resolves one by.
        public Task OpenAsync(string url, CancellationToken ct)
            => shell.RunOnUiAsync(() =>
                   shell.OpenTab("Html", new Dictionary<string, string> { ["path"] = url }));

        public Task<bool> ConfirmAsync(string title, string message, CancellationToken ct)
            => shell.ConfirmAsync(title, message, ct);

        void IProbeLog.Info(string m) => Say(m);
        void IProbeLog.Warn(string m) => Say("warning: " + m);
        void IProbeLog.Error(string m, Exception? ex)
            => Say($"error: {m}{(ex is null ? "" : " — " + ex.Message)}");

        private void Say(string m) => shell.RunOnUiAsync(() => log.Add(m));
    }

    // ── Discovery ─────────────────────────────────────────────────────────────

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
                var was = Selected?.Node.Id;

                Devices.Clear();
                foreach (var row in rows) Devices.Add(row);

                Log.Clear();
                foreach (var m in messages) Log.Add(m);

                Status = summary;

                // Keep the panel on whatever it was on. A sweep that silently deselected would throw away
                // what the user was reading every time it refreshed.
                Selected = was is null ? null : Devices.FirstOrDefault(d => d.Node.Id == was);
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
        var run = new DiscoveryRun(Adapters, new UdpTransport(_guard, new RunBudget()), graph: _graph);

        var result = await run.SweepAsync(probes, CancellationToken.None).ConfigureAwait(false);

        var rows = _graph.Nodes
            .Where(IsADevice)
            .OrderByDescending(n => n.IsNew)
            .ThenBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(Row)
            .ToList();

        string summary = rows.Count == 0
            ? "Nothing answered. The neighbour table may be empty on a quiet network, and some adapters "
            + "refuse a multicast join — the log says which."
            : $"{rows.Count} device(s) from {result.Observations} observation(s) "
            + $"across {probes.Count} layer(s).";

        return (rows, [.. result.Log], summary);
    }

    /// <summary>
    /// Whether a graph node is something that was <b>found</b>, rather than somewhere a finding pointed.
    /// </summary>
    /// <remarks>
    /// <c>DeviceGraph.ResolveEdge</c> mints a stub for the far end of an edge nothing has described yet —
    /// deliberately, so the topology is not full of holes. A stub has an identity and no facts, and it is
    /// not a discovery: the router's IPv6 link-local turned up as one on the first real run. A topology view
    /// will want these; a list of what is on the network does not.
    /// </remarks>
    private static bool IsADevice(DeviceNode node) => node.Facts.Count > 0;

    private static DeviceRow Row(DeviceNode node)
    {
        // Which layers contributed, deduplicated and in a stable order. This is the column the whole
        // exercise is about: 'network.arp, network.ssdp' on one row is two independent findings that the
        // graph decided were one device.
        var sources = node.Facts.Select(f => f.SourceProbe).Distinct().OrderBy(s => s, StringComparer.Ordinal);

        var v4 = Addresses(node, "ipv4");

        return new DeviceRow
        {
            // A friendly name where UPnP gave one, and the address otherwise. Leaving it blank was worse
            // than repeating the address: a row with nothing in its first column reads as a broken row.
            Name = node.Best(new FactKey("name", "hostname"))?.Value.Text is { Length: > 0 } named
                ? named
                : node.DisplayName,
            Icon = Picture(node.Best(new FactKey("dev", "icon"))?.Value.Bytes),
            IPv4 = v4,
            IPv6 = Addresses(node, "ipv6"),
            Mac = node.Best(new FactKey("link", "mac"))?.Value.Text ?? "—",
            Detail = node.Best(new FactKey("dev", "firmware"))?.Value.Text
                  ?? node.Best(new FactKey("svc", "type"))?.Value.Text
                  ?? node.Best(new FactKey("dev", "class"))?.Value.Text
                  ?? "",
            FoundBy = string.Join(", ", sources),
            IsNew = node.IsNew,
            Presence = node.Presence,
            Node = node,
        };
    }

    /// <summary>Octets a probe already fetched, decoded for display. Null when there are none or when
    /// they are not a picture — a device may serve anything at the address it advertised.</summary>
    private static ImageSource? Picture(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(bytes);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is NotSupportedException or ArgumentException or IOException)
        {
            return null;
        }
    }

    /// <summary>Every address of one family, in the order they were observed.</summary>
    private static IReadOnlyList<string> Addresses(DeviceNode node, string family)
        => [.. node.AllOf(new FactKey("net", family)).Select(f => f.Value.Text).Distinct()];
}
