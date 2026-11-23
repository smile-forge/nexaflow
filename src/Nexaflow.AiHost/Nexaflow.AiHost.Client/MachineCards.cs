namespace Nexaflow.AiHost.Client;

/// <summary>An NVIDIA card the AI host can run models on: its name, all its memory, and its PCI bus id.</summary>
public sealed record MachineCard(string Name, long TotalBytes, string? BusId = null);

/// <summary>
/// What this machine has for the AI host's CUDA runtime, read before any host runs: its NVIDIA cards, in the order the host
/// numbers them — PCI bus order — and the newest CUDA version their driver runs. Read through NVIDIA's management library,
/// which comes with the driver on Windows and Linux; a machine without it has no cards here and no CUDA. Never throws.
/// </summary>
public sealed record MachineCards(IReadOnlyList<MachineCard> Cards, Version? CudaDriver)
{
    /// <summary>A machine with no NVIDIA card: the host runs on the CPU.</summary>
    public static MachineCards None { get; } = new([], null);

    /// <summary>This machine, as its NVIDIA driver describes it now.</summary>
    public static MachineCards Probe()
    {
        using var nvml = Nvml.Open();
        return nvml is null ? None : new([.. nvml.Read().Select(c => new MachineCard(c.Name, c.TotalBytes, c.BusId))], nvml.CudaDriver());
    }
}
