using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.IO.Usb.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Icons;
using Nexaflow.IO.Common.Audio;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe.AudioTools;

/// <summary>One channel's meter: its peak and RMS in dBFS, and where each sits on a 60 dB scale.</summary>
public sealed partial class AudioMeterRow(string name) : ObservableObject
{
    const float Range = 60;

    public string Name { get; } = name;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PeakFill), nameof(Text))] private float _peakDb = AudioLevels.Floor;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RmsFill))] private float _rmsDb = AudioLevels.Floor;

    public double PeakFill => Math.Clamp((PeakDb + Range) / Range, 0, 1);
    public double RmsFill => Math.Clamp((RmsDb + Range) / Range, 0, 1);
    public string Text => PeakDb <= AudioLevels.Floor ? "—" : $"{PeakDb:0.0} dB";
}

/// <summary>
/// H3, audio input: every channel of one of the device's recording endpoints metered live while the tile is open, and
/// a recording of a few seconds saved as a float WAV with every channel it has — what the device sent, unprocessed.
/// </summary>
public sealed partial class AudioInputTool : ObservableObject, IUsbTool, IUsbToolDebugDetails
{
    readonly UsbFunctionNode _function;
    readonly AudioEndpointInfo _endpoint;
    readonly IAudioDevices _devices;
    readonly IUsbToolHost _host;
    IAudioLevelMeter? _meter;
    CancellationTokenSource? _recording;
    bool _shown;

    internal AudioInputTool(UsbFunctionNode function, AudioEndpointInfo endpoint, IAudioDevices devices, IUsbToolHost host)
    {
        _function = function;
        _endpoint = endpoint;
        _devices = devices;
        _host = host;
        foreach (var name in endpoint.ChannelNames) Meters.Add(new AudioMeterRow(name));
        Durations = [.. new[] { 5, 10, 30 }.Select(s => new AudioChoice<int>(s, Str.Format("AudioTools.Duration", s)))];
        _status = Str.Get("AudioTools.Status.Idle");
    }

    public string ToolId => "usb.audio.input";
    public string Key => $"{ToolId}:{_endpoint.Id}";
    public string Title => Str.Get("AudioTools.Input.Title");
    public IconRef Icon => IconRef.Fluent("mic");
    public string Description => Str.Get("AudioTools.Input.Description");
    public string? FunctionInstanceId => _function.InstanceId;
    public UsbToolSize Size => UsbToolSize.Compact;
    public bool IsBusy => IsRecording;

    public string EndpointName => _endpoint.Name;
    public string Format => Str.Format("AudioTools.Format", _endpoint.Channels, (_endpoint.SampleRate / 1000.0).ToString("0.#", CultureInfo.CurrentCulture));

    public ObservableCollection<AudioMeterRow> Meters { get; } = [];
    public IReadOnlyList<AudioChoice<int>> Durations { get; }

