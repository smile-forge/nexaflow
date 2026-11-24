namespace Nexaflow.AiHost.Client;

/// <summary>
/// What the app expects the host to run on. The host chooses its runtime itself — the CUDA pack, from its own store, where this
/// machine's cards and driver run it — so a change here is what restarts it: a pack that has just arrived is taken up.
/// </summary>
/// <param name="Cuda">Whether the CUDA pack is in the host's store and this machine runs it.</param>
public sealed record AiHostEnvironment(bool Cuda)
{
    public static AiHostEnvironment Cpu { get; } = new(Cuda: false);

    public static AiHostEnvironment OnCuda { get; } = new(Cuda: true);
}
