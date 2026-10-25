using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Icons;
using Nexaflow.IO.Common;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Diagnostics;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Raw;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.IO.Usb.Windows;
using Nexaflow.Plugins;
using Nexaflow.Visuals.Common.Behaviors;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe.RawTools;

/// <summary>A choice in one of the console's lists: the value, and what it is called in the user's language.</summary>
public sealed record RawChoice<T>(T Value, string Label);

/// <summary>An endpoint as the tile shows it, in the user's words; <see cref="Pipe"/> is the endpoint itself.</summary>
public sealed class RawPipeRow(UsbPipe pipe) : IClipboardText
{
    public UsbPipe Pipe { get; } = pipe;
    public byte Address => Pipe.Address;
    public ushort MaxPacketSize => Pipe.MaxPacketSize;
    public byte InterfaceNumber => Pipe.InterfaceNumber;
    public string Endpoint { get; } = $"0x{pipe.Address:X2}";
    public string Direction { get; } = RawWords.Direction(pipe.Direction);
    public string Type { get; } = RawWords.EndpointType(pipe.Type);
    public string Label => Str.Format("RawTools.Raw.PipeLabel", Endpoint, Direction, Type, MaxPacketSize, InterfaceNumber);
    public string ClipboardText => Label;
}

/// <summary>The words for USB's own values — directions, transfer and request types, recipients — in the user's
/// language. Request names (GET_DESCRIPTOR) and setup fields (bmRequestType) are the spec's, and stay as it writes them.</summary>
internal static class RawWords
{
    public static string Direction(UsbDirection direction) =>
        Str.Get(direction == UsbDirection.In ? "RawTools.Direction.In" : "RawTools.Direction.Out");

    public static string EndpointType(UsbEndpointType type) => Str.Get(type switch
    {
        UsbEndpointType.Control => "RawTools.EndpointType.Control",
        UsbEndpointType.Isochronous => "RawTools.EndpointType.Isochronous",
        UsbEndpointType.Bulk => "RawTools.EndpointType.Bulk",
        _ => "RawTools.EndpointType.Interrupt",
    });

    public static string RequestType(UsbRequestType type) => Str.Get(type switch
    {
        UsbRequestType.Standard => "RawTools.RequestType.Standard",
        UsbRequestType.Class => "RawTools.RequestType.Class",
        UsbRequestType.Vendor => "RawTools.RequestType.Vendor",
        _ => "RawTools.RequestType.Reserved",
    });

    public static string Recipient(UsbRequestRecipient recipient) => Str.Get(recipient switch
    {
        UsbRequestRecipient.Device => "RawTools.Recipient.Device",
        UsbRequestRecipient.Interface => "RawTools.Recipient.Interface",
        UsbRequestRecipient.Endpoint => "RawTools.Recipient.Endpoint",
        _ => "RawTools.Recipient.Other",
    });
}

/// <summary>A transfer as the session list shows it.</summary>
public sealed class RawTransferRow(RawUsbTransfer transfer) : IClipboardText
{
    public string ClipboardText => Transfer.Line();
    public RawUsbTransfer Transfer { get; } = transfer;
    public string Time { get; } = transfer.At.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
    public string Direction { get; } = RawWords.Direction(transfer.Direction);
    public string Target => Transfer.Target;
    public string Size { get; } = $"{transfer.Data.Length}/{transfer.Requested}";
    public string Result => Transfer.Error ?? "OK";
    public bool Failed => !Transfer.Succeeded;
    public string Hex { get; } = UsbHex.Format(transfer.Data);
}

/// <summary>
/// R3–R6, raw access: a function on WinUSB, talked to directly. A control-transfer console with the standard reads
/// ready-made; a reader that streams an IN endpoint; a writer that sends hex or text to an OUT endpoint, once or
/// repeated; and the session log of every transfer, shown here, logged to the device's Diagnostics tab and saved as
/// text. Anything that sends data to the device — an OUT control transfer, an endpoint write — asks first.
/// </summary>
/// <remarks>
/// The function is opened on the first transfer and held — WinUSB lets one program in at a time — until "Let go", or
/// until the page closes or the device goes; the tile's header says while it is held. The reader is live, so shutting
/// the tile stops it; a repeated write is a job the user started, so it runs to the end. Transfers reach the list in
/// batches off the UI thread, so a fast interrupt endpoint does not flood the page.
/// </remarks>
public sealed partial class UsbRawTool : ObservableObject, IUsbTool, IUsbToolDebugDetails
{
    const int ShownTransfers = 500;
    static readonly TimeSpan Flush = TimeSpan.FromMilliseconds(80);

