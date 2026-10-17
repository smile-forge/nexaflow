using Nexaflow.IO.Common.Audio;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Plugins;

namespace Nexaflow.Features.UsbProbe.AudioTools;

/// <summary>What the audio tools need of the machine: its endpoints, a way to play on one, a way to listen to one.</summary>
public interface IAudioDevices
{
    IReadOnlyList<AudioEndpointInfo> Endpoints();

    Task PlayAsync(string endpointId, IAudioSignal signal, CancellationToken ct);

    IAudioLevelMeter Listen(string endpointId);
}

/// <summary>The Windows audio engine, through the WASAPI code Nexaflow owns in IO.Common.</summary>
public sealed class WindowsAudioDevices : IAudioDevices
{
    public IReadOnlyList<AudioEndpointInfo> Endpoints() => AudioEndpointCatalog.List();

    public Task PlayAsync(string endpointId, IAudioSignal signal, CancellationToken ct) => AudioSignalPlayer.PlayAsync(endpointId, signal, ct);

    public IAudioLevelMeter Listen(string endpointId) => new AudioLevelMeter(endpointId);
}

/// <summary>
/// Offers an output tool for each playback endpoint and an input tool for each recording endpoint that a USB audio
/// function of the device drives — matched by the devnode behind the endpoint, so a headset offers its own speaker and
/// microphone and nothing else.
/// </summary>
[Subfeature("usbprobe", "audio",
    DisplayName = "Audio",
    Description = "Plays test signals on a USB audio device's outputs — tones, sweeps, a WAV, each channel in turn — and meters and records its inputs.",
    Order = 1)]
public sealed class UsbAudioToolProvider : IUsbToolProvider
{
    readonly IAudioDevices _devices;

    public UsbAudioToolProvider() : this(new WindowsAudioDevices()) { }

    internal UsbAudioToolProvider(IAudioDevices devices) => _devices = devices;

    public Uri Templates { get; } = new("/Nexaflow.Features.UsbProbe.AudioTools;component/Views/AudioTemplates.xaml", UriKind.Relative);

    public IReadOnlyList<IUsbTool> Offer(UsbDeviceNode device, IUsbToolHost host)
    {
        var audio = device.Functions.Where(f => f.Mode != UsbFunctionMode.CompositeParent && device.Speaks(f, UsbClassCodes.Audio)).ToList();
        if (audio.Count == 0) return [];

        var tools = new List<IUsbTool>();
        foreach (var endpoint in _devices.Endpoints())
        {
            if (FunctionFor(device, audio, endpoint.AdapterInstanceId) is not { } function) continue;
            tools.Add(endpoint.Flow == AudioFlow.Playback
                ? new AudioOutputTool(function, endpoint, _devices, host)
                : new AudioInputTool(function, endpoint, _devices, host));
        }
        return [.. tools.OrderBy(t => t is AudioInputTool).ThenBy(t => t.Key, StringComparer.Ordinal)];
    }

    static UsbFunctionNode? FunctionFor(UsbDeviceNode device, List<UsbFunctionNode> audio, string? adapter)
    {
        if (adapter is null) return null;
        return audio.FirstOrDefault(f => string.Equals(f.InstanceId, adapter, StringComparison.OrdinalIgnoreCase))
            ?? (string.Equals(device.InstanceId, adapter, StringComparison.OrdinalIgnoreCase) ? audio[0] : null);
    }
}
