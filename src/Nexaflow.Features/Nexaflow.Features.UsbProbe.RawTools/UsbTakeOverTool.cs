using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Icons;
using Nexaflow.IO.Usb.Binding;
using Nexaflow.IO.Usb.Diagnostics;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Plugins;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe.RawTools;

/// <summary>
/// R1 and R2, take-over and restore: one function's driver, what taking it over would stop, and the way back. Taking
/// over puts the function on Windows' own WinUSB driver so raw access reaches it; restoring gives it back the driver
/// it had. Both go through the page's driver broker — one UAC prompt each, in the elevated bridge — and both ask here
/// first. When the driver changes, Windows restarts the function and the page offers its tools again, so this tile is
/// replaced by one that says how it now stands.
/// </summary>
public sealed partial class UsbTakeOverTool : ObservableObject, IUsbDriverTool, IUsbToolDebugDetails
{
    readonly UsbDeviceNode _device;
    readonly UsbFunctionNode _function;
    readonly IUsbToolHost _host;

    internal UsbTakeOverTool(UsbDeviceNode device, UsbFunctionNode function, IUsbToolHost host)
    {
        _device = device;
        _function = function;
        _host = host;
        _status = DriverName;
    }

    public string ToolId => "usb.takeover";
    public string Key => $"{ToolId}:{_function.InstanceId}";
    public string Title => Str.Get("RawTools.TakeOver.Title");
    public IconRef Icon => IconRef.Fluent("wrench");
    public string Description => Str.Get("RawTools.TakeOver.Description");
    public string? FunctionInstanceId => _function.InstanceId;
    public UsbToolSize Size => UsbToolSize.Compact;
    public bool IsBusy => IsWorking;

    /// <summary>The driver Windows has on it now, with where it came from.</summary>
    public string Driver => _function.Driver switch
    {
        { Service: null } => Str.Get("RawTools.TakeOver.NoDriver"),
        var d => string.Join(" · ", new[] { d.Service, d.InfPath, d.Provider, d.Version }.Where(s => !string.IsNullOrEmpty(s))),
    };

    string DriverName => _function.Driver.Service ?? Str.Get("RawTools.TakeOver.NoDriver");

    /// <summary>What stops working while it is taken over, said before the user agrees.</summary>
    public string Impact => TakeOverImpact.Of(_device, _function);