    readonly UsbDeviceNode _device;
    readonly UsbFunctionNode _function;
    readonly IRawUsbAccess _access;
    readonly IUsbToolHost _host;
    readonly RawUsbSession _session = new();
    readonly ConcurrentQueue<RawUsbTransfer> _arrived = new();
    readonly CancellationTokenSource _lifetime = new();
    readonly SemaphoreSlim _opening = new(1, 1);
    IRawUsbTransport? _transport;
    CancellationTokenSource? _reading, _writing;
    Timer? _flush;
    long _total;
    bool _disposed;

    internal UsbRawTool(UsbDeviceNode device, UsbFunctionNode function, IRawUsbAccess access, IUsbToolHost host)
    {
        _device = device;
        _function = function;
        _access = access;
        _host = host;
        _session.Recorded += _arrived.Enqueue;

        var interfaces = device.InterfacesOf(function);
        Templates = UsbStandardRequests.Reads(interfaces.FirstOrDefault()?.Number ?? function.FunctionInterface ?? 0);
        ShowPipes([.. interfaces.SelectMany(s => s.Endpoints.Select(e => new UsbPipe(s.Number, e.Address, e.Type, e.MaxPacketSize, e.Interval)))]);
        _selectedTemplate = Templates[0];
        UseTemplate(Templates[0]);
        _status = Str.Get("RawTools.Raw.Status.Closed");
    }

    public string ToolId => "usb.raw";
    public string Key => $"{ToolId}:{_function.InstanceId}";
    public string Title => Str.Get("RawTools.Raw.Title");
    public IconRef Icon => IconRef.Fluent("plug_connected");
    public string Description => Str.Get("RawTools.Raw.Description");
    public string? FunctionInstanceId => _function.InstanceId;
    public UsbToolSize Size => UsbToolSize.Wide;
    public bool IsBusy => IsReading || IsWriting;

    [ObservableProperty] private string _status;
    [ObservableProperty] private bool _isOpen;

    // ── Endpoints ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every endpoint: from the descriptors until the function is open, then as WinUSB reports them.</summary>
    public ObservableCollection<RawPipeRow> Pipes { get; } = [];
    public ObservableCollection<RawPipeRow> InPipes { get; } = [];
    public ObservableCollection<RawPipeRow> OutPipes { get; } = [];
    public bool HasInPipes => InPipes.Count > 0;
    public bool HasOutPipes => OutPipes.Count > 0;

    void ShowPipes(IReadOnlyList<UsbPipe> pipes)
    {
        byte? reading = ReadPipe?.Address, writing = WritePipe?.Address;
        Pipes.Clear();
        InPipes.Clear();
        OutPipes.Clear();
        foreach (var pipe in pipes)
        {
            var row = new RawPipeRow(pipe);
            Pipes.Add(row);
            if (pipe.Type is not (UsbEndpointType.Bulk or UsbEndpointType.Interrupt)) continue;
            (pipe.Direction == UsbDirection.In ? InPipes : OutPipes).Add(row);
        }
        ReadPipe = InPipes.FirstOrDefault(p => p.Address == reading) ?? InPipes.FirstOrDefault();
        WritePipe = OutPipes.FirstOrDefault(p => p.Address == writing) ?? OutPipes.FirstOrDefault();
        OnPropertyChanged(nameof(HasInPipes));
        OnPropertyChanged(nameof(HasOutPipes));
    }

    // ── Life ───────────────────────────────────────────────────────────────────────────────────────────────

    public Task ActivateAsync(CancellationToken ct)
    {
        _flush ??= new Timer(_ => _ = _host.RunOnUiAsync(Drain), null, Flush, Flush);
        Drain();
        return Task.CompletedTask;
    }

