using System.IO;
using System.Windows.Media;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Services;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.Features.Common.Dependencies;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using NSubstitute;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// What Options → About → System components says about running Nexaflow's own models on the GPU, from the AI host's reading of
/// made-up machines:
/// one with no NVIDIA GPU, one whose driver is too old for the pack, one still waiting for the pack, and one with
/// everything — each row saying what it found, whether it will do, and why not.
/// </summary>
[TestClass]
[CoversNode("system-components")]
public class LocalAiRequirementsTests
{
    private static readonly ComputeAdapter Rtx3080 = new("NVIDIA GeForce RTX 3080", ComputeVendor.Nvidia, 10L << 30, IsIntegrated: false, IsNpu: false);
    private static readonly ComputeAdapter IntelUhd = new("Intel(R) UHD Graphics 770", ComputeVendor.Intel, 128L << 20, IsIntegrated: true, IsNpu: false);

    /// <summary>A machine as the rows read it: the NVIDIA cards its driver manages — each NVIDIA GPU of its own — beside every
    /// adapter Windows lists.</summary>
    private sealed record TestMachine(MachineCards Cards, ComputeHardwareReport Adapters);

    private static TestMachine Machine(Version? driver, params ComputeAdapter[] adapters)
    {
        var listed = new ComputeHardwareReport(adapters, driver);
        return new(new MachineCards([.. listed.NvidiaGpus.Select(a => new MachineCard(a.Name, a.DedicatedMemoryBytes))], driver), listed);
    }

    private static ExternalDependencyStatus Gpu(TestMachine machine) => LocalAiRequirements.Gpu(machine.Cards, machine.Adapters);

    private static ExternalDependencyStatus CudaDriver(TestMachine machine) => LocalAiRequirements.CudaDriver(machine.Cards);

    /// <summary>The pack row, where the host's store holds <paramref name="here"/> of its files and it runs on <paramref name="runsOn"/>.</summary>
    private static ExternalDependencyStatus CudaPack(TestMachine machine, int here, string? runsOn = null)
        => LocalAiRequirements.CudaPack(machine.Cards, runsOn, here);

    private static bool Applies(TestMachine machine) => CudaRuntimePack.Applies(machine.Cards);

    private static int PackFiles => CudaRuntimePack.Files.Count;

    /// <summary>The CUDA version the pack's libraries are built for, as the rows write it.</summary>
    private static string Needed => CudaRuntimePack.MinimumDriver.ToString(2);

    // ── A PC with no NVIDIA GPU ─────────────────────────────────────────────

    [TestMethod]
    public void NoNvidiaGpu_SaysWhatWasFound_AndThatTheModelsRunOnTheCpu()
    {
        var machine = Machine(null, IntelUhd);

        var gpu = Gpu(machine);
        Assert.AreEqual(ExternalDependencyState.Missing, gpu.State);
        StringAssert.Contains(gpu.Detail, IntelUhd.Name, "what was found is named, so the user can see why");
        StringAssert.Contains(gpu.Detail, Str.Format("Shell.Components.NvidiaGpu.IntegratedAdapterFormat", IntelUhd.Name, SizeFormatter.FormatBytes(IntelUhd.DedicatedMemoryBytes)));

        var driver = CudaDriver(machine);
        Assert.AreEqual(ExternalDependencyState.Missing, driver.State);
        Assert.AreEqual(Str.Get("Shell.Components.CudaDriver.NotNeeded"), driver.Detail);

        var pack = CudaPack(machine, 0);
        Assert.AreEqual(ExternalDependencyState.Missing, pack.State);
        Assert.AreEqual(Str.Format("Shell.Components.CudaPack.NotNeededFormat", Needed), pack.Detail,
            "the pack is not wanted here, so it must not read as something to fetch");
    }

    [TestMethod]
    public void NoAdapterAtAll_SaysWindowsReportsNone()
    {
        var gpu = Gpu(Machine(null));

        Assert.AreEqual(ExternalDependencyState.Missing, gpu.State);
        Assert.AreEqual(Str.Get("Shell.Components.NvidiaGpu.NoAdapters"), gpu.Detail);
    }

    [TestMethod]
    public void AnIntegratedNvidiaGpu_IsFoundButNotUsable()
    {
        var shared = Rtx3080 with { Name = "NVIDIA integrated", IsIntegrated = true };

        var gpu = Gpu(Machine(CudaRuntimePack.MinimumDriver, shared));

        Assert.AreEqual(ExternalDependencyState.Unusable, gpu.State);
        Assert.AreEqual(shared.Name, gpu.DetectedVersion);
        Assert.IsFalse(Applies(Machine(CudaRuntimePack.MinimumDriver, shared)), "the row agrees with the runtime's own rule");
    }

