using System.IO;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Provisioning;
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
    /// The machine the models' runtime decided by — the one reading the app made at startup, so a driver updated since shows
    /// once the app is restarted, which is when the models take it up — or a reading now where the app has not set its own
    /// models up.
    /// </summary>
    public static MachineCards Cards => ProviderManager.Instance.Service<MachineCards>() ?? MachineCards.Probe();

    /// <summary>Every adapter Windows lists for AI work: what was found, said where no NVIDIA card will do.</summary>
    public static ComputeHardwareReport Adapters => ComputeHardware.Probe();

    /// <summary>The store the models and the CUDA pack are downloaded into; null where the app has not set it up.</summary>
    public static IModelAssetStore? Store => ProviderManager.Instance.Assets;

    /// <summary>Each NVIDIA card the driver manages, named with its memory, beside every adapter Windows lists; where there is
    /// none, what was found instead.</summary>
    public static ExternalDependencyStatus Gpu(MachineCards cards, ComputeHardwareReport adapters)
    {
        var found = Found(adapters);
        if (cards.Cards.Count > 0)
            return new(ExternalDependencyState.Present,
                       string.Join(", ", cards.Cards.Select(c => Str.Format("Shell.Components.NvidiaGpu.AdapterFormat", c.Name, SizeFormatter.FormatBytes(c.TotalBytes)))),
                       Str.Format("Shell.Components.NvidiaGpu.FoundFormat", found));

        if (adapters.Adapters.FirstOrDefault(a => a.Vendor == ComputeVendor.Nvidia && !a.IsNpu) is { } shared)
            return new(ExternalDependencyState.Unusable, shared.Name, Str.Format("Shell.Components.NvidiaGpu.IntegratedFormat", found));

        return new(ExternalDependencyState.Missing, null,
                   adapters.Adapters.Count == 0 ? Str.Get("Shell.Components.NvidiaGpu.NoAdapters")
                                                : Str.Format("Shell.Components.NvidiaGpu.NoneFormat", found));
    }

    /// <summary>The CUDA version NVIDIA's driver supports, against the one the pack's libraries are built for.</summary>
    public static ExternalDependencyStatus CudaDriver(MachineCards cards)
    {
        var needed = CudaRuntimePack.MinimumDriver;
        if (cards.CudaDriver is not { } driver)
            return new(ExternalDependencyState.Missing, null,
                       cards.Cards.Count > 0 ? Str.Get("Shell.Components.CudaDriver.Absent")
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
    public static ExternalDependencyStatus CudaPack(MachineCards cards, IModelAssetStore? store)
    {
        if (store is null) return ExternalDependencyStatus.Unknown(Str.Get("Shell.Components.CudaPack.NotSetUp"));

        var needed = CudaRuntimePack.MinimumDriver.ToString(2);
        bool applies = CudaRuntimePack.Applies(cards);
        if (CudaRuntimePack.IsIn(store))
            return new(ExternalDependencyState.Present, CudaRuntimePack.Id,
                       applies ? CudaRuntimePack.Id : Str.Format("Shell.Components.CudaPack.UnusedFormat", CudaRuntimePack.Id, needed));

        if (!applies) return new(ExternalDependencyState.Missing, null, Str.Format("Shell.Components.CudaPack.NotNeededFormat", needed));

        int here = CudaRuntimePack.Files.Count(store.Has);
        return new(ExternalDependencyState.Missing, null, Str.Format("Shell.Components.CudaPack.PartFormat", here, CudaRuntimePack.Files.Count));
    }

    /// <summary>The AI host's executable, where the app starts it from.</summary>
    [LocalDiskOnly("the AI host's executable, installed beside the app")]
    public static ExternalDependencyStatus Host(string executable)
        => File.Exists(executable)
            ? new(ExternalDependencyState.Present, null, executable)
            : new(ExternalDependencyState.Missing, null, Str.Format("Shell.Components.AiHost.MissingFormat", executable));

    /// <summary>Every adapter that can run AI work, as a person reads it: its name, and its memory or what it is instead.</summary>
    private static string Found(ComputeHardwareReport adapters)
        => string.Join("; ", adapters.Adapters.Select(a =>
               a.IsNpu        ? Str.Format("Shell.Components.NvidiaGpu.NpuFormat", a.Name)
             : a.IsIntegrated ? Str.Format("Shell.Components.NvidiaGpu.IntegratedAdapterFormat", a.Name, Size(a))
                              : Str.Format("Shell.Components.NvidiaGpu.AdapterFormat", a.Name, Size(a))));

    private static string Size(ComputeAdapter adapter) => SizeFormatter.FormatBytes(adapter.DedicatedMemoryBytes);
}
