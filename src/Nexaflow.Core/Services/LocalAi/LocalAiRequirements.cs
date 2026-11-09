using System.IO;
using Nexaflow.AiHost.Client;
using Nexaflow.Features.Common.Dependencies;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>
/// What Nexaflow's own models need before one runs on a GPU, each judged as Options → About lists it: what was found,
/// whether it will do, and why not. The rule each judgement applies is the one the models' runtime decides by —
/// <see cref="CudaRuntimePack.Applies"/> for CUDA, <see cref="CudaRuntimePack.Locate"/> for the pack — so the page says
/// what the app does rather than a guess beside it.
/// <para>
/// The judgements take the machine as arguments, so they are tested on made-up machines; the declarations in
/// <c>LocalAiDependencies.cs</c> hand them the machine the app itself decided by.
/// </para>
/// </summary>
internal static class LocalAiRequirements
{
    /// <summary>
    /// The hardware the models' runtime decided by — the one probe the app made at startup, so a driver updated since
    /// shows once the app is restarted, which is when the models take it up — or a probe now where the app has not set
    /// its own models up.
    /// </summary>
    public static ComputeHardwareReport Hardware
        => ProviderManager.Instance.Service<ComputeHardwareReport>() ?? ComputeHardware.Probe();

    /// <summary>The store the models and the CUDA pack are downloaded into; null where the app has not set it up.</summary>
    public static IModelAssetStore? Store => ProviderManager.Instance.Assets;

    /// <summary>An NVIDIA GPU with memory of its own, each named with its memory, beside every other adapter found.</summary>
    public static ExternalDependencyStatus Gpu(ComputeHardwareReport hardware)
    {
        var found = Found(hardware);
        if (hardware.NvidiaGpus.Any())
            return new(ExternalDependencyState.Present,
                       string.Join(", ", hardware.NvidiaGpus.Select(g => Str.Format("Shell.Components.NvidiaGpu.AdapterFormat", g.Name, Size(g)))),
                       Str.Format("Shell.Components.NvidiaGpu.FoundFormat", found));

        if (hardware.Adapters.FirstOrDefault(a => a.Vendor == ComputeVendor.Nvidia && !a.IsNpu) is { } shared)
            return new(ExternalDependencyState.Unusable, shared.Name, Str.Format("Shell.Components.NvidiaGpu.IntegratedFormat", found));

        return new(ExternalDependencyState.Missing, null,
                   hardware.Adapters.Count == 0 ? Str.Get("Shell.Components.NvidiaGpu.NoAdapters")
                                                : Str.Format("Shell.Components.NvidiaGpu.NoneFormat", found));
    }

    /// <summary>The CUDA version NVIDIA's driver supports, against the one the pack's libraries are built for.</summary>
    public static ExternalDependencyStatus CudaDriver(ComputeHardwareReport hardware)
    {
        var needed = CudaRuntimePack.MinimumDriver;
        if (hardware.CudaDriver is not { } driver)
            return new(ExternalDependencyState.Missing, null,
                       hardware.NvidiaGpus.Any() ? Str.Get("Shell.Components.CudaDriver.Absent")
                                                 : Str.Get("Shell.Components.CudaDriver.NotNeeded"));

        return driver >= needed
            ? new(ExternalDependencyState.Present, driver.ToString(2))
            : new(ExternalDependencyState.Unusable, driver.ToString(2),
                  Str.Format("Shell.Components.CudaDriver.TooOldFormat", driver.ToString(2), needed.ToString(2)));
    }

    /// <summary>
    /// The CUDA pack: here and verified, or how much of it is — and whether this machine runs on it at all, since the
    /// models wait for it where it does, and never use it where it does not.
    /// </summary>
    public static ExternalDependencyStatus CudaPack(ComputeHardwareReport hardware, IModelAssetStore? store)
    {
        if (store is null) return ExternalDependencyStatus.Unknown(Str.Get("Shell.Components.CudaPack.NotSetUp"));

        var needed = CudaRuntimePack.MinimumDriver.ToString(2);
        bool applies = CudaRuntimePack.Applies(hardware);
        if (CudaRuntimePack.Locate(store) is { } folder)
            return new(ExternalDependencyState.Present, CudaRuntimePack.Id,
                       applies ? folder : Str.Format("Shell.Components.CudaPack.UnusedFormat", folder, needed));

        if (!applies) return new(ExternalDependencyState.Missing, null, Str.Format("Shell.Components.CudaPack.NotNeededFormat", needed));

        int here = CudaRuntimePack.Files.Count(f => store.Locate(f) is not null);
        return new(ExternalDependencyState.Missing, null, Str.Format("Shell.Components.CudaPack.PartFormat", here, CudaRuntimePack.Files.Count));
    }

    /// <summary>The AI host's executable, where the app starts it from.</summary>
    [LocalDiskOnly("the AI host's executable, installed beside the app")]
    public static ExternalDependencyStatus Host(string executable)
        => File.Exists(executable)
            ? new(ExternalDependencyState.Present, null, executable)
            : new(ExternalDependencyState.Missing, null, Str.Format("Shell.Components.AiHost.MissingFormat", executable));

    /// <summary>Every adapter that can run AI work, as a person reads it: its name, and its memory or what it is instead.</summary>
    private static string Found(ComputeHardwareReport hardware)
        => string.Join("; ", hardware.Adapters.Select(a =>
               a.IsNpu        ? Str.Format("Shell.Components.NvidiaGpu.NpuFormat", a.Name)
             : a.IsIntegrated ? Str.Format("Shell.Components.NvidiaGpu.IntegratedAdapterFormat", a.Name, Size(a))
                              : Str.Format("Shell.Components.NvidiaGpu.AdapterFormat", a.Name, Size(a))));

    private static string Size(ComputeAdapter adapter) => SizeFormatter.FormatBytes(adapter.DedicatedMemoryBytes);
}
