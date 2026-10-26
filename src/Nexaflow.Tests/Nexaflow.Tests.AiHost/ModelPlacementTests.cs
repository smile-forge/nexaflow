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

        var gemma = Placed(placement, "gemma");
        Assert.AreEqual((null, 32768, 6 * GB + 18432L * 32768), (gemma.Device, gemma.ContextTokens, gemma.MemoryBytes),
                        "too large for the card: the CPU, with all its context where no memory share is set");
        Assert.AreEqual(0, Placed(placement, "transcriber").Device);
        CollectionAssert.AreEqual(new[] { "encoder" }, placement.Unplaced.ToArray());
    }

    /// <summary>A part that runs on a GPU or the CPU, holding <paramref name="gpuGb"/> on one and <paramref name="cpuGb"/>
    /// on the other.</summary>
    private static PlacementNeed OnEither(string id, double gpuGb, double cpuGb) => new(Spec(id, (long)(gpuGb * GB)), Spec(id, (long)(cpuGb * GB)));

    [TestMethod]
    public void OnTheCpu_EachPartHoldsWhatItsBuildHolds_AndTheLanguageModelIsGivenWhatTheShareLeaves()
    {
        long share = 6 * GB + 2 * GB + 18432L * 16384;

        var placement = ModelPlacement.Plan([], [Gemma(), OnEither("transcriber", 3.75, 2)], share);

        Assert.AreEqual(2 * GB, Placed(placement, "transcriber").MemoryBytes, "its CPU build's footprint, not its GPU's");
        var gemma = Placed(placement, "gemma");
        Assert.AreEqual((16384, 6 * GB + 18432L * 16384), (gemma.ContextTokens, gemma.MemoryBytes), "what the share leaves beside the transcriber");
    }

    [TestMethod]
    public void OnTheCpu_ALanguageModelIsNeverGivenLessThanItsShortestContext()
    {
        var placement = ModelPlacement.Plan([], [Gemma()], memoryBudget: 1 * GB);

        Assert.AreEqual(8192, Placed(placement, "gemma").ContextTokens, "the share is a share of the machine, not a wall");
    }

    [TestMethod]
    public void APartBuiltForTheCpu_HoldsWhatItsBuildHolds()
    {
        var vad = new PlacementNeed(Spec("vad", 0), Spec("vad", 2L << 20));

        Assert.AreEqual(2L << 20, Placed(ModelPlacement.Plan([Gpu(0, 22)], [vad]), "vad").MemoryBytes);
        Assert.AreEqual(2L << 20, ModelPlacement.PlaceOnDemand(ModelPlacement.Plan([Gpu(0, 22)], []), vad)!.MemoryBytes);
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

    /// <summary>Gemma 4 26B A4B as the catalog has it: 18.8 GB with no context, 14.4 GB of it its 30 layers' weights; 13.6 KB a token
    /// of context held in 8 bits, 262k of it where there is room; and what reading in longer steps, and holding more sequences, cost it.</summary>
    private static PlacementNeed Large()
        => new(Spec("large", 18_790_481_920L, 262_144), Spec("large", 18_790_481_920L, 262_144), BytesPerToken: 13_600, ShortestContext: 8192,
               Layers: 30, WeightsBytes: 14_439_363_584L, BatchBytesPerToken: 1_376_256, BytesPerSequence: 113_639_424, BackgroundBytes: 9_428_795_392L);

    [TestMethod]
    public void ALanguageModelNoCardHasRoomFor_IsCutBetweenTheCardAndTheCpu_AsManyOfItsLayersOnTheCardAsFit()
    {
        var placement = ModelPlacement.Plan([Gpu(0, 14)], [Large()]);
        var large = Placed(placement, "large");

        Assert.AreEqual(0, large.Device, "it runs on the card");
        Assert.AreEqual(21, large.GpuLayers, "21 of its 30 layers there, the rest on the CPU");
        Assert.AreEqual(ModelPlacement.CutContext, large.ContextTokens, "its layers come before its length");
        Assert.AreEqual(Large().BytesAt(large.ContextTokens, 21), large.MemoryBytes, "what it holds on the card: its layers' share, and the rest whole");
        Assert.IsTrue(placement.FreeOn(placement.Gpus[0]) >= 0, "within the card's budget");
        Assert.IsTrue(Large().BytesAt(large.ContextTokens, 22) > placement.Gpus[0].BudgetBytes, "and one layer more would not be");
    }

    [TestMethod]
    public void ButWhereFewerThanHalfItsLayersFitOnTheCard_ItRunsOnTheCpu()
    {
        var large = Placed(ModelPlacement.Plan([Gpu(0, 9)], [Large()]), "large");

        Assert.AreEqual((null, null), (large.Device, large.GpuLayers));
    }

    [TestMethod]
    public void ACardWithRoomLeftOver_HasItsLanguageModelReadInLongerSteps_OnlyOnceItHasItsWholeContext()
    {
        var roomy = Placed(ModelPlacement.Plan([Gpu(0, 40)], [Large()]), "large");
        Assert.AreEqual((262_144, ModelPlacement.LongestBatch), (roomy.ContextTokens, roomy.BatchTokens));
    Assert.AreEqual(Large().BytesAt(262_144, batch: ModelPlacement.LongestBatch, sequences: roomy.Sequences), roomy.MemoryBytes,
                        "what the longer steps hold counted with the rest");

        var snug = Placed(ModelPlacement.Plan([Gpu(0, 22.5)], [Large()]), "large");
        Assert.AreEqual((262_144, ModelSpec.DefaultBatch), (snug.ContextTokens, snug.BatchTokens), "its whole context fits, and longer steps beside it do not");
    }

    [TestMethod]
    public void AContextHeldIn8Bits_TakesALittleOverHalfWhatItTakesIn16()
    {
        Assert.AreEqual(34, ContextCache.Q8.BytesPerToken(64), "32 values of 8 bits and a 16-bit scale, where 16 bits each take 64 bytes");
        Assert.AreEqual(64, ContextCache.F16.BytesPerToken(64));
    }

    [TestMethod]
    public void ACardWithRoomLeftOver_HasItsLanguageModelWriteMoreRepliesAtOnce_EachSequenceCounted()
    {
        var roomy = Placed(ModelPlacement.Plan([Gpu(0, 40)], [Large()]), "large");
        Assert.AreEqual(ModelPlacement.MostSequences, roomy.Sequences);
        Assert.AreEqual(Large().BytesAt(262_144, batch: ModelPlacement.LongestBatch, sequences: ModelPlacement.MostSequences), roomy.MemoryBytes,
                        "what each sequence past the catalog's holds counted with the rest");

        var snug = Placed(ModelPlacement.Plan([Gpu(0, 21.5)], [Large()]), "large");
        Assert.AreEqual((262_144, ModelSpec.DefaultSequences), (snug.ContextTokens, snug.Sequences),
                        "its whole context fits, 0.7 GB left beside it, too little for 8 more sequences: the ones its catalog was measured with");

        var cut = Placed(ModelPlacement.Plan([Gpu(0, 14)], [Large()]), "large");
        Assert.AreEqual(ModelSpec.DefaultSequences, cut.Sequences, "and a model cut between a card and the CPU has no room to spare");
    }

    [TestMethod]
    public void ACardWithRoomForIt_LetsItsLanguageModelMakeAContextForWorkNobodyIsWatching_WithoutCountingItAsHeld()
    {
        var roomy = Placed(ModelPlacement.Plan([Gpu(0, 40)], [Large()]), "large");
        Assert.AreEqual((ModelPlacement.BackgroundSequences, ModelPlacement.BackgroundTokens, Large().BackgroundBytes),
                        (roomy.BackgroundSequences, roomy.BackgroundTokens, roomy.BackgroundBytes));
        Assert.AreEqual(Large().BytesAt(262_144, batch: roomy.BatchTokens, sequences: roomy.Sequences), roomy.MemoryBytes,
                        "made only while there is such work, it is not what the model holds");

        var short_ = Placed(ModelPlacement.Plan([Gpu(0, 28)], [Large()]), "large");
        Assert.AreEqual(0, short_.BackgroundSequences, "what is left once the model is placed, 2 GB longer steps and 1 GB more sequences in, holds no 9 GB context");
    }
}
