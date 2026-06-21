using System.Collections.Generic;
using System.Linq;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local.Catalog;

namespace Nexaflow.Tests.Providers.Unit;

[TestClass]
public class LocalModelCatalogTests
{
    private static List<LocalModelVariant> Variants() =>
    [
        new() { Id = "small", Family = "gemma", Files = ["a.gguf"], ApproxVramMb = 3000 },
        new() { Id = "mid",   Family = "gemma", Files = ["a.gguf"], ApproxVramMb = 9000 },
        new() { Id = "big",   Family = "qwen",  Files = ["a.gguf"], ApproxVramMb = 20000 },
        new() { Id = "huge",  Family = "qwen",  Files = ["a.gguf"], ApproxVramMb = 75000 },
    ];

    private static HostCapabilities Caps(bool cuda, int vramMb, int ramMb) =>
        new(Avx: true, Avx2: true, Fma: true, F16C: true,
            CudaAvailable: cuda, CudaMajorVersion: cuda ? 12 : 0,
            GpuName: cuda ? "Test GPU" : null, GpuComputeCapability: cuda ? 8.0 : 0.0,
            GpuVramMb: vramMb, TotalRamMb: ramMb);

    private static string[] Ids(HostCapabilities? caps) =>
        LocalModelCatalog.FittingHost(Variants(), caps).Select(v => v.Id).ToArray();

    [TestMethod]
    public void NullCaps_OffersOnlySmallFallback()
        => CollectionAssert.AreEquivalent(new[] { "small" }, Ids(null));

    [TestMethod]
    public void Cuda_GatesByVram()
        // 0.85 * 24000 = 20400 → small/mid/big fit, huge doesn't.
        => CollectionAssert.AreEquivalent(new[] { "small", "mid", "big" }, Ids(Caps(cuda: true, vramMb: 24000, ramMb: 64000)));

    [TestMethod]
    public void SmallGpu_OffersOnlySmall()
        // 0.85 * 6000 = 5100 → only small.
        => CollectionAssert.AreEquivalent(new[] { "small" }, Ids(Caps(cuda: true, vramMb: 6000, ramMb: 32000)));

    [TestMethod]
    public void CpuOnly_GatesByRam()
        // No CUDA → budget = RAM. 0.85 * 16000 = 13600 → small/mid fit, big doesn't.
        => CollectionAssert.AreEquivalent(new[] { "small", "mid" }, Ids(Caps(cuda: false, vramMb: 0, ramMb: 16000)));

    [TestMethod]
    public void CudaPresentButTinyVram_FallsBackToRam()
        // VRAM below the 2048 MB floor → treated as CPU; gate by RAM (0.85 * 16000 = 13600).
        => CollectionAssert.AreEquivalent(new[] { "small", "mid" }, Ids(Caps(cuda: true, vramMb: 1000, ramMb: 16000)));

    [TestMethod]
    public void Find_IsCaseInsensitive()
    {
        var v = LocalModelCatalog.Find(Variants(), "BIG");
        Assert.IsNotNull(v);
        Assert.AreEqual("big", v!.Id);
    }
}
