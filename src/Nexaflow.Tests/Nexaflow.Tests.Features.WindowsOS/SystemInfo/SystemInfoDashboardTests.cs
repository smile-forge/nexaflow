using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.Converters;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.Services;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.SystemInfo;

/// <summary>
/// The dashboard's six cards and the health colouring behind their values, asserted against a real
/// <see cref="SystemInfoCollector"/> run on this machine. The collector's contract is that every probe is
/// independently fault-tolerant — a WMI class that isn't there, a registry key that can't be read — so the
/// card must still appear with its facts degraded to "—" rather than the whole snapshot failing. That
/// resilience is exactly what a live run pins down; the specific values are machine-dependent and are not
/// asserted.
/// </summary>
[TestClass]
public class SystemInfoDashboardTests
{
    private static readonly SystemInfoSnapshot Snapshot = new SystemInfoCollector().Collect();

    private static SystemInfoSection Card(string title)
        => Snapshot.Sections.SingleOrDefault(s => s.Title == title)
           ?? throw new AssertFailedException($"The dashboard is missing its '{title}' card.");

    [TestMethod]
    [CoversNode("operating-system")]
    public void OperatingSystemCard_NamesTheMachineAndItsWindowsBuild()
    {
        var card = Card("Operating System");

        Assert.IsTrue(card.Items.Count > 0);
        CollectionAssert.IsSubsetOf(new[] { "Name", "Version", "Architecture", "Computer Name" },
                                    card.Items.Select(i => i.Label).ToList());
    }

    [TestMethod]
    [CoversNode("hardware")]
    public void HardwareCard_IsPresentAndPopulated()
        => Assert.IsTrue(Card("Hardware").Items.Count > 0);

    [TestMethod]
    [CoversNode("display")]
    public void DisplayCard_IsPresentAndPopulated()
        => Assert.IsTrue(Card("Display").Items.Count > 0);

    [TestMethod]
    [CoversNode("sysinfo-local-ai")]
    public void LocalAiCard_SaysWhenTheWorkspaceRunsNoModelOfNexaflowsOwn()
    {
        var first = Card("Local AI").Items[0];

        Assert.AreEqual(("Nexaflow's own models", "Not used in this workspace"), (first.Label, first.Value));
    }

    [TestMethod]
    [CoversNode("sysinfo-local-ai")]
    public void BeforeTheHostRuns_LocalAiCard_NamesEachAdapterAndNpu_AndTheCudaDriver()
    {
        var report = new ComputeHardwareReport(
        [
            new ComputeAdapter("AMD Radeon(TM) Graphics", ComputeVendor.Amd, 2L << 30, IsIntegrated: true, IsNpu: false),
            new ComputeAdapter("NVIDIA RTX PRO 6000", ComputeVendor.Nvidia, 96L << 30, IsIntegrated: false, IsNpu: false),
            new ComputeAdapter("Intel AI Boost", ComputeVendor.Intel, 0, IsIntegrated: true, IsNpu: true),
        ], new Version(13, 0));

        var rows = SystemInfoCollector.CollectLocalAi(report, null).Items.ToDictionary(i => i.Label, i => i);

        StringAssert.StartsWith(rows["NVIDIA RTX PRO 6000"].Value, "NVIDIA GPU");
        StringAssert.StartsWith(rows["AMD Radeon(TM) Graphics"].Value, "AMD integrated GPU");
        Assert.AreEqual("Intel AI Boost", rows["NPU"].Value);
        Assert.AreEqual("13.0", rows["CUDA driver"].Value);
    }