    [ObservableProperty] private string _status;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsIdle))] private bool _isWorking;

    /// <summary>No change is under way, so the buttons may start one.</summary>
    public bool IsIdle => !IsWorking;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsTakenOver), nameof(CanTakeOver))] private UsbBindingRecord? _binding;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanTakeOver))] private string? _refusal;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasOtherBindings))] private int _otherBindings;
    [ObservableProperty] private string? _outcome;

    /// <summary>Nexaflow holds a binding for this function — not cleanly restored, so restore is what it offers.</summary>
    public bool IsTakenOver => Binding is not null;

    public bool CanTakeOver => Binding is null && Refusal is null;

    /// <summary>Bindings Nexaflow holds on other interfaces of this machine — what "Restore all" would also give back.</summary>
    public bool HasOtherBindings => OtherBindings > 0;

    /// <summary>The binding's state in words: taken over, or a bind or restore that did not finish.</summary>
    public string? BindingState => Binding?.State switch
    {
        null => null,
        UsbBindingState.Bound => Str.Format("RawTools.TakeOver.State.Bound", Binding.OriginalService ?? Str.Get("RawTools.TakeOver.NoDriver")),
        UsbBindingState.BindIntent => Str.Get("RawTools.TakeOver.State.BindIntent"),
        UsbBindingState.BindFailed => Str.Get("RawTools.TakeOver.State.BindFailed"),
        UsbBindingState.RestoreIntent => Str.Get("RawTools.TakeOver.State.RestoreIntent"),
        _ => Str.Get("RawTools.TakeOver.State.RestoreFailed"),
    };

    public async Task ActivateAsync(CancellationToken ct) => await RefreshAsync(ct);

    public void Deactivate() { }

    /// <summary>Reads the ledger and the policy again — off the UI thread, as both read the registry and a file.</summary>
    async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            (Binding, OtherBindings, Refusal) = await ReadAsync(ct);
            OnPropertyChanged(nameof(BindingState));
            Status = Binding is not null ? BindingState! : Refusal is not null ? Str.Format("RawTools.TakeOver.Status.Refused", DriverName) : DriverName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Status = Str.Format("RawTools.TakeOver.Status.LedgerUnread", e.Message);
        }
    }

    /// <summary>This function's active binding, how many others the machine has, and — when it holds none — why the
    /// policy would refuse it.</summary>
    async Task<(UsbBindingRecord? Binding, int Others, string? Refusal)> ReadAsync(CancellationToken ct)
    {
        var broker = _host.DriverBroker;
        var (records, refusal) = await Task.Run(() => (broker.GetBindings(), broker.RefusalReason(_function.InstanceId)), ct);
        var active = records.Where(r => r.IsActive).ToList();
        bool Mine(UsbBindingRecord r) => string.Equals(r.InterfaceInstanceId, _function.InstanceId, StringComparison.OrdinalIgnoreCase);
        var binding = active.LastOrDefault(Mine);
        return (binding, active.Count(r => !Mine(r)), binding is null ? refusal : null);
    }

    // ── Commands ───────────────────────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task TakeOverAsync()
    {
        if (!CanTakeOver) return;
        if (!await _host.ConfirmAsync(Str.Get("RawTools.TakeOver.Confirm.Title"),
                Str.Format("RawTools.TakeOver.Confirm.Message", FunctionName, Impact), CancellationToken.None, Str.Get("RawTools.TakeOver.Confirm.Button")))
            return;
        Outcome = await ChangeAsync(broker => broker.BindGenericAsync(_function.InstanceId, GenericUsbDriverKind.WinUsb, CancellationToken.None),
            "RawTools.TakeOver.Done.Bound", CancellationToken.None);
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (Binding is not { } binding) return;
        if (!await _host.ConfirmAsync(Str.Get("RawTools.Restore.Confirm.Title"),
                Str.Format("RawTools.Restore.Confirm.Message", FunctionName, binding.OriginalService ?? Str.Get("RawTools.TakeOver.NoDriver")),
                CancellationToken.None, Str.Get("RawTools.Restore.Confirm.Button")))
            return;
        Outcome = await ChangeAsync(broker => broker.RestoreAsync(_function.InstanceId, CancellationToken.None), "RawTools.TakeOver.Done.Restored", CancellationToken.None);
    }

    [RelayCommand]
    private async Task RestoreAllAsync()
    {
        int count = OtherBindings + (IsTakenOver ? 1 : 0);
        if (count == 0) return;
        if (!await _host.ConfirmAsync(Str.Get("RawTools.RestoreAll.Confirm.Title"), Str.Format("RawTools.RestoreAll.Confirm.Message", count),
                CancellationToken.None, Str.Get("RawTools.RestoreAll.Confirm.Button")))
            return;
        Outcome = await ChangeAsync(broker => broker.RestoreAllAsync(CancellationToken.None), "RawTools.TakeOver.Done.RestoredAll", CancellationToken.None);
    }

    /// <summary>Runs one change through the broker and says how it went — in the tile, the device's log and the answer.</summary>
    async Task<string> ChangeAsync(Func<IUsbDeviceBroker, Task<UsbBindingOutcome>> change, string doneKey, CancellationToken ct)
    {
        if (IsWorking) return Str.Get("RawTools.TakeOver.Busy");
        IsWorking = true;
        Status = Str.Get("RawTools.TakeOver.Status.Asking");
        try
        {
            var outcome = await change(_host.DriverBroker);
            string said = outcome.Kind switch
            {
                UsbBindingOutcomeKind.Succeeded => Str.Get(doneKey),
                UsbBindingOutcomeKind.Declined => Str.Get("RawTools.TakeOver.Declined"),
                UsbBindingOutcomeKind.Refused => outcome.Message,
                _ => Str.Format("RawTools.TakeOver.Failed", outcome.Message),
            };
            _host.Log(this, said);
            return said;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        {
            var said = Str.Format("RawTools.TakeOver.Failed", e.Message);
            _host.Log(this, said);
            return said;
        }
        finally
        {
            IsWorking = false;
            await RefreshAsync(ct);
        }
    }

    string FunctionName => _function.FunctionInterface is { } mi
        ? Str.Format("RawTools.Function.Interface", _function.Name ?? _device.ClassOf(_function).Name, mi)
        : _function.Name ?? _device.DisplayName;

    // ── The agent ──────────────────────────────────────────────────────────────────────────────────────────

    public string Describe()
    {
        var sb = new StringBuilder($"{FunctionName}: driver {Driver}.\n");
        if (Binding is { } binding)
            sb.AppendLine($"Taken over by Nexaflow ({binding.State}) on {binding.BoundAt:yyyy-MM-dd HH:mm}; restore gives it back {binding.OriginalService ?? "no driver"}.");
        else if (Refusal is { } refusal)
            sb.AppendLine($"Not to be taken over: {refusal}.");
        else
            sb.AppendLine($"May be taken over. {Impact}");
        if (OtherBindings > 0) sb.AppendLine($"{OtherBindings} other interface(s) on this machine are taken over.");
        if (Outcome is { } outcome) sb.AppendLine($"Last: {outcome}");
        return sb.ToString();
    }

    public IReadOnlyList<UsbToolOperation> Operations =>
    [
        new("take_over", "Puts this function on Windows' generic WinUSB driver so raw access reaches it. Windows asks for administrator permission.", true, [],
            async (_, ct) =>
            {
                await RefreshAsync(ct);
                if (!CanTakeOver) return Binding is not null ? Str.Get("RawTools.TakeOver.Already") : Str.Format("RawTools.TakeOver.RefusedBecause", Refusal);
                return Outcome = await ChangeAsync(b => b.BindGenericAsync(_function.InstanceId, GenericUsbDriverKind.WinUsb, ct), "RawTools.TakeOver.Done.Bound", ct);
            }),
        new("restore", "Gives this function back the driver it had before Nexaflow took it over. Windows asks for administrator permission.", true, [],
            async (_, ct) =>
            {
                await RefreshAsync(ct);
                if (!IsTakenOver) return Str.Get("RawTools.Restore.NotTaken");
                return Outcome = await ChangeAsync(b => b.RestoreAsync(_function.InstanceId, ct), "RawTools.TakeOver.Done.Restored", ct);
            }),
        new("restore_all", "Gives every interface Nexaflow took over on this machine back its driver, in one administrator prompt.", true, [],
            async (_, ct) => Outcome = await ChangeAsync(b => b.RestoreAllAsync(ct), "RawTools.TakeOver.Done.RestoredAll", ct)),
    ];

    /// <summary>The driver, what the policy says, and this function's binding with every step the bridge took.</summary>
    public async Task<JsonObject> DebugDetailsAsync(UsbDebugMask mask, CancellationToken ct)
    {
        var (binding, others, refusal) = await ReadAsync(ct);
        return new JsonObject
        {
            ["driver"] = Driver,
            ["mode"] = _function.Mode.ToString(),
            ["refusal"] = refusal,
            ["binding"] = binding is { } b ? new JsonObject
            {
                ["state"] = b.State.ToString(),
                ["strategy"] = b.Strategy,
                ["hardwareId"] = b.HardwareId,
                ["originalService"] = b.OriginalService,
                ["originalInf"] = b.OriginalInfName,
                ["originalSection"] = b.OriginalInfSection,
                ["originalMatchingId"] = b.OriginalMatchingId,
                ["originalClassGuid"] = b.OriginalClassGuid?.ToString(),
                ["classChanged"] = b.ClassChanged,
                ["boundAt"] = b.BoundAt.ToString("O"),
                ["restoredAt"] = b.RestoredAt?.ToString("O"),
                ["log"] = new JsonArray([.. b.Log.Select(line => (JsonNode?)mask.Text(line))]),
            } : null,
            ["otherActiveBindings"] = others,
        };
    }

    public void Dispose() { }
}

