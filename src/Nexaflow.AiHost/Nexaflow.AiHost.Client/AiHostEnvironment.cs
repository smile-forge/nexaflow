namespace Nexaflow.AiHost.Client;

/// <summary>What a host is started with, beyond its pipe.</summary>
/// <param name="RuntimePack">The runtime pack to load ONNX Runtime from, or null for the install's CPU runtime.</param>
public sealed record AiHostEnvironment(string? RuntimePack)
{
    public static AiHostEnvironment Cpu { get; } = new(RuntimePack: null);
}