    // ── An NVIDIA GPU with a driver too old for CUDA 13 ─────────────────────

    [TestMethod]
    public void AnOldDriver_IsFoundButNotUsable_NamingItsVersionAndWhatIsNeeded()
    {
        var machine = Machine(new Version(12, 8), Rtx3080, IntelUhd);

        var gpu = Gpu(machine);
        Assert.AreEqual(ExternalDependencyState.Present, gpu.State);
        Assert.AreEqual(Str.Format("Shell.Components.NvidiaGpu.AdapterFormat", Rtx3080.Name, SizeFormatter.FormatBytes(Rtx3080.DedicatedMemoryBytes)), gpu.DetectedVersion);
        StringAssert.Contains(gpu.Detail, IntelUhd.Name, "every adapter found is listed");

        var driver = CudaDriver(machine);
        Assert.AreEqual(ExternalDependencyState.Unusable, driver.State);
        Assert.AreEqual("12.8", driver.DetectedVersion);
        Assert.AreEqual(Str.Format("Shell.Components.CudaDriver.TooOldFormat", "12.8", Needed), driver.Detail);

        var pack = CudaPack(machine, 0);
        Assert.AreEqual(Str.Format("Shell.Components.CudaPack.NotNeededFormat", Needed), pack.Detail,
            "with the driver too old the models run on the CPU, as the runtime decides");
    }

    [TestMethod]
    public void NoDriverAnswering_BesideAnNvidiaGpu_SaysToInstallIt()
    {
        var driver = CudaDriver(Machine(null, Rtx3080));

        Assert.AreEqual(ExternalDependencyState.Missing, driver.State);
        Assert.AreEqual(Str.Get("Shell.Components.CudaDriver.Absent"), driver.Detail);
    }

    // ── A machine the pack suits, still waiting for it ──────────────────────

    [TestMethod]
    public void ThePackMissing_WhereItIsNeeded_SaysHowMuchOfItIsHere()
    {
        var machine = Machine(CudaRuntimePack.MinimumDriver, Rtx3080);

        var none = CudaPack(machine, 0);
        Assert.AreEqual(ExternalDependencyState.Missing, none.State);
        Assert.AreEqual(Str.Format("Shell.Components.CudaPack.PartFormat", 0, PackFiles), none.Detail);

        var part = CudaPack(machine, 1);
        Assert.AreEqual(ExternalDependencyState.Missing, part.State, "a pack is usable only whole");
        Assert.AreEqual(Str.Format("Shell.Components.CudaPack.PartFormat", 1, PackFiles), part.Detail);
    }

    [TestMethod]
    public void ThePackWhole_ButNotYetRunOn_IsTakenUpAtTheHostsNextStart()
    {
        var pack = CudaPack(Machine(CudaRuntimePack.MinimumDriver, Rtx3080), PackFiles);

        Assert.AreEqual(ExternalDependencyState.Present, pack.State, "downloaded whole, it is here");
        Assert.AreEqual(Str.Format("Shell.Components.CudaPack.NextStartFormat", CudaRuntimePack.Id), pack.Detail,
            "the host loads its runtime once, as it starts: one fetched since runs from its next start");
    }

    [TestMethod]
    public void ARow_WithNoHostToAsk_IsUnknown_SayingWhy()
    {
        const string why = "The AI host service is not running.";

        var row = LocalAiRequirements.Judged(HostRuntimeAnswer.Unanswered(why), host => LocalAiRequirements.CudaDriver(host.Machine));

        Assert.AreEqual(ExternalDependencyState.Unknown, row.State, "unknown is not missing: nothing was learned of where the models run");
        Assert.AreEqual(why, row.Detail);
    }

    [TestMethod]
    public void TheHostsReading_IsTheMachineItDescribed_AndThePackItRunsOn()
    {
        var described = new HostDescription(1, "1.31.1", ["CUDAExecutionProvider"], CudaRuntimePack.Id, [],
                                            Machine: new HostMachine([new HostCard(Rtx3080.Name, Rtx3080.DedicatedMemoryBytes, "00000000:01:00.0")], "13.1", PackFiles));

        var reading = HostRuntimeReading.Of(described)!;

        Assert.AreEqual(Rtx3080.Name, reading.Machine.Cards.Single().Name);
        Assert.AreEqual(new Version(13, 1), reading.Machine.CudaDriver);
        Assert.AreEqual((CudaRuntimePack.Id, PackFiles), (reading.RuntimePack, reading.PackFilesHere));
        Assert.IsNull(HostRuntimeReading.Of(described with { Machine = null }), "a host that described no machine gives no reading to judge");
    }

    // ── Everything in place ─────────────────────────────────────────────────