    /// <summary>The reader is live and stops here; a repeated write runs on, and the function stays held.</summary>
    public void Deactivate()
    {
        StopReading();
        _flush?.Dispose();
        _flush = null;
        Drain();
    }

    /// <summary>The function, opened on first use and logged through the session from then on.</summary>
    async Task<IRawUsbTransport> TransportAsync()
    {
        if (_transport is { } open) return open;
        await _opening.WaitAsync(_lifetime.Token);
        try
        {
            if (_transport is { } raced) return raced;
            ObjectDisposedException.ThrowIf(_disposed, this);
            var opened = _session.Record(await Task.Run(() => _access.Open(_function), _lifetime.Token));
            _transport = opened;
            IsOpen = true;
            ShowPipes(opened.Pipes);
            Status = Str.Format("RawTools.Raw.Status.Open", opened.Pipes.Count);
            _host.Log(this, Str.Format("RawTools.Raw.Log.Opened", string.Join(", ", opened.Pipes.Select(p => p.Label))));
            return opened;
        }
        finally
        {
            _opening.Release();
        }
    }

    /// <summary>Lets the function go, so another program can open it. Not while a write is running.</summary>
    [RelayCommand]
    private async Task LetGoAsync()
    {
        if (IsWriting || _transport is null) return;
        StopReading();
        await CloseAsync();
        Status = Str.Get("RawTools.Raw.Status.Closed");
        _host.Log(this, Str.Get("RawTools.Raw.Log.LetGo"));
    }

    async Task CloseAsync()
    {
        var transport = _transport;
        _transport = null;
        IsOpen = false;
        if (transport is not null) await transport.DisposeAsync();
    }

    // ── Control console (R3) ───────────────────────────────────────────────────────────────────────────────

    public IReadOnlyList<UsbRequestTemplate> Templates { get; }
    public IReadOnlyList<RawChoice<UsbDirection>> Directions { get; } =
        [.. new[] { UsbDirection.In, UsbDirection.Out }.Select(d => new RawChoice<UsbDirection>(d, RawWords.Direction(d)))];
    public IReadOnlyList<RawChoice<UsbRequestType>> RequestTypes { get; } =
        [.. new[] { UsbRequestType.Standard, UsbRequestType.Class, UsbRequestType.Vendor }.Select(t => new RawChoice<UsbRequestType>(t, RawWords.RequestType(t)))];
    public IReadOnlyList<RawChoice<UsbRequestRecipient>> Recipients { get; } =
        [.. Enum.GetValues<UsbRequestRecipient>().Select(r => new RawChoice<UsbRequestRecipient>(r, RawWords.Recipient(r)))];

