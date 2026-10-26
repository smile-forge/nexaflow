using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Serial.Services;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Serial.ViewModels;

/// <summary>A choice in a list: the invariant value and the words shown for it.</summary>
public sealed record SerialChoice<T>(T Value, string Label);

/// <summary>What goes after a line that is sent as text.</summary>
public enum SerialLineEnding { None, CR, LF, CRLF }

/// <summary>
/// A serial terminal on one COM port: the line settings, connect and disconnect, DTR and RTS, a transcript of what came
/// and went — as text or hex, with or without times — and a line to send from.
/// </summary>
/// <remarks>
/// <para>
/// Bytes arrive on the link's thread and wait in a queue; the page takes them in every 50 ms, so a device streaming at
/// 921600 baud costs twenty updates a second, not one per read. A received line stays open, gathering bytes, until a
/// newline ends it, it reaches 512 bytes, or the port has been quiet for a quarter of a second.
/// </para>
/// <para>
/// Sending is the one thing here that reaches the device, so the first send on each connection asks first.
/// </para>
/// </remarks>
public sealed partial class SerialViewModel : ObservableObject, IPageViewModel, IDisposable
{
    const int TranscriptLines = 10_000, LongestLine = 512;
    static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250), Intake = TimeSpan.FromMilliseconds(50);

    readonly ISerialLink _link;
    readonly Func<IReadOnlyList<SerialPortInfo>> _ports;
    readonly IShellServices _shell;
    readonly TimeProvider _time;
    readonly ConcurrentQueue<(DateTimeOffset At, byte[] Bytes)> _incoming = new();
    readonly Timer _intake;
    bool _sendConfirmed, _disposed;

    public SerialViewModel(ISerialLink link, Func<IReadOnlyList<SerialPortInfo>> ports, IShellServices shell, string? port, TimeProvider? time = null)
    {
        _link = link;
        _ports = ports;
        _shell = shell;
        _time = time ?? TimeProvider.System;
        _link.Received += OnReceived;
        _link.Faulted += OnFaulted;
        _intake = new Timer(_ => { if (!_incoming.IsEmpty) _ = _shell.RunOnUiAsync(TakeIn); }, null, Intake, Intake);

        // The standard rates, and the odd ones boards actually use: 74880 (an ESP8266's boot messages), 250000 (3D
        // printers), and the megabaud rates of USB bridges.
        BaudRates = [300, 1200, 2400, 4800, 9600, 19_200, 38_400, 57_600, 74_880, 115_200, 230_400, 250_000, 460_800, 500_000,
                     921_600, 1_000_000, 2_000_000];
        DataBitsChoices = [5, 6, 7, 8];
        Parities = [.. Enum.GetValues<Parity>().Select(p => new SerialChoice<Parity>(p, ParityLabel(p)))];
        StopBitsChoices = [new(StopBits.One, "1"), new(StopBits.OnePointFive, 1.5.ToString(CultureInfo.CurrentCulture)), new(StopBits.Two, "2")];
        Handshakes =
        [
            new(Handshake.None, Str.Get("Serial.Handshake.None")),
            new(Handshake.XOnXOff, Str.Get("Serial.Handshake.XOnXOff")),
            new(Handshake.RequestToSend, Str.Get("Serial.Handshake.Rts")),
            new(Handshake.RequestToSendXOnXOff, Str.Get("Serial.Handshake.Both")),
        ];
        LineEndings =
        [
            new(SerialLineEnding.None, Str.Get("Serial.LineEnding.None")),
            new(SerialLineEnding.CR, "CR"),
            new(SerialLineEnding.LF, "LF"),
            new(SerialLineEnding.CRLF, "CR LF"),
        ];

        RefreshPorts();
        if (!string.IsNullOrWhiteSpace(port))
            SelectedPort = Ports.FirstOrDefault(p => p.Name.Equals(port, StringComparison.OrdinalIgnoreCase))
                ?? new SerialPortInfo(port.ToUpperInvariant(), null, null);
        _status = Str.Get("Serial.Status.Closed");
    }

    static string ParityLabel(Parity parity) => parity switch
    {
        Parity.Odd => Str.Get("Serial.Parity.Odd"),
        Parity.Even => Str.Get("Serial.Parity.Even"),
        Parity.Mark => Str.Get("Serial.Parity.Mark"),
        Parity.Space => Str.Get("Serial.Parity.Space"),
        _ => Str.Get("Serial.Parity.None"),
    };

    /// <summary>The port in view changed — the registration re-titles the tab and re-keys it.</summary>
    public event Action? PortChanged;

    public ObservableCollection<SerialPortInfo> Ports { get; } = [];
    public IReadOnlyList<int> BaudRates { get; }
    public IReadOnlyList<int> DataBitsChoices { get; }
    public IReadOnlyList<SerialChoice<Parity>> Parities { get; }
    public IReadOnlyList<SerialChoice<StopBits>> StopBitsChoices { get; }
    public IReadOnlyList<SerialChoice<Handshake>> Handshakes { get; }
    public IReadOnlyList<SerialChoice<SerialLineEnding>> LineEndings { get; }

    public ObservableCollection<SerialLine> Transcript { get; } = [];

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ConnectCommand))] private SerialPortInfo? _selectedPort;
    [ObservableProperty] private int _baudRate = SerialSettings.Default.BaudRate;
    [ObservableProperty] private int _dataBits = SerialSettings.Default.DataBits;
    [ObservableProperty] private Parity _parity = SerialSettings.Default.Parity;
    [ObservableProperty] private StopBits _stopBits = SerialSettings.Default.StopBits;
    [ObservableProperty] private Handshake _handshake = SerialSettings.Default.Handshake;
    [ObservableProperty] private bool _dtr;
    [ObservableProperty] private bool _rts;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDisconnected)), NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand), nameof(SendCommand))]
    private bool _isConnected;

    [ObservableProperty] private bool _showHex;
    [ObservableProperty] private bool _showTimes = true;
    [ObservableProperty] private string _sendText = "";
    [ObservableProperty] private bool _sendHex;
    [ObservableProperty] private SerialLineEnding _lineEnding = SerialLineEnding.CRLF;
    [ObservableProperty] private string _status;
    [ObservableProperty] private long _bytesIn;
    [ObservableProperty] private long _bytesOut;

    public bool IsDisconnected => !IsConnected;
    public string Title => SelectedPort?.Name ?? Str.Get("Serial.Page.Title");
    public string Port => SelectedPort?.Name ?? "";

    public SerialSettings Settings => new(BaudRate, DataBits, Parity, StopBits, Handshake, Dtr, Rts);

    partial void OnSelectedPortChanged(SerialPortInfo? value)
    {
        if (IsConnected) Disconnect();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Port));
        PortChanged?.Invoke();
    }

    partial void OnDtrChanged(bool value)
    {
        if (IsConnected) _link.Dtr = value;
    }

    partial void OnRtsChanged(bool value)
    {
        if (IsConnected) _link.Rts = value;
    }

    // ── Ports ──────────────────────────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void RefreshPorts()
    {
        var keep = SelectedPort?.Name;
        Ports.Clear();
        foreach (var port in _ports()) Ports.Add(port);
        if (keep is not null) SelectedPort = Ports.FirstOrDefault(p => p.Name.Equals(keep, StringComparison.OrdinalIgnoreCase)) ?? SelectedPort;
    }

    /// <summary>The shell re-pointing the tab at another port.</summary>
    public void Retarget(string port)
    {
        if (port.Equals(SelectedPort?.Name, StringComparison.OrdinalIgnoreCase)) return;
        RefreshPorts();
        SelectedPort = Ports.FirstOrDefault(p => p.Name.Equals(port, StringComparison.OrdinalIgnoreCase)) ?? new SerialPortInfo(port.ToUpperInvariant(), null, null);
    }

    // ── Connection ─────────────────────────────────────────────────────────────────────────────────────────

    bool CanConnect() => !IsConnected && SelectedPort is not null;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync() => Status = await ConnectCoreAsync();

    /// <summary>Opens the port with the settings shown; the result is what the status line and the agent are told.</summary>
    internal async Task<string> ConnectCoreAsync()
    {
        if (SelectedPort is not { } port) return Str.Get("Serial.Status.NoPort");
        if (IsConnected) return Str.Format("Serial.Status.Open", port.Name, Settings.Summary);
        var settings = Settings;
        try
        {
            await Task.Run(() => _link.Open(port.Name, settings));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return e is UnauthorizedAccessException ? Str.Format("Serial.Status.InUse", port.Name) : Str.Format("Serial.Status.CannotOpen", port.Name, e.Message);
        }
        IsConnected = true;
        _sendConfirmed = false;
        Add(SerialLine.Note(Str.Format("Serial.Note.Connected", port.Name, settings.Summary)));
        return Str.Format("Serial.Status.Open", port.Name, settings.Summary);
    }

    bool CanDisconnect() => IsConnected;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        if (!IsConnected) return;
        _link.Close();
        TakeIn();
        IsConnected = false;
        Add(SerialLine.Note(Str.Get("Serial.Note.Disconnected")));
        Status = Str.Get("Serial.Status.Closed");
    }

    void OnFaulted(string message) => _ = _shell.RunOnUiAsync(() =>
    {
        if (_disposed || !IsConnected) return;
        _link.Close();
        TakeIn();
        IsConnected = false;
        Add(SerialLine.Note(Str.Format("Serial.Note.Lost", message)));
        Status = Str.Format("Serial.Status.Lost", Port, message);
    });

    // ── Receiving ──────────────────────────────────────────────────────────────────────────────────────────

    void OnReceived(byte[] bytes) => _incoming.Enqueue((_time.GetLocalNow(), bytes));

    /// <summary>Takes in what arrived since last time, line by line.</summary>
    internal void TakeIn()
    {
        while (_incoming.TryDequeue(out var block))
        {
            BytesIn += block.Bytes.Length;
            var rest = block.Bytes.AsSpan();
            while (rest.Length > 0)
            {
                var open = Transcript.Count > 0 ? Transcript[^1] : null;
                bool extend = open is { Direction: SerialDirection.Received, EndsLine: false } && open.Length < LongestLine && block.At - open.LastAt < Quiet;
                int newline = rest.IndexOf((byte)'\n');
                int room = extend ? LongestLine - open!.Length : LongestLine;
                int take = Math.Min(newline >= 0 ? newline + 1 : rest.Length, room);
                if (extend) open!.Append(rest[..take], block.At);
                else Add(new SerialLine(block.At, SerialDirection.Received, rest[..take]));
                rest = rest[take..];
            }
        }
    }

    void Add(SerialLine line)
    {
        Transcript.Add(line);
        if (Transcript.Count > TranscriptLines) Transcript.RemoveAt(0);
    }

    // ── Sending ────────────────────────────────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task SendAsync()
    {
        if (!TryEncode(SendText, SendHex, LineEnding, out var bytes, out var error)) { Status = error!; return; }
        if (!_sendConfirmed)
        {
            if (!await _shell.ConfirmAsync(Str.Format("Serial.Confirm.Title", Port), Str.Format("Serial.Confirm.Message", Port))) return;
            _sendConfirmed = true;
        }
        var (sent, message) = await SendCoreAsync(bytes, CancellationToken.None);
        Status = message;
        if (sent) SendText = "";
    }

    /// <summary>Writes the bytes and puts them in the transcript; says whether it did, and what to tell the user.</summary>
    internal async Task<(bool Sent, string Message)> SendCoreAsync(byte[] bytes, CancellationToken ct)
    {
        if (!IsConnected) return (false, Str.Get("Serial.Status.NotConnected"));
        try
        {
            await _link.WriteAsync(bytes, ct);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException)
        {
            return (false, Str.Format("Serial.Status.SendFailed", e.Message));
        }
        BytesOut += bytes.Length;
        Add(new SerialLine(DateTimeOffset.Now, SerialDirection.Sent, bytes));
        return (true, Str.Format("Serial.Status.Sent", bytes.Length));
    }

    /// <summary>Text with its line ending as UTF-8, or hex as the bytes it names.</summary>
    internal static bool TryEncode(string text, bool hex, SerialLineEnding ending, out byte[] bytes, out string? error)
    {
        error = null;
        if (hex)
        {
            var digits = string.Concat(text.Split([' ', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? t[2..] : t)
                .Select(t => t.Length % 2 == 1 ? "0" + t : t));
            if (digits.Length == 0 || !digits.All(Uri.IsHexDigit)) { bytes = []; error = Str.Get("Serial.Status.NotHex"); return false; }
            bytes = Convert.FromHexString(digits);
            return true;
        }
        string suffix = ending switch { SerialLineEnding.CR => "\r", SerialLineEnding.LF => "\n", SerialLineEnding.CRLF => "\r\n", _ => "" };
        bytes = Encoding.UTF8.GetBytes(text + suffix);
        if (bytes.Length == 0) { error = Str.Get("Serial.Status.NothingToSend"); return false; }
        return true;
    }

    // ── The transcript ─────────────────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Clear()
    {
        Transcript.Clear();
        BytesIn = BytesOut = 0;
    }

    [RelayCommand]
    [LocalDiskOnly("a transcript of this computer's serial port, saved where the Windows save dialog picked")]
    private async Task SaveAsync()
    {
        if (await _shell.PickSaveFileAsync($"{Port} {DateTime.Now:yyyy-MM-dd HHmm}.txt", [".txt"]) is not { } path) return;
        await File.WriteAllTextAsync(path, TranscriptText(Transcript.Count, withHex: true));
        Status = Str.Format("Serial.Status.Saved", Path.GetFileName(path));
    }

    /// <summary>The last <paramref name="lines"/> lines as text: time, direction, text, and the hex beneath when asked.</summary>
    internal string TranscriptText(int lines, bool withHex)
    {
        var sb = new StringBuilder();
        foreach (var line in Transcript.Skip(Math.Max(0, Transcript.Count - lines)))
        {
            sb.Append(line.Time).Append(' ').Append(line.Arrow).Append(' ').AppendLine(line.Text);
            if (withHex && line.Direction != SerialDirection.Note) sb.Append("               ").AppendLine(line.Hex);
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _intake.Dispose();
        _link.Received -= OnReceived;
        _link.Faulted -= OnFaulted;
        _link.Dispose();
    }
}
