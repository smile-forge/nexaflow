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

/// <summary>What the output tool plays.</summary>
public enum AudioTestSignal { Tone, Sweep, File }

/// <summary>A choice in a list: the invariant value and the words shown for it.</summary>
public sealed record AudioChoice<T>(T Value, string Label);

/// <summary>
/// H2, audio output: plays a test signal on one of the device's playback endpoints — a tone at a chosen frequency
/// and level, a sweep across the audible range, or a WAV — on every channel or one, and can walk the channels one at a
/// time naming each as it sounds. Signals are generated at the endpoint's own mix format, so a tone sent to "Side
/// left" reaches that speaker and no other.
/// </summary>
public sealed partial class AudioOutputTool : ObservableObject, IUsbTool, IUsbToolDebugDetails
{
    const double MinHz = 20, MaxHz = 20_000;
    static readonly TimeSpan SweepLength = TimeSpan.FromSeconds(10);

    readonly UsbFunctionNode _function;
    readonly AudioEndpointInfo _endpoint;
    readonly IAudioDevices _devices;
    readonly IUsbToolHost _host;
    CancellationTokenSource? _playing;
    Timer? _watch;

    internal AudioOutputTool(UsbFunctionNode function, AudioEndpointInfo endpoint, IAudioDevices devices, IUsbToolHost host)
    {
        _function = function;
        _endpoint = endpoint;
        _devices = devices;
        _host = host;
        Channels = [new(-1, Str.Get("AudioTools.Channel.All")), .. endpoint.ChannelNames.Select((name, i) => new AudioChoice<int>(i, name))];
        Signals =
        [
            new(AudioTestSignal.Tone, Str.Get("AudioTools.Signal.Tone")),
            new(AudioTestSignal.Sweep, Str.Get("AudioTools.Signal.Sweep")),
            new(AudioTestSignal.File, Str.Get("AudioTools.Signal.File")),
        ];
        _channel = -1;
        _status = Str.Get("AudioTools.Status.Idle");
    }

    public string ToolId => "usb.audio.output";
    public string Key => $"{ToolId}:{_endpoint.Id}";
    public string Title => Str.Get("AudioTools.Output.Title");
    public IconRef Icon => IconRef.Fluent("speaker_2");
    public string Description => Str.Get("AudioTools.Output.Description");
    public string? FunctionInstanceId => _function.InstanceId;
    public UsbToolSize Size => UsbToolSize.Compact;
    public bool IsBusy => IsPlaying;

    public string EndpointName => _endpoint.Name;
    public string Format => Str.Format("AudioTools.Format", _endpoint.Channels, (_endpoint.SampleRate / 1000.0).ToString("0.#", CultureInfo.CurrentCulture));

    public IReadOnlyList<AudioChoice<int>> Channels { get; }
    public IReadOnlyList<AudioChoice<AudioTestSignal>> Signals { get; }

