using Nexaflow.IO.Common.Hardware;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>What a machine has for local AI: its adapters, read without anything failing.</summary>
[TestClass]
[CoversNode("sysinfo-local-ai")]
public class ComputeHardwareTests
{
    [TestMethod]
    public void ProbingThisMachine_NeverThrows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var report = ComputeHardware.Probe();
        Assert.IsNotNull(report.Adapters);
    }

    [TestMethod]
    public void AnNpu_IsToldApartFromTheGpus()
    {
        var report = new ComputeHardwareReport(
        [
            new ComputeAdapter("gpu", ComputeVendor.Nvidia, 16L << 30, IsIntegrated: false, IsNpu: false),
            new ComputeAdapter("npu", ComputeVendor.Intel, 0, IsIntegrated: true, IsNpu: true),
        ], null);

        Assert.AreEqual("npu", report.Npus.Single().Name);
    }
}
