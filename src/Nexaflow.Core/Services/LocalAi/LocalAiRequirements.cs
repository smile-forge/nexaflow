using System.IO;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.Features.Common.Dependencies;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;
using Nexaflow.Localization.Formatting;
using Nexaflow.Localization;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>
/// What Nexaflow's own models need before one runs on a GPU, each judged as Options → About lists it: what was found,
/// whether it will do, and why not. Each is judged from what the AI host itself found when it set its runtime up — the machine
/// it read, the pack it runs on (<see cref="AiHostReading"/>) — by the rule it decided with (<see cref="CudaRuntimePack.Applies"/>),
/// so the page says where the models run rather than a guess beside it.
/// <para>
/// The judgements take the host's reading as arguments, so they are tested on made-up machines; the declarations in
/// <c>LocalAiDependencies.cs</c> hand them the host's answer, asked through the session's <see cref="AiHostReading"/>.
/// </para>
/// </summary>
internal static class LocalAiRequirements
{
    /// <summary><paramref name="judge"/>'s judgement of the host's reading, or unknown — with why — where no host could be asked: a
    /// guess beside the host's own word could only disagree with where the models run.</summary>
    public static ExternalDependencyStatus Judged(HostRuntimeAnswer answer, Func<HostRuntimeReading, ExternalDependencyStatus> judge)
        => answer.Reading is { } reading ? judge(reading) : ExternalDependencyStatus.Unknown(answer.Why);

    /// <summary>Every adapter Windows lists for AI work: what was found, said where no NVIDIA card will do.</summary>
    public static ComputeHardwareReport Adapters => ComputeHardware.Probe();

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
    /// The CUDA pack: the one the host runs on; else how much of it the host's store holds — whole and waiting for the host's next start,
    /// or partly — where this machine runs on it at all, since the models wait for it where it does, and never use it where it does not.
    /// </summary>
    /// <param name="runtimePack">The pack the host runs on, or null where it runs on the CPU.</param>
    /// <param name="packFilesHere">How many of the pack's files the host's store holds, each verified.</param>
    public static ExternalDependencyStatus CudaPack(MachineCards cards, string? runtimePack, int packFilesHere)
    {
        if (runtimePack is not null) return new(ExternalDependencyState.Present, runtimePack, runtimePack);

        var needed = CudaRuntimePack.MinimumDriver.ToString(2);
        bool whole = packFilesHere >= CudaRuntimePack.Files.Count;
        if (!CudaRuntimePack.Applies(cards))
            return whole ? new(ExternalDependencyState.Present, CudaRuntimePack.Id, Str.Format("Shell.Components.CudaPack.UnusedFormat", CudaRuntimePack.Id, needed))
                         : new(ExternalDependencyState.Missing, null, Str.Format("Shell.Components.CudaPack.NotNeededFormat", needed));

        return whole ? new(ExternalDependencyState.Present, CudaRuntimePack.Id, Str.Format("Shell.Components.CudaPack.NextStartFormat", CudaRuntimePack.Id))
                     : new(ExternalDependencyState.Missing, null, Str.Format("Shell.Components.CudaPack.PartFormat", packFilesHere, CudaRuntimePack.Files.Count));
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