    [ObservableProperty] private int _seconds = 5;
    [ObservableProperty] private bool _isListening;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy))] private bool _isRecording;
    [ObservableProperty] private string _status;
    [ObservableProperty] private string? _lastRecording;

    /// <summary>Opening the tile starts the meters.</summary>
    public async Task ActivateAsync(CancellationToken ct)
    {
        _shown = true;
        await ListenAsync();
    }

    /// <summary>Shutting it stops them — unless a recording is running, which finishes first.</summary>
    public void Deactivate()
    {
        _shown = false;
        if (!IsRecording) StopListening();
    }

    async Task ListenAsync()
    {
        if (_meter is not null) return;
        var meter = _meter = _devices.Listen(_endpoint.Id);
        meter.Reading += OnReading;
        try
        {
            await meter.StartAsync();
            IsListening = true;
            Status = Str.Get("AudioTools.Status.Listening");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            StopListening();
            Status = Str.Format("AudioTools.Status.Failed", e.Message);
            throw;
        }
    }

    void StopListening()
    {
        if (_meter is { } meter)
        {
            meter.Reading -= OnReading;
            meter.Dispose();
        }
        _meter = null;
        IsListening = false;
        foreach (var row in Meters) (row.PeakDb, row.RmsDb) = (AudioLevels.Floor, AudioLevels.Floor);
        if (!IsRecording) Status = Str.Get("AudioTools.Status.Idle");
    }

    void OnReading(AudioLevelReading reading) => _ = _host.RunOnUiAsync(() =>
    {
        for (int c = 0; c < Meters.Count && c < reading.PeakDb.Length; c++)
            (Meters[c].PeakDb, Meters[c].RmsDb) = (reading.PeakDb[c], reading.RmsDb[c]);
    });

    // ── Recording ──────────────────────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RecordAsync()
    {
        string name = $"{Sanitise(EndpointName)} {DateTime.Now:yyyy-MM-dd HHmmss}.wav";
        if (await _host.PickSaveFileAsync(name, [".wav"]) is not { } path) return;
        await RecordToAsync(path, Seconds, CancellationToken.None);
    }

    [RelayCommand]
    private void StopRecording() => _recording?.Cancel();

    async Task<string> RecordToAsync(string path, double seconds, CancellationToken ct)
    {
        if (IsRecording) return Str.Get("AudioTools.Status.AlreadyRecording");
        using var recording = _recording = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsRecording = true;
        Status = Str.Format("AudioTools.Status.Recording", seconds);
        try
        {
            await ListenAsync();
            var clip = await _meter!.RecordAsync(TimeSpan.FromSeconds(seconds), recording.Token);
            await File.WriteAllBytesAsync(path, WavAudio.WriteFloat(clip), CancellationToken.None);
            LastRecording = Str.Format("AudioTools.Recorded", Path.GetFileName(path), clip.Length.TotalSeconds.ToString("0.#", CultureInfo.CurrentCulture), clip.Channels, clip.Rate);
            _host.Log(this, $"Recorded {clip.Length.TotalSeconds:0.#} s from {EndpointName} to {path}");
            return $"Recorded {clip.Length.TotalSeconds:0.#} s, {clip.Channels} channel(s) at {clip.Rate} Hz, to {path}.";
        }
        catch (OperationCanceledException)
        {
            LastRecording = Str.Get("AudioTools.RecordingCancelled");
            return LastRecording;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            LastRecording = Str.Format("AudioTools.Status.Failed", e.Message);
            return LastRecording;
        }
        finally
        {
            _recording = null;
            IsRecording = false;
            if (!_shown) StopListening();
            else Status = Str.Get("AudioTools.Status.Listening");
        }
    }

    static string Sanitise(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    // ── The agent ──────────────────────────────────────────────────────────────────────────────────────────

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Recording endpoint '{EndpointName}': {_endpoint.Channels} channel(s) at {_endpoint.SampleRate} Hz.");
        sb.AppendLine(IsListening ? "Levels: " + string.Join(", ", Meters.Select(m => $"{m.Name} peak {m.Text}")) : "Not listening (the tile is shut).");
        if (LastRecording is not null) sb.AppendLine($"Last recording: {LastRecording}");
        return sb.ToString();
    }

    public IReadOnlyList<UsbToolOperation> Operations =>
    [
        new("read_levels", "Listens for half a second and reports each channel's peak and RMS level in dBFS.", false, [],
            async (_, ct) =>
            {
                bool was = IsListening;
                await ListenAsync();
                await Task.Delay(500, ct);
                var text = string.Join("\n", Meters.Select(m => $"{m.Name}: peak {m.PeakDb:0.0} dBFS, RMS {m.RmsDb:0.0} dBFS"));
                if (!was && !_shown) StopListening();
                return text;
            }),
        new("record", "Records the endpoint to a 32-bit float WAV with every channel.", true,
            [new UsbToolArgument("path", "Where to save the WAV on this computer."),
             new UsbToolArgument("seconds", "How long, 1–60. Default 5.", Required: false)],
            (args, ct) =>
            {
                double seconds = args.TryGetValue("seconds", out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 1, 60) : 5;
                return RecordToAsync(args["path"], seconds, ct);
            }),
    ];

    public Task<JsonObject> DebugDetailsAsync(UsbDebugMask mask, CancellationToken ct) => Task.FromResult(AudioEndpointDebug.Of(_endpoint, mask));

    public void Dispose()
    {
        _recording?.Cancel();
        _shown = false;
        StopListening();
    }
}
