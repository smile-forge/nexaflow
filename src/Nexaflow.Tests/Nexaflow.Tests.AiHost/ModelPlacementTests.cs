using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>Where the models the host keeps loaded run: each on a GPU it fits, a language model with the context its
/// GPU leaves it, and on the CPU or nowhere when no GPU has room.</summary>
[TestClass]
[CoversNode("local-ai-setup")]
public class ModelPlacementTests
{
    private const long GB = 1L << 30;

    private static HostGpu Gpu(int device, double budgetGb) => new(device, $"GPU {device}", null, (long)(budgetGb * GB) + (2 * GB), (long)(budgetGb * GB), (long)(budgetGb * GB) + HostGpu.ReservedBytes);

    private static ModelSpec Spec(string id, long bytes, int context = 0) => new(id, "runner", id, null, null, bytes, context);

    /// <summary>Gemma 4 E4B as the catalog has it: 6 GB with no context, 18 KB a token, 32k where there is room.</summary>
    private static PlacementNeed Gemma(bool onCpu = true)
        => new(Spec("gemma", 6 * GB, 32768), onCpu ? Spec("gemma", 6 * GB, 32768) : null, BytesPerToken: 18432, ShortestContext: 8192);

    private static PlacementNeed Part(string id, double gb, bool onCpu = false)
        => new(Spec(id, (long)(gb * GB)), onCpu ? Spec(id + "-cpu", 0) with { Id = id } : null);

    /// <summary>Speech in and out, as the catalog has it: a recogniser, a transcriber with a CPU build, two voices.</summary>
    private static PlacementNeed[] Speech() =>
    [
        Part("recognizer", 2.25), Part("transcriber", 3.75, onCpu: true), Part("voice-en", 1, onCpu: true), Part("voice-de", 1, onCpu: true),
        new(Spec("vad", 0)),
    ];

    private static ModelSpec Placed(Placement placement, string id) => placement.Of(id) ?? throw new AssertFailedException($"{id} was not placed");

    [TestMethod]
    public void OnALargeCard_EveryModelRunsOnIt_AndTheLanguageModelHasItsOwnContext()
    {
        var placement = ModelPlacement.Plan([Gpu(0, 22)], [Gemma(), .. Speech()]);

        Assert.AreEqual((0, 32768), (Placed(placement, "gemma").Device, Placed(placement, "gemma").ContextTokens));
        Assert.AreEqual(6 * GB + 18432L * 32768, Placed(placement, "gemma").MemoryBytes, "what it holds, its context included");
        Assert.IsTrue(Speech().Where(n => !n.CpuOnly).All(n => Placed(placement, n.Id).Device == 0));
        Assert.IsNull(Placed(placement, "vad").Device, "a part built for the CPU runs there whatever the machine");
        Assert.AreEqual(0, placement.Unplaced.Count);
    }

    [TestMethod]
    public void OnTwoSmallCards_TheModelsAreShared_SoThatAllOfThemFit()
    {
        var placement = ModelPlacement.Plan([Gpu(0, 9), Gpu(1, 9)], [Gemma(), .. Speech()]);

        Assert.IsTrue(placement.Placed.Where(s => s.Id != "vad").All(s => s.Device is not null), "nothing runs on the CPU");
        Assert.AreEqual(32768, Placed(placement, "gemma").ContextTokens);
        Assert.AreNotEqual(Placed(placement, "gemma").Device, Placed(placement, "transcriber").Device, "the two do not fit one card together");
        foreach (var gpu in placement.Gpus) Assert.IsTrue(placement.FreeOn(gpu) >= 0, $"GPU {gpu.Device} is within its budget");
    }

