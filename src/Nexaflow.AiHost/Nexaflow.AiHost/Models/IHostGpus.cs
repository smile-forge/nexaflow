using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Models;

/// <summary>The GPUs the host runs models on, as admission reads them: each one's budget.</summary>
internal interface IHostGpus
{
    /// <exception cref="HostRequestException">This host has no GPU <paramref name="device"/>.</exception>
    HostGpu Gpu(int device);
}
