using System.IO;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Hid;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Behaviors;
using Nexaflow.Icons;
using System.Text.Json.Nodes;
using Nexaflow.IO.Usb.Diagnostics;

namespace Nexaflow.Features.UsbProbe.HidTools;

/// <summary>One field of a report layout, as the table shows it.</summary>
public sealed class HidFieldRow(HidField field) : IClipboardText
{
    public string ClipboardText => $"{Kind}\t{ReportId}\t{Name}\t{Bits}\t{Range}";
    public HidField Field { get; } = field;
    public string Kind => Field.Kind switch
    {
        HidReportKind.Input => Str.Get("HidTools.ReportKind.Input"),
        HidReportKind.Output => Str.Get("HidTools.ReportKind.Output"),
        _ => Str.Get("HidTools.ReportKind.Feature"),
    };
    public string ReportId => Field.ReportId == 0 ? "—" : $"0x{Field.ReportId:X2}";
    public string Name => Field.Name;
    public string Bits => Field.BitSize == 0 ? $"× {Field.Count}" : Field.Count == 1 ? $"{Field.BitSize}" : $"{Field.BitSize} × {Field.Count}";
    public string Range => Field.IsButton ? "" : $"{Field.LogicalMin} … {Field.LogicalMax}";
}

/// <summary>A report as the monitor lists it: when, which, the bytes, and what they mean.</summary>
public sealed class HidReportRow(HidReport report) : IClipboardText
{
    public string ClipboardText => $"{Time}\t{Id}\t{Hex}\t{Values}";
    public string Time { get; } = report.At.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
    public string Id { get; } = report.ReportId == 0 ? "" : $"0x{report.ReportId:X2}";
    public string Hex { get; } = UsbHex.Format(report.Bytes);
    public string Values { get; } = string.Join(" · ", report.Values.Select(v => $"{v.Name} {v.Value}"));
}

/// <summary>
/// H4, HID: a function's collections, each with its report layout decoded by the HID class driver, a live monitor of
/// its input reports, and its feature and output reports on request. Sending a report asks first; reading one does not.
/// </summary>
/// <remarks>
/// Windows keeps keyboards and mice to itself, so for those the monitor is not offered — the layout and the feature
/// reports still are, and the tile says why. The monitor reads off the UI thread and the list is refreshed in batches,
/// so a 1000 Hz mouse does not flood the page.
/// </remarks>
public sealed partial class HidTool : ObservableObject, IUsbTool, IUsbToolDebugDetails
{
    const int ShownReports = 300;
    static readonly TimeSpan Flush = TimeSpan.FromMilliseconds(80);

    readonly UsbFunctionNode _function;
    readonly IHidAccess _access;
    readonly IUsbToolHost _host;
    readonly ConcurrentQueue<HidReport> _arrived = new();
    IHidChannel? _channel;
    CancellationTokenSource? _monitoring;
    Timer? _flush;
    bool _read, _resume;
    int _total;

    internal HidTool(UsbFunctionNode function, IHidAccess access, IUsbToolHost host)
    {
        _function = function;
        _access = access;
        _host = host;
        _status = Str.Get("HidTools.Status.NotRead");
    }

    public string ToolId => "usb.hid";
    public string Key => $"{ToolId}:{_function.InstanceId}";
    public string Title => Str.Get("HidTools.Title");
    public IconRef Icon => IconRef.Fluent("games");
    public string Description => Str.Get("HidTools.Description");
    public string? FunctionInstanceId => _function.InstanceId;
    public UsbToolSize Size => UsbToolSize.Wide;
    public bool IsBusy => IsMonitoring;

    public ObservableCollection<HidCollection> Collections { get; } = [];
    public ObservableCollection<HidFieldRow> Fields { get; } = [];
    public ObservableCollection<HidReportRow> Reports { get; } = [];
    public ObservableCollection<byte> FeatureIds { get; } = [];