/// <summary>What Windows stops doing with a function while it is on WinUSB, in the user's words (spec R1).</summary>
internal static class TakeOverImpact
{
    public static string Of(UsbDeviceNode device, UsbFunctionNode function)
    {
        string sameModel = Str.Get("RawTools.Impact.SameModel");
        if (function.Mode == UsbFunctionMode.NoDriver) return $"{Str.Get("RawTools.Impact.NoDriver")} {sameModel}";
        string use = (function.Driver.ClassName ?? "").ToUpperInvariant() switch
        {
            "MEDIA" or "AUDIOENDPOINT" => Str.Get("RawTools.Use.Audio"),
            "PORTS" => Str.Get("RawTools.Use.Serial"),
            "MODEM" => Str.Get("RawTools.Use.Modem"),
            "CAMERA" or "IMAGE" => Str.Get("RawTools.Use.Camera"),
            "NET" => Str.Get("RawTools.Use.Network"),
            "PRINTER" => Str.Get("RawTools.Use.Printer"),
            "SMARTCARDREADER" => Str.Get("RawTools.Use.SmartCard"),
            "WPD" => Str.Get("RawTools.Use.Portable"),
            "HIDCLASS" => Str.Get("RawTools.Use.Input"),
            _ => Str.Format("RawTools.Use.Class", device.ClassOf(function).Name),
        };
        return $"{Str.Format("RawTools.Impact.Stops", use, function.Driver.Service)} {sameModel}";
    }
}

/// <summary>
/// Offers take-over for every function the policy lets go, and restore for every function Nexaflow holds a binding for.
/// Functions the policy keeps — hubs, storage, keyboards and mice in use, anything already generic — get no tile: their
/// own tiles, or the device's Interfaces tab, say what they are.
/// </summary>
[Subfeature("usbprobe", "takeover",
    DisplayName = "Take over",
    Description = "Puts one interface on Windows' generic WinUSB driver so raw access reaches it, and gives it back its own driver.",
    Order = 7)]
public sealed class UsbTakeOverToolProvider : IUsbToolProvider
{
    public Uri Templates { get; } = new("/Nexaflow.Features.UsbProbe.RawTools;component/Views/RawTemplates.xaml", UriKind.Relative);

    public IReadOnlyList<IUsbTool> Offer(UsbDeviceNode device, IUsbToolHost host)
    {
        if (device.IsEjected) return [];
        var broker = host.DriverBroker;
        var held = broker.GetBindings().Where(r => r.IsActive).Select(r => r.InterfaceInstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return
        [
            .. device.Functions
                .Where(f => f.Mode != UsbFunctionMode.CompositeParent)
                .Where(f => held.Contains(f.InstanceId) || broker.RefusalReason(f.InstanceId) is null)
                .Select(f => new UsbTakeOverTool(device, f, host)),
        ];
    }
}
