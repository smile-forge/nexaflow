using Nexaflow.AiHost.Client;
using Nexaflow.Features.Common.Dependencies;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.LocalAi;

// What Nexaflow's own models need to run on a GPU, declared like any feature's component so Options → About lists each
// with the rest. The judging is LocalAiRequirements'; these name each requirement and hand it the app's machine.

/// <summary>An NVIDIA GPU with memory of its own: where Nexaflow's own models run, where there is one.</summary>
internal sealed class NvidiaGpuDependency : IExternalDependency
{
    public string Id          => "nvidia-gpu";
    public string DisplayName => Str.Get("Shell.Components.NvidiaGpu.Name");
    public string Description => Str.Get("Shell.Components.NvidiaGpu.Description");
    public ExternalDependencyKind Kind => ExternalDependencyKind.Optional;
    public string? InstallUrl => null;

    public ExternalDependencyStatus Probe() => LocalAiRequirements.Gpu(LocalAiRequirements.Hardware);
}

/// <summary>NVIDIA's driver, new enough for the CUDA the pack's libraries are built for.</summary>
internal sealed class CudaDriverDependency : IExternalDependency
{
    public string Id          => "nvidia-cuda-driver";
    public string DisplayName => Str.Get("Shell.Components.CudaDriver.Name");
    public string Description => Str.Format("Shell.Components.CudaDriver.DescriptionFormat", CudaRuntimePack.MinimumDriver.ToString(2));
    public ExternalDependencyKind Kind => ExternalDependencyKind.Optional;
    public string? InstallUrl => "https://www.nvidia.com/Download/index.aspx";

    public ExternalDependencyStatus Probe() => LocalAiRequirements.CudaDriver(LocalAiRequirements.Hardware);
}

/// <summary>The CUDA pack the models' runtime loads its GPU libraries from, downloaded into the model store.</summary>
internal sealed class CudaRuntimePackDependency : IExternalDependency
{
    public string Id          => "cuda-runtime-pack";
    public string DisplayName => Str.Get("Shell.Components.CudaPack.Name");
    public string Description => Str.Format("Shell.Components.CudaPack.DescriptionFormat", SizeFormatter.FormatBytes(CudaRuntimePack.Size));
    public ExternalDependencyKind Kind => ExternalDependencyKind.Optional;

    /// <summary>No link: it is downloaded by the app, with the models.</summary>
    public string? InstallUrl => null;

    public ExternalDependencyStatus Probe() => LocalAiRequirements.CudaPack(LocalAiRequirements.Hardware, LocalAiRequirements.Store);
}

/// <summary>The process every model Nexaflow runs itself is loaded in, installed with the app.</summary>
internal sealed class AiHostDependency : IExternalDependency
{
    public string Id          => "nexaflow-ai-host";
    public string DisplayName => Str.Get("Shell.Components.AiHost.Name");
    public string Description => Str.Get("Shell.Components.AiHost.Description");
    public ExternalDependencyKind Kind => ExternalDependencyKind.Required;

    /// <summary>No link: it ships with the app, so the fix is to repair the installation.</summary>
    public string? InstallUrl => null;

    public ExternalDependencyStatus Probe() => LocalAiRequirements.Host(AiHostInstall.ExecutablePath);
}