    [TestMethod]
    public void AllInPlace_EveryRowIsPresent_NamingWhatWasFound()
    {
        var machine = Machine(new Version(13, 1), Rtx3080);

        Assert.AreEqual(ExternalDependencyState.Present, Gpu(machine).State);

        var driver = CudaDriver(machine);
        Assert.AreEqual(ExternalDependencyState.Present, driver.State);
        Assert.AreEqual("13.1", driver.DetectedVersion);

        var pack = CudaPack(machine, PackFiles, runsOn: CudaRuntimePack.Id);
        Assert.AreEqual(ExternalDependencyState.Present, pack.State);
        Assert.AreEqual(CudaRuntimePack.Id, pack.DetectedVersion);
        Assert.AreEqual(CudaRuntimePack.Id, pack.Detail, "the pack the host runs on: where it keeps it is the host's own");
    }

    [TestMethod]
    public void ThePackHere_OnAMachineItDoesNotSuit_IsSaidToGoUnused()
    {
        var pack = CudaPack(Machine(new Version(12, 8), Rtx3080), PackFiles);

        Assert.AreEqual(ExternalDependencyState.Present, pack.State);
        Assert.AreEqual(Str.Format("Shell.Components.CudaPack.UnusedFormat", CudaRuntimePack.Id, Needed), pack.Detail);
    }

    [TestMethod]
    public void TheHost_IsFoundWhereItIsInstalled_OrNamedWhereItWasLookedFor()
    {
        var folder = TestTemp.CreateSubdirectory("nexaflow-host-check-").FullName;
        try
        {
            var exe = Path.Combine(folder, "Nexaflow.AiHost.exe");
            var missing = LocalAiRequirements.Host(exe);
            Assert.AreEqual(ExternalDependencyState.Missing, missing.State);
            Assert.AreEqual(Str.Format("Shell.Components.AiHost.MissingFormat", exe), missing.Detail);

            File.WriteAllText(exe, "");
            Assert.AreEqual(ExternalDependencyState.Present, LocalAiRequirements.Host(exe).State);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    // ── On the About page ───────────────────────────────────────────────────

    private static readonly Type[] Declarations =
        [typeof(NvidiaGpuDependency), typeof(CudaDriverDependency), typeof(CudaRuntimePackDependency), typeof(AiHostDependency)];

    [TestMethod]
    public void TheRequirements_AreListedOnTheAboutPage_EachAnsweringRatherThanThrowing()
    {
        var catalog = new FeatureCatalog();
        catalog.Initialize((_, _) => { });
        var discovered = catalog.TypesImplementing<IExternalDependency>();
        foreach (var type in Declarations)
            CollectionAssert.Contains(discovered.ToList(), type, $"{type.Name} must be found where About looks");

        // Built as the session builds them, with its reading of the AI host — here a session that runs none.
        var reports = ExternalDependencyRegistry.BuildReports(Declarations, [new AiHostReading(holds: null)]);
        Assert.AreEqual(Declarations.Length, reports.Count, "each requirement is a row of its own");
        foreach (var report in reports)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(report.DisplayName), report.Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(report.Description), report.Id);
            Assert.IsFalse((report.Status.Detail ?? "").StartsWith(Str.Format("Shell.Components.CheckFailedFormat", ""), StringComparison.Ordinal),
                $"{report.Id}'s check threw: {report.Status.Detail}");
        }

        // Without the host none of the models runs; without the GPU path they run on the CPU.
        Assert.AreEqual(ExternalDependencyKind.Required, reports.Single(r => r.Id == new AiHostDependency().Id).Kind);
        Assert.IsTrue(reports.Where(r => r.Id != new AiHostDependency().Id).All(r => r.Kind == ExternalDependencyKind.Optional));
    }

    [TestMethod]
    public void AnUnusableRow_ReadsAsFoundButNotUsable_InTheWarningColourOfAnOptionalComponent()
    {
        var warning = new SolidColorBrush(Colors.Orange);
        var report = new ExternalDependencyReport("nvidia-cuda-driver", "NVIDIA CUDA driver", "", ExternalDependencyKind.Optional, null,
                                                  CudaDriver(Machine(new Version(12, 8), Rtx3080)), ["Core"]);

        var row = new ComponentRow(report, key => key == "WarningBrush" ? warning : null);

        Assert.AreEqual(Str.Format("Shell.Components.UnusableFormat", "12.8"), row.StatusLabel);
        Assert.AreSame(warning, row.Brush);
        Assert.IsFalse(report.IsBlocking, "an optional component is never the page's alarm");
        Assert.IsTrue((report with { Kind = ExternalDependencyKind.Required }).IsBlocking, "a required one found unusable is");
    }
}