    [ObservableProperty] private UsbRequestTemplate? _selectedTemplate;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsOut))] private UsbDirection _direction;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NeedsWarning))] private UsbRequestType _requestType;
    [ObservableProperty] private UsbRequestRecipient _recipient;
    [ObservableProperty] private string _request = "";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private string _index = "";
    [ObservableProperty] private string _length = "";
    [ObservableProperty] private string _data = "";
    [ObservableProperty] private string? _controlResult;

    /// <summary>An OUT request carries data, which it says how long by itself; an IN one says how much it takes.</summary>
    public bool IsOut => Direction == UsbDirection.Out;

    /// <summary>Class and vendor requests do what the device's maker decided — the warning says so (spec §9.1).</summary>
    public bool NeedsWarning => RequestType is not UsbRequestType.Standard;

    partial void OnSelectedTemplateChanged(UsbRequestTemplate? value)
    {
        if (value is not null) UseTemplate(value);
    }

    void UseTemplate(UsbRequestTemplate template)
    {
        var s = template.Setup;
        Direction = s.Direction;
        RequestType = s.Type;
        Recipient = s.Recipient;
        Request = $"0x{s.Request:X2}";
        Value = $"0x{s.Value:X4}";
        Index = $"0x{s.Index:X4}";
        Length = s.Length.ToString(CultureInfo.InvariantCulture);
        Data = "";
    }

    [RelayCommand]
    private async Task SendControlAsync()
    {
        if (!TryBuildSetup(out var setup, out var data, out var error))
        {
            ControlResult = error;
            return;
        }
        if (setup.Direction == UsbDirection.Out && !await _host.ConfirmAsync(Str.Get("RawTools.Raw.Control.Confirm.Title"),
                Str.Format("RawTools.Raw.Control.Confirm.Message", setup.Describe(), data.Length == 0 ? Str.Get("RawTools.Raw.NoData") : UsbHex.Format(data), FunctionName),
                CancellationToken.None, Str.Get("RawTools.Raw.Send")))
            return;
        ControlResult = await ControlAsync(setup, data, _lifetime.Token);
    }

    bool TryBuildSetup(out UsbControlSetup setup, out byte[] data, out string? error)
    {
        setup = default;
        data = [];
        if (!RawNumber.TryParse(Request, byte.MaxValue, out int request)) { error = Str.Format("RawTools.Raw.Bad", "bRequest", 0xFF); return false; }
        if (!RawNumber.TryParse(Value, ushort.MaxValue, out int value)) { error = Str.Format("RawTools.Raw.Bad", "wValue", 0xFFFF); return false; }
        if (!RawNumber.TryParse(Index, ushort.MaxValue, out int index)) { error = Str.Format("RawTools.Raw.Bad", "wIndex", 0xFFFF); return false; }
        int length;
        if (IsOut)
        {
            if (!string.IsNullOrWhiteSpace(Data) && !UsbHex.TryParse(Data, out data)) { error = Str.Get("RawTools.Raw.NotHex"); return false; }
            if (data.Length > ushort.MaxValue) { error = Str.Format("RawTools.Raw.Bad", "wLength", 0xFFFF); return false; }
            length = data.Length;
        }
        else if (!RawNumber.TryParse(Length, ushort.MaxValue, out length)) { error = Str.Format("RawTools.Raw.Bad", "wLength", 0xFFFF); return false; }
        setup = UsbControlSetup.Of(Direction, RequestType, Recipient, (byte)request, (ushort)value, (ushort)index, (ushort)length);
        error = null;
        return true;
    }

    async Task<string> ControlAsync(UsbControlSetup setup, byte[] data, CancellationToken ct)
    {
        try
        {
            var transport = await TransportAsync();
            if (setup.Direction == UsbDirection.In)
            {
                var got = await transport.ControlInAsync(setup, ct);
                _host.Log(this, Str.Format("RawTools.Raw.Log.ControlIn", setup.Describe(), got.Length));
                return got.Length == 0 ? Str.Get("RawTools.Raw.Control.Empty") : Str.Format("RawTools.Raw.Control.Got", got.Length, UsbHex.Format(got));
            }
            await transport.ControlOutAsync(setup, data, ct);
            _host.Log(this, Str.Format("RawTools.Raw.Log.ControlOut", setup.Describe(), UsbHex.Format(data)));
            return Str.Format("RawTools.Raw.Control.Sent", setup.Describe());
        }
        catch (Exception e) when (e is IOException or ArgumentException or ObjectDisposedException)
        {
            _host.Log(this, Str.Format("RawTools.Raw.Log.Failed", setup.Describe(), e.Message));
            return e.Message;
        }
        catch (OperationCanceledException)
        {
            return Str.Get("RawTools.Raw.Cancelled");
        }
    }

    // ── Endpoint reader (R4) ───────────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private RawPipeRow? _readPipe;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(CanChooseReadPipe))] private bool _isReading;

    public bool CanChooseReadPipe => !IsReading;
    [ObservableProperty] private long _readCount;

    [RelayCommand]
    private async Task StartReadingAsync()
    {
        if (IsReading || ReadPipe is not { } pipe) return;
        IRawUsbTransport transport;
        try { transport = await TransportAsync(); }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            Status = e.Message;
            return;
        }

        var reading = _reading = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        IsReading = true;
        Status = Str.Format("RawTools.Raw.Status.Reading", pipe.Address);
        _host.Log(this, Str.Format("RawTools.Raw.Log.ReadStarted", pipe.Address));
        _ = Task.Run(async () =>
        {
            long packets = 0;
            string? stopped = null;
            try
            {
                await foreach (var _ in transport.ReadEndpointAsync(pipe.Address, pipe.MaxPacketSize, reading.Token)) packets++;
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or ArgumentException)
            {
                stopped = e.Message;
            }
            await _host.RunOnUiAsync(() =>
            {
                if (_disposed) return;
                IsReading = false;
                Drain();
                Status = stopped is null ? Str.Format("RawTools.Raw.Status.Read", ReadCount, pipe.Address) : Str.Format("RawTools.Raw.Status.ReadStopped", stopped);
                _host.Log(this, Str.Format("RawTools.Raw.Log.ReadStopped", pipe.Address, packets, stopped ?? "OK"));
            });
        });
    }

    /// <summary>Pauses the reader: what it read stays in the list, and starting again carries on from there.</summary>
    [RelayCommand]
    private void StopReading()
    {
        _reading?.Cancel();
        _reading = null;
    }

    // ── Endpoint writer (R5) ───────────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private RawPipeRow? _writePipe;
    [ObservableProperty] private string _payload = "";
    [ObservableProperty] private bool _payloadIsText;
    [ObservableProperty] private string _repeat = "1";
    [ObservableProperty] private string _intervalMs = "0";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy), nameof(CanChooseWritePipe))] private bool _isWriting;

    public bool CanChooseWritePipe => !IsWriting;
    [ObservableProperty] private string? _writeResult;

    [RelayCommand]
    private async Task WriteAsync()
    {
        if (IsWriting || WritePipe is not { } pipe) return;
        if (!RawPayload.TryParse(Payload, PayloadIsText, out var bytes, out var error)) { WriteResult = error; return; }
        if (!RawNumber.TryParse(Repeat, 100_000, out int times) || times < 1) { WriteResult = Str.Format("RawTools.Raw.Bad", Str.Get("RawTools.Raw.Field.Repeat"), 100_000); return; }
        if (!RawNumber.TryParse(IntervalMs, 60_000, out int interval)) { WriteResult = Str.Format("RawTools.Raw.Bad", Str.Get("RawTools.Raw.Field.Interval"), 60_000); return; }
        string message = times == 1
            ? Str.Format("RawTools.Raw.Write.Confirm.Once", bytes.Length, pipe.Address, FunctionName)
            : Str.Format("RawTools.Raw.Write.Confirm.Repeated", bytes.Length, pipe.Address, FunctionName, times, interval);
        if (!await _host.ConfirmAsync(Str.Get("RawTools.Raw.Write.Confirm.Title"), message, CancellationToken.None, Str.Get("RawTools.Raw.Send")))
            return;
        WriteResult = await WriteCoreAsync(pipe.Address, bytes, times, interval);
    }

    [RelayCommand]
    private void StopWriting() => _writing?.Cancel();

    async Task<string> WriteCoreAsync(byte endpoint, byte[] bytes, int times, int interval)
    {
        if (IsWriting) return Str.Get("RawTools.Raw.Write.Busy");
        IRawUsbTransport transport;
        try { transport = await TransportAsync(); }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { return e.Message; }

        var writing = _writing = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        IsWriting = true;
        int sent = 0;
        string said;
        try
        {
            for (; sent < times; sent++)
            {
                if (sent > 0 && interval > 0) await Task.Delay(interval, writing.Token);
                await transport.WriteEndpointAsync(endpoint, bytes, writing.Token);
            }
            said = Str.Format("RawTools.Raw.Write.Done", sent, bytes.Length, endpoint);
        }
        catch (OperationCanceledException)
        {
            said = Str.Format("RawTools.Raw.Write.Stopped", sent, times);
        }
        catch (Exception e) when (e is IOException or ArgumentException or ObjectDisposedException)
        {
            said = Str.Format("RawTools.Raw.Write.Failed", sent, times, e.Message);
        }
        finally
        {
            _writing = null;
            writing.Dispose();
            if (!_disposed) IsWriting = false;
        }
        _host.Log(this, said);
        return said;
    }

    // ── Session log (R6) ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>The newest transfers first; the whole session is in <see cref="SaveSessionAsync"/>.</summary>
    public ObservableCollection<RawTransferRow> Transfers { get; } = [];

    [ObservableProperty] private long _transferCount;

    /// <summary>Moves what arrived since the last flush into the list, keeping the list short.</summary>
    void Drain()
    {
        var arrived = new List<RawUsbTransfer>();
        while (_arrived.TryDequeue(out var transfer)) arrived.Add(transfer);
        if (arrived.Count == 0) return;
        _total += arrived.Count;
        foreach (var transfer in arrived.TakeLast(ShownTransfers)) Transfers.Insert(0, new RawTransferRow(transfer));
        while (Transfers.Count > ShownTransfers) Transfers.RemoveAt(Transfers.Count - 1);
        TransferCount = _total;
        ReadCount += arrived.Count(t => t.Kind == RawUsbTransferKind.EndpointIn && t.Succeeded);
    }

    [RelayCommand]
    private void ClearSession()
    {
        Drain();
        _session.Clear();
        Transfers.Clear();
        TransferCount = _total = 0;
        ReadCount = 0;
    }

    [RelayCommand]
    [LocalDiskOnly("a session with a device plugged into this computer, saved where the Windows save dialog picked")]
    private async Task SaveSessionAsync()
    {
        Drain();
        var name = $"usb-session-{_device.VendorId:X4}-{_device.ProductId:X4}-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
        if (await _host.PickSaveFileAsync(name, [".txt"]) is not { } path) return;
        try
        {
            await File.WriteAllTextAsync(path, Header() + _session.Text(), _lifetime.Token);
            Status = Str.Format("RawTools.Raw.Status.Saved", Path.GetFileName(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = Str.Format("RawTools.Raw.Status.SaveFailed", e.Message);
        }
    }

    /// <summary>What the session was with: the device and function, as the first lines of a saved log.</summary>
    string Header() => string.Create(CultureInfo.InvariantCulture,
        $"# {_device.DisplayName} {_device.VendorId:X4}:{_device.ProductId:X4} at {_device.Location.PortPath} — {FunctionName}\n# {string.Join("; ", Pipes.Select(p => p.Pipe.Label))}\n");

    string FunctionName => _function.FunctionInterface is { } mi
        ? Str.Format("RawTools.Function.Interface", _function.Name ?? _device.ClassOf(_function).Name, mi)
        : _function.Name ?? _device.DisplayName;

    // ── The agent ──────────────────────────────────────────────────────────────────────────────────────────

    public string Describe()
    {
        var sb = new StringBuilder($"{FunctionName} on WinUSB, {(IsOpen ? "held open by this tile" : "not open")}.\n");
        foreach (var row in Pipes) sb.AppendLine($"  endpoint {row.Pipe.Label}");
        if (IsReading && ReadPipe is { } reading) sb.AppendLine($"Reading 0x{reading.Address:X2}: {ReadCount} packet(s) so far.");
        if (IsWriting) sb.AppendLine("Writing.");
        sb.AppendLine($"{_total} transfer(s) this session.");
        foreach (var row in Transfers.Take(5)) sb.AppendLine($"  {row.ClipboardText}");
        return sb.ToString();
    }

    public IReadOnlyList<UsbToolOperation> Operations =>
    [
        new("standard_request", "Sends one of the standard reads (GET_DESCRIPTOR, GET_STATUS…) and returns what the device answered.", false,
            [new UsbToolArgument("request", $"One of: {string.Join("; ", Templates.Select(t => t.Name))}.")],
            async (args, ct) => Templates.FirstOrDefault(t => string.Equals(t.Name, args["request"], StringComparison.OrdinalIgnoreCase)) is { } template
                ? await ControlAsync(template.Setup, [], ct)
                : $"No such request; one of: {string.Join("; ", Templates.Select(t => t.Name))}."),
        new("control_transfer", "Sends any control transfer — standard, class or vendor, IN or OUT. A class or vendor request does whatever the device's maker decided.", true,
            [
                new UsbToolArgument("bmRequestType", "The request type byte, e.g. 0xC0 for a vendor IN request to the device."),
                new UsbToolArgument("bRequest", "The request code."),
                new UsbToolArgument("wValue", "Decimal or 0x hex.", Required: false),
                new UsbToolArgument("wIndex", "Decimal or 0x hex.", Required: false),
                new UsbToolArgument("wLength", "For an IN request: how many bytes to take.", Required: false),
                new UsbToolArgument("data", "For an OUT request: its bytes in hex.", Required: false),
            ],
            (args, ct) => AgentControlAsync(args, ct)),
        new("read_endpoint", "Reads a bulk or interrupt IN endpoint for a few seconds and returns what arrived.", false,
            [new UsbToolArgument("endpoint", "The endpoint address, e.g. 0x81."), new UsbToolArgument("seconds", "How long, 1–10. Default 3.", Required: false)],
            (args, ct) => AgentReadAsync(args, ct)),
        new("write_endpoint", "Writes bytes to a bulk or interrupt OUT endpoint.", true,
            [new UsbToolArgument("endpoint", "The endpoint address, e.g. 0x01."), new UsbToolArgument("hex", "The bytes in hex.")],
            async (args, ct) =>
            {
                if (!RawNumber.TryParse(args["endpoint"], byte.MaxValue, out int endpoint)) return "That is not an endpoint address.";
                if (!UsbHex.TryParse(args["hex"], out var bytes)) return Str.Get("RawTools.Raw.NotHex");
                return await WriteCoreAsync((byte)endpoint, bytes, 1, 0);
            }),
        new("session_log", "The session's newest transfers, one a line.", false,
            [new UsbToolArgument("count", "How many, up to 200. Default 50.", Required: false)],
            (args, _) =>
            {
                Drain();
                int count = args.TryGetValue("count", out var c) && RawNumber.TryParse(c, 200, out int n) && n > 0 ? n : 50;
                return Task.FromResult(Transfers.Count == 0 ? "No transfers yet." : string.Join("\n", Transfers.Take(count).Select(r => r.ClipboardText)));
            }),
    ];

    Task<string> AgentControlAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        int Field(string name, int max, int fallback) => args.TryGetValue(name, out var text) && !string.IsNullOrWhiteSpace(text)
            ? RawNumber.TryParse(text, max, out int v) ? v : -1
            : fallback;
        int type = Field("bmRequestType", byte.MaxValue, -1), request = Field("bRequest", byte.MaxValue, -1);
        int value = Field("wValue", ushort.MaxValue, 0), index = Field("wIndex", ushort.MaxValue, 0), length = Field("wLength", ushort.MaxValue, 0);
        if (type < 0 || request < 0 || value < 0 || index < 0 || length < 0) return Task.FromResult("A field is missing or out of range.");
        var probe = new UsbControlSetup((byte)type, (byte)request, (ushort)value, (ushort)index, (ushort)length);
        byte[] data = [];
        if (probe.Direction == UsbDirection.Out && args.TryGetValue("data", out var hex) && !string.IsNullOrWhiteSpace(hex) && !UsbHex.TryParse(hex, out data))
            return Task.FromResult(Str.Get("RawTools.Raw.NotHex"));
        var setup = probe.Direction == UsbDirection.Out ? probe with { Length = (ushort)data.Length } : probe;
        return ControlAsync(setup, data, ct);
    }

    async Task<string> AgentReadAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        if (!RawNumber.TryParse(args["endpoint"], byte.MaxValue, out int address)) return "That is not an endpoint address.";
        if (IsReading) return "The tile is reading an endpoint now; session_log has what it read.";
        double seconds = args.TryGetValue("seconds", out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 1, 10) : 3;
        try
        {
            var transport = await TransportAsync();
            var pipe = transport.Pipes.FirstOrDefault(p => p.Address == address);
            if (pipe is null) return $"0x{address:X2} is not one of its endpoints.";
            using var window = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            window.CancelAfter(TimeSpan.FromSeconds(seconds));
            var lines = new List<string>();
            await foreach (var packet in transport.ReadEndpointAsync(pipe.Address, pipe.MaxPacketSize, window.Token))
                if (lines.Count < 100) lines.Add($"{DateTime.Now:HH:mm:ss.fff} {packet.Length} B · {UsbHex.Format(packet.Span)}");
            return lines.Count == 0 ? $"Nothing arrived on 0x{address:X2} in {seconds:0.#} s." : string.Join("\n", lines);
        }
        catch (Exception e) when (e is IOException or ArgumentException or ObjectDisposedException)
        {
            return e.Message;
        }
    }

    /// <summary>Whether it can be opened, its endpoints, and how the session went — what failed, not the bytes, which
    /// may hold anything the device keeps.</summary>
    public Task<JsonObject> DebugDetailsAsync(UsbDebugMask mask, CancellationToken ct)
    {
        var transfers = _session.Transfers;
        return Task.FromResult(new JsonObject
        {
            ["unreachable"] = _access.UnreachableReason(_function),
            ["open"] = IsOpen,
            ["pipesFrom"] = IsOpen ? "WinUSB" : "descriptors",
            ["pipes"] = new JsonArray([.. Pipes.Select(row => row.Pipe).Select(p => (JsonNode)new JsonObject
            {
                ["interface"] = p.InterfaceNumber,
                ["address"] = $"{p.Address:X2}",
                ["type"] = p.Type.ToString(),
                ["maxPacketSize"] = p.MaxPacketSize,
                ["interval"] = p.Interval,
            })]),
            ["transfers"] = transfers.Count,
            ["dropped"] = _session.Dropped,
            ["failures"] = new JsonArray([.. transfers.Where(t => !t.Succeeded).TakeLast(50)
                .Select(t => (JsonNode?)mask.Text($"{t.At:O} {t.Kind} {t.Target}: {t.Error}"))]),
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flush?.Dispose();
        _flush = null;
        _lifetime.Cancel();
        _ = CloseAsync().ContinueWith(_ => _lifetime.Dispose(), TaskScheduler.Default);
    }
}

/// <summary>Offers raw access for every function WinUSB holds — taken over by Nexaflow or WinUSB-ready by itself.</summary>
[Subfeature("usbprobe", "raw",
    DisplayName = "Raw access",
    Description = "Talks to a function on WinUSB directly: control transfers, endpoint reads and writes, and a log of every transfer.",
    Order = 6)]
public sealed class UsbRawToolProvider : IUsbToolProvider
{
    readonly IRawUsbAccess _access;

    public UsbRawToolProvider() : this(new WinUsbRawAccess()) { }

    internal UsbRawToolProvider(IRawUsbAccess access) => _access = access;

    public Uri Templates { get; } = new("/Nexaflow.Features.UsbProbe.RawTools;component/Views/RawTemplates.xaml", UriKind.Relative);

    public IReadOnlyList<IUsbTool> Offer(UsbDeviceNode device, IUsbToolHost host) =>
    [
        .. device.Functions
            .Where(f => f.Mode != UsbFunctionMode.CompositeParent && _access.UnreachableReason(f) is null)
            .Select(f => new UsbRawTool(device, f, _access, host)),
    ];
}

/// <summary>A number typed into a field: decimal, or hex after 0x.</summary>
internal static class RawNumber
{
    public static bool TryParse(string? text, int max, out int value)
    {
        var t = (text ?? "").Trim();
        bool parsed = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(t[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)
            : int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        return parsed && value >= 0 && value <= max;
    }
}

/// <summary>What the writer sends: hex, or text as UTF-8 with \r, \n, \t, \0, \\ and \xHH escapes.</summary>
internal static class RawPayload
{
    public static bool TryParse(string text, bool asText, out byte[] bytes, out string? error)
    {
        bytes = [];
        if (string.IsNullOrEmpty(text)) { error = Str.Get("RawTools.Raw.Empty"); return false; }
        if (!asText)
        {
            error = UsbHex.TryParse(text, out bytes) ? null : Str.Get("RawTools.Raw.NotHex");
            return error is null;
        }
        // Text is UTF-8; \xHH is a byte, not a character, so it goes in between the encoded runs as it is.
        var output = new List<byte>();
        var run = new StringBuilder();
        void Encode()
        {
            output.AddRange(Encoding.UTF8.GetBytes(run.ToString()));
            run.Clear();
        }
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length) { run.Append(text[i]); continue; }
            char next = text[++i];
            switch (next)
            {
                case 'r': run.Append('\r'); break;
                case 'n': run.Append('\n'); break;
                case 't': run.Append('\t'); break;
                case '0': run.Append('\0'); break;
                case '\\': run.Append('\\'); break;
                case 'x' when i + 2 < text.Length && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2]):
                    Encode();
                    output.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
                    i += 2;
                    break;
                default: run.Append('\\').Append(next); break;
            }
        }
        Encode();
        bytes = [.. output];
        error = null;
        return true;
    }
}