    [ObservableProperty] private AudioTestSignal _signal = AudioTestSignal.Tone;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FrequencyText), nameof(FrequencyPosition))] private double _frequency = 1000;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(LevelText))] private double _level = -20;
    [ObservableProperty] private int _channel;
    [ObservableProperty] private string? _file;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy))] private bool _isPlaying;
    [ObservableProperty] private string _status;

    /// <summary>The channel sounding now while the channels are walked; null otherwise.</summary>
    [ObservableProperty] private string? _sounding;

    public bool IsFile => Signal == AudioTestSignal.File;
    public string FrequencyText => HzText(Frequency);
    public string LevelText => DbText(Level);

    static string HzText(double hz) => hz >= 1000 ? $"{hz / 1000:0.00} kHz" : $"{hz:0} Hz";
    static string DbText(double db) => $"{db:0} dBFS";

    /// <summary>The frequency as a slider position, 0 to 1 on a log scale: equal travel per octave.</summary>
    public double FrequencyPosition
    {
        get => Math.Log(Frequency / MinHz) / Math.Log(MaxHz / MinHz);
        set => Frequency = Math.Round(MinHz * Math.Pow(MaxHz / MinHz, Math.Clamp(value, 0, 1)));
    }

    partial void OnSignalChanged(AudioTestSignal value) => OnPropertyChanged(nameof(IsFile));

    public Task ActivateAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>A test signal is live: shutting the tile stops it.</summary>
    public void Deactivate() => Stop();

    // ── Playing ────────────────────────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task PickFileAsync()
    {
        if (await _host.PickOpenFileAsync([".wav"]) is { } path)
        {
            File = path;
            Signal = AudioTestSignal.File;
        }
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        IAudioSignal signal;
        try { signal = Build(); }
        catch (Exception e) when (e is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Status = e.Message;
            return;
        }
        await PlayCoreAsync(signal, Describe(Signal), null);
    }

    [RelayCommand]
    private Task IdentifyChannelsAsync()
    {
        var walk = new ChannelWalkSignal(1000, AudioLevels.Amplitude(Level), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(400));
        return PlayCoreAsync(walk, Str.Get("AudioTools.Status.Identifying"), walk);
    }

    [RelayCommand]
    private void Stop() => _playing?.Cancel();

    IAudioSignal Build()
    {
        var channels = SelectionOf(Channel);
        double amplitude = AudioLevels.Amplitude(Level);
        return Signal switch
        {
            AudioTestSignal.Sweep => new SweepSignal(MinHz, MaxHz, SweepLength, amplitude, channels),
            AudioTestSignal.File when File is { } path => Clip(path, amplitude),
            AudioTestSignal.File => throw new InvalidOperationException(Str.Get("AudioTools.Status.NoFile")),
            _ => new ToneSignal(Frequency, amplitude, channels),
        };
    }

    static ClipSignal Clip(string path, double amplitude)
    {
        var clip = WavAudio.ReadClip(System.IO.File.ReadAllBytes(path));
        return new ClipSignal(clip.Samples, clip.Channels, clip.Rate, amplitude);
    }

    string Describe(AudioTestSignal signal) => signal switch
    {
        AudioTestSignal.Sweep => SweepingText(Channel, Level),
        AudioTestSignal.File => Str.Format("AudioTools.Status.PlayingFile", System.IO.Path.GetFileName(File ?? ""), ChannelLabel(Channel)),
        _ => ToneText(Frequency, Channel, Level),
    };

    string ToneText(double hz, int channel, double db) => Str.Format("AudioTools.Status.PlayingTone", HzText(hz), ChannelLabel(channel), DbText(db));

    string SweepingText(int channel, double db) => Str.Format("AudioTools.Status.Sweeping", ChannelLabel(channel), DbText(db));

    string ChannelLabel(int channel) => Channels.FirstOrDefault(c => c.Value == channel)?.Label ?? "";

    /// <summary>
    /// Plays until the signal ends or Stop, showing <paramref name="status"/> meanwhile; null when it played, or why it
    /// could not. The status is the person's words, so an agent caller words its own result.
    /// </summary>
    async Task<string?> PlayCoreAsync(IAudioSignal signal, string status, ChannelWalkSignal? walk, CancellationToken ct = default)
    {
        Stop();
        using var playing = _playing = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsPlaying = true;
        Status = status;
        if (walk is not null) _watch = new Timer(_ => Watch(walk), null, 0, 100);
        bool failed = false;
        try
        {
            await _devices.PlayAsync(_endpoint.Id, signal, playing.Token);
            _host.Log(this, $"{status} — {EndpointName}");
            return null;
        }
        catch (Exception e) when (e is not OutOfMemoryException and not OperationCanceledException)
        {
            failed = true;
            Status = Str.Format("AudioTools.Status.Failed", e.Message);
            return Status;
        }
        finally
        {
            _watch?.Dispose();
            _watch = null;
            if (ReferenceEquals(_playing, playing))
            {
                _playing = null;
                IsPlaying = false;
                Sounding = null;
                if (!failed) Status = Str.Get("AudioTools.Status.Idle");
            }
        }
    }

    void Watch(ChannelWalkSignal walk)
    {
        int channel = walk.CurrentChannel;
        string? name = channel >= 0 && channel < _endpoint.ChannelNames.Count ? _endpoint.ChannelNames[channel] : null;
        _ = _host.RunOnUiAsync(() => Sounding = name);
    }

    // ── The agent ──────────────────────────────────────────────────────────────────────────────────────────

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Playback endpoint '{EndpointName}': {_endpoint.Channels} channel(s) at {_endpoint.SampleRate} Hz — {string.Join(", ", _endpoint.ChannelNames)}.");
        sb.AppendLine(IsPlaying ? $"Playing: {Status}" : "Idle.");
        return sb.ToString();
    }

    public IReadOnlyList<UsbToolOperation> Operations =>
    [
        new("play_tone", "Plays a sine tone on the endpoint for a few seconds.", true,
            [new UsbToolArgument("frequency_hz", "Frequency, 20–20000. Default 1000.", Required: false),
             new UsbToolArgument("level_db", "Level in dBFS, -60 to 0. Default -20.", Required: false),
             new UsbToolArgument("channel", "A channel name from the endpoint's list, or 'all'. Default all.", Required: false),
             new UsbToolArgument("seconds", "How long, 0.5–30. Default 2.", Required: false)],
            (args, ct) => AgentToneAsync(args, ct)),
        new("play_sweep", "Plays a 10-second logarithmic sweep from 20 Hz to 20 kHz.", true,
            [new UsbToolArgument("level_db", "Level in dBFS, -60 to 0. Default -20.", Required: false),
             new UsbToolArgument("channel", "A channel name, or 'all'. Default all.", Required: false)],
            async (args, ct) =>
            {
                int channel = ChannelIndex(args);
                double db = Db(args);
                return await PlayCoreAsync(new SweepSignal(MinHz, MaxHz, SweepLength, AudioLevels.Amplitude(db), SelectionOf(channel)),
                    SweepingText(channel, db), null, ct) ?? "Played the sweep, 20 Hz to 20 kHz.";
            }),
        new("identify_channels", "Plays a one-second tone on each channel in turn, in channel order, naming each.", true,
            [new UsbToolArgument("level_db", "Level in dBFS, -60 to 0. Default -20.", Required: false)],
            async (args, ct) =>
            {
                return await PlayCoreAsync(new ChannelWalkSignal(1000, AudioLevels.Amplitude(Db(args)), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(400)),
                    Str.Get("AudioTools.Status.Identifying"), null, ct)
                    ?? $"Played a tone on each channel in order: {string.Join(", ", _endpoint.ChannelNames)}.";
            }),
        new("stop", "Stops whatever is playing.", false, [], (_, _) => { Stop(); return Task.FromResult("Stopped."); }),
    ];

    async Task<string> AgentToneAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        double hz = Math.Clamp(Number(args, "frequency_hz", 1000), MinHz, MaxHz);
        double seconds = Math.Clamp(Number(args, "seconds", 2), 0.5, 30);
        int channel = ChannelIndex(args);
        double db = Db(args);
        return await PlayCoreAsync(new ToneSignal(hz, AudioLevels.Amplitude(db), SelectionOf(channel), TimeSpan.FromSeconds(seconds)),
            ToneText(hz, channel, db), null, ct) ?? $"Played {hz:0} Hz for {seconds:0.#} s.";
    }

    static double Db(IReadOnlyDictionary<string, string> args) => Math.Clamp(Number(args, "level_db", -20), -60, 0);

    static double Number(IReadOnlyDictionary<string, string> args, string name, double fallback) =>
        args.TryGetValue(name, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>The channel the agent named, as an index into the endpoint's channels; -1 for every channel.</summary>
    int ChannelIndex(IReadOnlyDictionary<string, string> args)
    {
        if (!args.TryGetValue("channel", out var name) || name.Equals("all", StringComparison.OrdinalIgnoreCase)) return -1;
        int index = _endpoint.ChannelNames.ToList().FindIndex(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? throw new ArgumentException($"No channel '{name}'. This endpoint has: {string.Join(", ", _endpoint.ChannelNames)}.") : index;
    }

    static ChannelSelection SelectionOf(int channel) => channel < 0 ? ChannelSelection.All : ChannelSelection.Only(channel);

    public Task<JsonObject> DebugDetailsAsync(UsbDebugMask mask, CancellationToken ct) => Task.FromResult(AudioEndpointDebug.Of(_endpoint, mask));

    public void Dispose()
    {
        _playing?.Cancel();
        _watch?.Dispose();
    }
}
