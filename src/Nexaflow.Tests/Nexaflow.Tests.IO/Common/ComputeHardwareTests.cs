using Nexaflow.IO.Common.Hardware;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>Which local models a machine's hardware suits, by its best GPU's own memory.</summary>
[TestClass]
[CoversNode("sysinfo-local-ai")]
public class ComputeHardwareTests
{
    private static ComputeHardwareReport With(params ComputeAdapter[] adapters) => new(adapters, null);

    private static ComputeAdapter Gpu(long gib, bool integrated = false)
        => new("gpu", ComputeVendor.Nvidia, gib << 30, integrated, IsNpu: false);

    [TestMethod]
    public void TheFit_FollowsTheBestGpusOwnMemory()
    {
        Assert.AreEqual(LocalAiFit.Insufficient, With(Gpu(16)).Fit);
        Assert.AreEqual(LocalAiFit.DefaultModel, With(Gpu(24)).Fit);
        Assert.AreEqual(LocalAiFit.LargerModels, With(Gpu(32)).Fit);
        Assert.AreEqual(LocalAiFit.LargerModels, With(Gpu(8), Gpu(96)).Fit);
    }

    [TestMethod]
    public void ACardSoldAs24GB_WhoseDriverKeepsALittle_StillFits()
        => Assert.AreEqual(LocalAiFit.DefaultModel, With(new ComputeAdapter("gpu", ComputeVendor.Amd, (24L << 30) - (300L << 20), false, false)).Fit);

    [TestMethod]
    public void AnIntegratedGpu_OrAnNpu_IsNeverEnough()
    {
        Assert.AreEqual(LocalAiFit.Insufficient, With(Gpu(64, integrated: true)).Fit, "its memory is the system's");
        Assert.AreEqual(LocalAiFit.Insufficient,
            With(new ComputeAdapter("npu", ComputeVendor.Intel, 64L << 30, true, IsNpu: true)).Fit);
    }

    [TestMethod]
    public void ProbingThisMachine_NeverThrows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var report = ComputeHardware.Probe();
        Assert.IsNotNull(report.Adapters);
    }
}
