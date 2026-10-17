using System.Text.Json.Nodes;
using Nexaflow.IO.Common.Audio;
using Nexaflow.IO.Usb.Diagnostics;

namespace Nexaflow.Features.UsbProbe.AudioTools;

/// <summary>An audio endpoint as both audio tools put it in a device's debug details: the format Windows mixes at,
/// which is what the tools generate and record in, and the adapter devnode it was matched to.</summary>
static class AudioEndpointDebug
{
    public static JsonObject Of(AudioEndpointInfo endpoint, UsbDebugMask mask) => new()
    {
        ["endpointId"] = endpoint.Id,
        ["name"] = endpoint.Name,
        ["flow"] = endpoint.Flow.ToString(),
        ["channels"] = endpoint.Channels,
        ["sampleRate"] = endpoint.SampleRate,
        ["channelMask"] = $"0x{endpoint.ChannelMask:X8}",
        ["channelNames"] = new JsonArray([.. endpoint.ChannelNames.Select(n => (JsonNode)n)]),
        ["adapter"] = mask.Text(endpoint.AdapterInstanceId),
    };
}