    [TestMethod]
    [CoversNode("sysinfo-local-ai")]
    public void LocalAiCard_SaysWhereEachModelRuns_WhatElseFits_AndEachGpusBudget()
    {
        var setup = new LocalAiSetup([new LocalAiGpu(0, "NVIDIA GeForce RTX 2080 Ti", 11L << 30, 9L << 30)],
        [
            new LocalAiModel("Gemma 4 E4B", 0, 6L << 30, 16384, OnDemand: false, Displaces: false, Fits: true),
            new LocalAiModel("Whisper — transcriber", null, 0, 0, OnDemand: false, Displaces: false, Fits: true),
            new LocalAiModel("Z-Image Turbo — denoiser", 0, 9L << 30, 0, OnDemand: true, Displaces: true, Fits: true),
            new LocalAiModel("Gemma 4 31B", null, 0, 0, OnDemand: false, Displaces: false, Fits: false),
        ], ["Qwen 3.5 9B"]);
        var report = new ComputeHardwareReport([new ComputeAdapter("NVIDIA GeForce RTX 2080 Ti", ComputeVendor.Nvidia, 11L << 30, false, false)], new Version(13, 0));

        var rows = SystemInfoCollector.CollectLocalAi(report, setup).Items.ToDictionary(i => i.Label, i => i);

        Assert.AreEqual(("GPU 0 · 6.0 GB · a context of 16,384 tokens", SystemInfoStatus.Good), (rows["Gemma 4 E4B"].Value, rows["Gemma 4 E4B"].Status));
        Assert.AreEqual(("The CPU", SystemInfoStatus.Warning), (rows["Whisper — transcriber"].Value, rows["Whisper — transcriber"].Status),
                        "a GPU here, but no room on it: slower");
        StringAssert.EndsWith(rows["Z-Image Turbo — denoiser"].Value, "make way while it works");
        Assert.AreEqual(("Too large for this machine", SystemInfoStatus.Bad), (rows["Gemma 4 31B"].Value, rows["Gemma 4 31B"].Status));
        Assert.AreEqual("Qwen 3.5 9B", rows["Also fits this machine"].Value);
        StringAssert.EndsWith(rows["GPU 0"].Value, "9.0 GB for models", "the host's GPUs, each with its budget, in place of the adapters");
    }

    [TestMethod]
    [CoversNode("storage")]
    public void StorageCard_IsPresentAndPopulated()
        => Assert.IsTrue(Card("Storage").Items.Count > 0);

    [TestMethod]
    [CoversNode("windows-security")]
    public void SecurityCard_ReportsEachProtectionWithAVerdict_NotJustText()
    {
        var card = Card("Windows Security");

        Assert.IsTrue(card.Items.Count > 0);
        Assert.IsTrue(card.Items.Any(i => i.Status != SystemInfoStatus.Neutral),
                      "the security card exists to flag protections that are off — a purely neutral card is a bug");
    }

    [TestMethod]
    [CoversNode("sysinfoview")]
    public void EveryCard_FillsAMissingFactWithADash_RatherThanBlank()
    {
        Assert.AreEqual(6, Snapshot.Sections.Count, "the dashboard is six cards");
        Assert.IsFalse(Snapshot.Sections.SelectMany(s => s.Items).Any(i => string.IsNullOrWhiteSpace(i.Value)),
                       "a probe that couldn't read its value must render '—', never an empty cell");
        StringAssert.Contains(Snapshot.ToPlainText(), "Operating System",
                              "the plain-text rendering feeds the AI context and copy-to-clipboard");
    }

    // ── Health colour coding ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("sysinfo-statuscolour")]
    public void EveryStatus_ResolvesToASemanticThemeToken()
    {
        Assert.AreEqual("SuccessBrush", StatusToBrushConverter.ResourceKey(SystemInfoStatus.Good));
        Assert.AreEqual("WarningBrush", StatusToBrushConverter.ResourceKey(SystemInfoStatus.Warning));
        Assert.AreEqual("DangerBrush", StatusToBrushConverter.ResourceKey(SystemInfoStatus.Bad));
        Assert.AreEqual("TextBrush", StatusToBrushConverter.ResourceKey(SystemInfoStatus.Neutral));
    }

    [TestMethod]
    [CoversNode("sysinfo-statuscolour")]
    public void AnUnrecognisedStatus_PaintsAsPlainText_NotAVerdict()
        => Assert.AreEqual("TextBrush", StatusToBrushConverter.ResourceKey(null));

    [TestMethod]
    [CoversNode("sysinfo-statuscolour")]
    public void Convert_AlwaysReturnsABrush_EvenWithNoApplicationResources()
        => Assert.IsNotNull(StatusToBrushConverter.Instance.Convert(
               SystemInfoStatus.Good, typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture));

    // ── WMI probe layer ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("sysinfo-wmi")]
    public void Wmi_ReadsAKnownClass_AndDegradesToNullOnAnUnknownOne()
    {
        Assert.IsNotNull(Wmi.First("Win32_OperatingSystem")?.Str("Caption"),
                         "Win32_OperatingSystem is present on every Windows install");

        Assert.IsNull(Wmi.First("Win32_NoSuchClassAnywhere"),
                      "a missing class must degrade to null so one bad probe can't fail the whole card");
    }
}