    [ObservableProperty] private string _status;
    [ObservableProperty] private HidCollection? _collection;
    [ObservableProperty] private string? _reachNote;
    [ObservableProperty] private bool _canMonitor;
    [ObservableProperty] private bool _canWrite;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy))] private bool _isMonitoring;
    [ObservableProperty] private int _reportCount;
    [ObservableProperty] private byte _featureId;
    [ObservableProperty] private string? _featureResult;
    [ObservableProperty] private string _featureHex = "";
    [ObservableProperty] private string _outputHex = "";
    [ObservableProperty] private string? _sendResult;

    public bool HasFeatures => FeatureIds.Count > 0;
    public bool HasOutput => Collection?.OutputLength > 0;

    public async Task ActivateAsync(CancellationToken ct)
    {
        if (!_read)
        {
            var names = _host.Names;
            var found = await Task.Run(() => _access.CollectionsOf(_function, names), ct);
            _read = true;
            Collections.Clear();
            foreach (var c in found) Collections.Add(c);
            Collection = Collections.FirstOrDefault();
            Status = Collections.Count == 0 ? Str.Get("HidTools.Status.NoCollections") : Str.Format("HidTools.Status.Collections", Collections.Count);
        }
        if (_resume) StartMonitor();
    }

    /// <summary>The monitor is live: shutting the tile stops it, and opening it again starts it again.</summary>
    public void Deactivate()
    {
        _resume = IsMonitoring;
        StopMonitor();
    }

    partial void OnCollectionChanged(HidCollection? value)
    {
        StopMonitor();
        _channel?.Dispose();
        _channel = null;
        Fields.Clear();
        FeatureIds.Clear();
        Reports.Clear();
        ReportCount = _total = 0;
        FeatureResult = SendResult = null;
        if (value is not null)
        {
            foreach (var field in value.Reports.SelectMany(r => r.Fields)) Fields.Add(new HidFieldRow(field));
            foreach (var id in value.Of(HidReportKind.Feature).Select(r => r.ReportId).Distinct()) FeatureIds.Add(id);
            FeatureId = FeatureIds.FirstOrDefault();
        }
        CanMonitor = value is { Reach: not HidReach.LayoutOnly } && value.InputLength > 0;
        CanWrite = value is { Reach: HidReach.ReadWrite };
        ReachNote = value?.Reach switch
        {
            HidReach.LayoutOnly => Str.Get("HidTools.Reach.LayoutOnly"),
            HidReach.ReadOnly => Str.Get("HidTools.Reach.ReadOnly"),
            _ => null,
        };
        OnPropertyChanged(nameof(HasFeatures));
        OnPropertyChanged(nameof(HasOutput));
    }

    IHidChannel Channel() => _channel ??= _access.Open(Collection ?? throw new InvalidOperationException(Str.Get("HidTools.Status.NoCollections")));

    // ── The monitor ────────────────────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void StartMonitor()
    {
        if (IsMonitoring || !CanMonitor) return;
        IHidChannel channel;
        try { channel = Channel(); }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            Status = e.Message;
            return;
        }

        var monitoring = _monitoring = new CancellationTokenSource();
        IsMonitoring = true;
        Status = Str.Get("HidTools.Status.Monitoring");
        _flush = new Timer(_ => _ = _host.RunOnUiAsync(Drain), null, Flush, Flush);
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var report in channel.ReadInputAsync(monitoring.Token)) _arrived.Enqueue(report);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
            {
                await _host.RunOnUiAsync(() => Status = Str.Format("HidTools.Status.Stopped", e.Message));
            }
        });
    }

    [RelayCommand]
    private void StopMonitor()
    {
        if (!IsMonitoring) return;
        _monitoring?.Cancel();
        _monitoring = null;
        _flush?.Dispose();
        _flush = null;
        Drain();
        IsMonitoring = false;
        Status = Str.Format("HidTools.Status.Seen", _total);
    }

    [RelayCommand]
    private void Clear()
    {
        Reports.Clear();
        ReportCount = _total = 0;
    }

    /// <summary>Moves what arrived since the last flush into the list, newest first, keeping the list short.</summary>
    void Drain()
    {
        int added = 0;
        while (_arrived.TryDequeue(out var report))
        {
            Reports.Insert(0, new HidReportRow(report));
            added++;
        }
        while (Reports.Count > ShownReports) Reports.RemoveAt(Reports.Count - 1);
        if (added == 0) return;
        _total += added;
        ReportCount = _total;
    }

    // ── Feature and output reports ─────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ReadFeatureAsync() => FeatureResult = await ReadFeatureCoreAsync(FeatureId, CancellationToken.None);

    async Task<string> ReadFeatureCoreAsync(byte id, CancellationToken ct)
    {
        try
        {
            var report = await Channel().GetFeatureAsync(id, ct);
            var text = $"{UsbHex.Format(report.Bytes)}\n{string.Join(" · ", report.Values.Select(v => $"{v.Name} {v.Value}"))}";
            _host.Log(this, Str.Format("HidTools.Log.ReadFeature", id, UsbHex.Format(report.Bytes)));
            return text;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            return e.Message;
        }
    }

    [RelayCommand]
    private Task SendFeatureAsync() => SendFromTileAsync(FeatureHex, HidReportKind.Feature);

    [RelayCommand]
    private Task SendOutputAsync() => SendFromTileAsync(OutputHex, HidReportKind.Output);

    async Task SendFromTileAsync(string hex, HidReportKind kind)
    {
        if (!HidHex.TryParse(hex, out var bytes, out var error)) { SendResult = error; return; }
        string what = ReportKindLabel(kind);
        if (!await _host.ConfirmAsync(Str.Format("HidTools.Confirm.Title", what), Str.Format("HidTools.Confirm.Message", what, UsbHex.Format(bytes), Collection?.Title ?? ""), CancellationToken.None))
            return;
        SendResult = await SendCoreAsync(bytes, kind, CancellationToken.None);
    }

    async Task<string> SendCoreAsync(byte[] bytes, HidReportKind kind, CancellationToken ct)
    {
        if (!CanWrite) return Str.Get("HidTools.Reach.NoWrite");
        try
        {
            if (kind == HidReportKind.Feature) await Channel().SetFeatureAsync(bytes, ct);
            else await Channel().SetOutputAsync(bytes, ct);
            var done = Str.Format("HidTools.Sent", ReportKindLabel(kind), UsbHex.Format(bytes));
            _host.Log(this, done);
            return done;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException)
        {
            return e.Message;
        }
    }

    /// <summary>A sendable report's kind in the person's words: "feature report", "output report".</summary>
    static string ReportKindLabel(HidReportKind kind) =>
        kind == HidReportKind.Feature ? Str.Get("HidTools.Kind.Feature") : Str.Get("HidTools.Kind.Output");

    // ── The agent ──────────────────────────────────────────────────────────────────────────────────────────

    public string Describe()
    {
        if (!_read) return "Not read yet: open the tile or run 'layout'.";
        var sb = new StringBuilder();
        foreach (var c in Collections)
            sb.AppendLine($"{(c == Collection ? "* " : "  ")}{c.Title}{(c.Product is { } p ? $" — {p}" : "")}: input {c.InputLength} B, output {c.OutputLength} B, feature {c.FeatureLength} B; {c.Reach}");
        if (ReachNote is not null) sb.AppendLine(ReachNote);
        sb.AppendLine(IsMonitoring ? $"Monitoring: {_total} report(s) so far." : $"Not monitoring; {_total} report(s) seen.");
        foreach (var row in Reports.Take(5)) sb.AppendLine($"  {row.Time} {row.Id} {row.Hex} — {row.Values}");
        return sb.ToString();
    }

    public IReadOnlyList<UsbToolOperation> Operations =>
    [
        new("layout", "Every field of every report of the selected collection: kind, report ID, usage, size and range.", false,
            [new UsbToolArgument("collection", "A collection title to select first, e.g. 'Generic Desktop Controls › Mouse'.", Required: false)],
            async (args, ct) =>
            {
                if (!_read) await ActivateAsync(ct);
                if (args.TryGetValue("collection", out var title) && Collections.FirstOrDefault(c => c.Title.Contains(title, StringComparison.OrdinalIgnoreCase)) is { } pick)
                    Collection = pick;
                return Layout();
            }),
        new("listen", "Watches the selected collection's input reports for a few seconds and returns them decoded.", false,
            [new UsbToolArgument("seconds", "How long, 1–10. Default 3.", Required: false)],
            async (args, ct) =>
            {
                if (!_read) await ActivateAsync(ct);
                if (!CanMonitor) return ReachNote ?? Str.Get("HidTools.Status.NoCollections");
                double seconds = args.TryGetValue("seconds", out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 1, 10) : 3;
                int before = _total;
                bool was = IsMonitoring;
                StartMonitor();
                await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
                Drain();
                if (!was) StopMonitor();
                var fresh = Reports.Take(Math.Min(_total - before, 50)).ToList();
                return fresh.Count == 0 ? $"No input in {seconds:0.#} s." : string.Join("\n", fresh.Select(r => $"{r.Time} {r.Id} {r.Hex} — {r.Values}"));
            }),
        new("read_feature", "Asks the device for one feature report and decodes it.", false,
            [new UsbToolArgument("report_id", "The report ID, decimal or 0x-hex; 0 when the collection numbers none.")],
            async (args, ct) => await ReadFeatureCoreAsync(ParseId(args["report_id"]), ct)),
        new("send_feature", "Sends a feature report to the device.", true,
            [new UsbToolArgument("hex", "The report's bytes in hex, its ID first, e.g. '02 01 00'.")],
            (args, ct) => SendHexAsync(args["hex"], HidReportKind.Feature, ct)),
        new("send_output", "Sends an output report to the device — LEDs, rumble, whatever it defines.", true,
            [new UsbToolArgument("hex", "The report's bytes in hex, its ID first.")],
            (args, ct) => SendHexAsync(args["hex"], HidReportKind.Output, ct)),
    ];

    string Layout()
    {
        if (Collection is not { } c) return "No collection.";
        var sb = new StringBuilder($"{c.Title}: input {c.InputLength} B, output {c.OutputLength} B, feature {c.FeatureLength} B\n");
        foreach (var report in c.Reports)
        {
            sb.AppendLine($"{report.Kind} report {(report.ReportId == 0 ? "(unnumbered)" : $"0x{report.ReportId:X2}")}, {report.Bits} bits:");
            foreach (var f in report.Fields) sb.AppendLine($"  {f.Name}: {new HidFieldRow(f).Bits} bits{(f.IsButton ? "" : $", {f.LogicalMin}…{f.LogicalMax}")}");
        }
        return sb.ToString();
    }

    Task<string> SendHexAsync(string hex, HidReportKind kind, CancellationToken ct) =>
        HidHex.TryParse(hex, out var bytes, out var error) ? SendCoreAsync(bytes, kind, ct) : Task.FromResult(error!);

    static byte ParseId(string text) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? byte.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : byte.Parse(text, CultureInfo.InvariantCulture);

    /// <summary>Every collection Windows made of the function as the HID class driver decoded it — its caps, how far it
    /// can be opened, each field of each report — and the preparsed data it was decoded from.</summary>
    public async Task<JsonObject> DebugDetailsAsync(UsbDebugMask mask, CancellationToken ct)
    {
        var names = _host.Names;
        IReadOnlyList<HidCollection> collections = _read ? [.. Collections] : await Task.Run(() => _access.CollectionsOf(_function, names), ct);
        var preparsed = await Task.Run(() => collections.Select(_access.PreparsedData).ToList(), ct);
        return new JsonObject
        {
            ["collections"] = new JsonArray([.. collections.Select((c, i) => (JsonNode)new JsonObject
            {
                ["instanceId"] = mask.Text(c.InstanceId),
                ["devicePath"] = mask.Text(c.DevicePath),
                ["usagePage"] = $"{c.UsagePage:X4}",
                ["usage"] = $"{c.Usage:X4}",
                ["title"] = c.Title,
                ["product"] = mask.Text(c.Product),
                ["inputLength"] = c.InputLength,
                ["outputLength"] = c.OutputLength,
                ["featureLength"] = c.FeatureLength,
                ["reach"] = c.Reach.ToString(),
                ["reports"] = new JsonArray([.. c.Reports.Select(r => (JsonNode)new JsonObject
                {
                    ["kind"] = r.Kind.ToString(),
                    ["reportId"] = r.ReportId,
                    ["bits"] = r.Bits,
                    ["fields"] = new JsonArray([.. r.Fields.Select(f => (JsonNode)new JsonObject
                    {
                        ["name"] = f.Name,
                        ["usagePage"] = $"{f.UsagePage:X4}",
                        ["usages"] = f.UsageMin == f.UsageMax ? $"{f.UsageMin:X4}" : $"{f.UsageMin:X4}-{f.UsageMax:X4}",
                        ["button"] = f.IsButton,
                        ["bitSize"] = f.BitSize,
                        ["count"] = f.Count,
                        ["logical"] = $"{f.LogicalMin}..{f.LogicalMax}",
                        ["absolute"] = f.IsAbsolute,
                        ["linkCollection"] = f.LinkCollection,
                    })]),
                })]),
                ["preparsedData"] = preparsed[i] is { } blob ? UsbHex.Format(blob) : null,
            })]),
        };
    }

    public void Dispose()
    {
        StopMonitor();
        _channel?.Dispose();
        _channel = null;
    }
}

/// <summary>Bytes typed as hex: "02 01 ff", "0201FF" and "0x02,0x01" all read the same.</summary>
public static class HidHex
{
    public static bool TryParse(string text, out byte[] bytes, out string? error)
    {
        var digits = new StringBuilder();
        foreach (var token in text.Split([' ', ',', ';', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token;
            digits.Append(t.Length % 2 == 1 ? "0" + t : t);
        }
        var s = digits.ToString();
        bytes = [];
        if (s.Length == 0) { error = Str.Get("HidTools.Hex.Empty"); return false; }
        if (!s.All(Uri.IsHexDigit)) { error = Str.Get("HidTools.Hex.NotHex"); return false; }
        bytes = Convert.FromHexString(s);
        error = null;
        return true;
    }
}