    [TestMethod]
    public void OnACardShortOfRoom_TheLanguageModelGivesUpContext_BeforeAnythingRunsOnTheCpu()
    {
        var placement = ModelPlacement.Plan([Gpu(0, 12.3)], [Gemma(), Part("recognizer", 2.25), Part("transcriber", 3.75, onCpu: true)]);

        Assert.AreEqual(0, Placed(placement, "transcriber").Device, "the transcriber stays on the GPU");
        int context = Placed(placement, "gemma").ContextTokens;
        Assert.IsTrue(context is >= 8192 and < 32768 && context % ModelPlacement.ContextStep == 0, $"a shorter context, in whole steps: {context}");
        Assert.IsTrue(placement.FreeOn(placement.Gpus[0]) >= 0);
    }

    [TestMethod]
    public void WhereNoGpuHasRoom_APartWithACpuBuildRunsOnTheCpu_AndOneWithoutIsLeftOut()
    {
        var placement = ModelPlacement.Plan([Gpu(0, 4)], [Gemma(), Part("transcriber", 3.75, onCpu: true), Part("encoder", 5)]);

        Assert.AreEqual((null, 0L), (Placed(placement, "gemma").Device, Placed(placement, "gemma").MemoryBytes), "too large for the card: the CPU");
        Assert.AreEqual(0, Placed(placement, "transcriber").Device);
        CollectionAssert.AreEqual(new[] { "encoder" }, placement.Unplaced.ToArray());
    }

    [TestMethod]
    public void OnAMachineWithNoGpu_EverythingWithACpuBuild_RunsOnTheCpu()
    {
        var placement = ModelPlacement.Plan([], [Gemma(), .. Speech(), Part("recognizer-gpu-only", 2)]);

        Assert.IsTrue(placement.Placed.All(s => s.Device is null));
        CollectionAssert.AreEquivalent(new[] { "recognizer", "recognizer-gpu-only" }, placement.Unplaced.ToArray());
    }

    [TestMethod]
    public void ThePlan_IsTheSame_WhicheverOrderTheModelsAreTakenOnIn()
    {
        PlacementNeed[] needs = [Gemma(), .. Speech()];
        var gpus = new[] { Gpu(0, 9), Gpu(1, 9) };

        var forwards = ModelPlacement.Plan(gpus, needs);
        var backwards = ModelPlacement.Plan(gpus, [.. needs.Reverse()]);

        foreach (var spec in forwards.Placed) Assert.IsTrue(spec.PlacedAs(Placed(backwards, spec.Id)), spec.Id);
    }

    [TestMethod]
    public void APicturesPart_GoesWhereItFitsBesideTheResidents_ElseWhereTheyMakeWay_AndNowhereTooSmall()
    {
        var residents = ModelPlacement.Plan([Gpu(0, 9), Gpu(1, 9)], [Gemma(), Part("transcriber", 3.75), Part("recognizer", 2.25)]);

        var small = ModelPlacement.PlaceOnDemand(residents, Part("decoder", 2))!;
        var large = ModelPlacement.PlaceOnDemand(residents, Part("denoiser", 8.5))!;

        Assert.IsFalse(residents.Displaces(small), "beside the residents");
        Assert.IsTrue(residents.Displaces(large), "residents make way for it");
        Assert.IsNull(ModelPlacement.PlaceOnDemand(residents, Part("huge", 10)), "no card could hold it");
    }

    [TestMethod]
    public void BeforeTheHostRuns_ItsGpusAreExpectedFromTheAdapters()
    {
        var hardware = new ComputeHardwareReport(
        [
            new ComputeAdapter("NVIDIA GeForce RTX 4080", ComputeVendor.Nvidia, 16 * GB, IsIntegrated: false, IsNpu: false),
            new ComputeAdapter("Intel UHD", ComputeVendor.Intel, 0, IsIntegrated: true, IsNpu: false),
        ], CudaRuntimePack.MinimumDriver);

        var gpu = ModelPlacement.Expected(hardware).Single();

        Assert.AreEqual(16 * GB - ModelPlacement.DesktopBytes - HostGpu.ReservedBytes, gpu.BudgetBytes);
        Assert.AreEqual(0, ModelPlacement.Expected(hardware with { CudaDriver = null }).Count, "without a CUDA driver the host runs on the CPU");
    }
}
