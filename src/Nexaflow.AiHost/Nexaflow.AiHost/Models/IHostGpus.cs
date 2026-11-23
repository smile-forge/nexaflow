using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Models;

/// <summary>The GPUs the host runs models on, as admission reads them: each one's budget.</summary>
internal interface IHostGpus
{
    /// <exception cref="HostRequestException">This host has no GPU <paramref name="device"/>.</exception>
    HostGpu Gpu(int device);

    /// <summary>
    /// What models may hold on GPU <paramref name="device"/> together now, the host's own <paramref name="heldBytes"/> among
    /// them: what is free on the card with those counted back in, less what the runtime keeps for itself — so what another
    /// program has taken since the host started is not counted twice, and what it has given back is there to use. A card that
    /// cannot be read has the budget it started with.
    /// </summary>
    /// <exception cref="HostRequestException">This host has no GPU <paramref name="device"/>.</exception>
    long BudgetNow(int device, long heldBytes) => Gpu(device).BudgetBytes;
}
