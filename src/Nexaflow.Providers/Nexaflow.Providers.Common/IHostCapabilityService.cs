namespace Nexaflow.Providers.Common;

/// <summary>
/// Read-only view of the host's CPU / GPU / RAM capabilities, exposed to providers so a local-inference
/// provider can decide which model variants the machine can actually run — without depending on Core.
/// Implemented in Core by the host-capability probe; a provider receives it by declaring an
/// <see cref="IHostCapabilityService"/> constructor parameter (injected by the host like the config
/// and <see cref="IBackgroundActivityManager"/>).
/// </summary>
public interface IHostCapabilityService
{
    /// <summary>The probe result, or null while the probe is still running (or if it never ran).</summary>
    HostCapabilities? Report { get; }

    /// <summary>True once <see cref="Report"/> is populated.</summary>
    bool IsReady { get; }
}

/// <summary>
/// Hardware capabilities relevant to running local LLMs. Memory figures are in MB. GPU fields are
/// zero / null when no usable NVIDIA GPU is detected.
/// </summary>
public sealed record HostCapabilities(
    bool    Avx,
    bool    Avx2,
    bool    Fma,
    bool    F16C,
    bool    CudaAvailable,
    int     CudaMajorVersion,
    string? GpuName,
    double  GpuComputeCapability,
    int     GpuVramMb,
    int     TotalRamMb);
