using Nexaflow.Features.Processes.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Processes;

[TestClass]
[CoversNode("processes-cpu-sampling")]
public class CpuSamplingTests
{
    [TestMethod]
    public void FirstSample_NoElapsedTime_IsZero()
    {
        // nowTicks == prevTicks → no window → 0%, never a divide-by-zero.
        Assert.AreEqual(0, CpuMath.Percent(TimeSpan.Zero, TimeSpan.FromSeconds(1), 1000, 1000, 1000, 4));
    }

    [TestMethod]
    public void OneCoreFullyBusy_OnFourCores_IsTwentyFive()
    {
        // 1s of CPU over 1s of wall-clock on 4 cores = 25% of total capacity.
        var pct = CpuMath.Percent(TimeSpan.Zero, TimeSpan.FromSeconds(1),
                                  prevTicks: 0, nowTicks: 1000, frequency: 1000, cores: 4);
        Assert.AreEqual(25.0, pct, 0.001);
    }

    [TestMethod]
    public void OverBudget_ClampsTo100()
    {
        var pct = CpuMath.Percent(TimeSpan.Zero, TimeSpan.FromSeconds(10),
                                  prevTicks: 0, nowTicks: 1000, frequency: 1000, cores: 1);
        Assert.AreEqual(100.0, pct);
    }

    [TestMethod]
    public void DegenerateInputs_AreZero()
    {
        Assert.AreEqual(0, CpuMath.Percent(TimeSpan.Zero, TimeSpan.FromSeconds(1), 0, 1000, frequency: 0, cores: 4));
        Assert.AreEqual(0, CpuMath.Percent(TimeSpan.Zero, TimeSpan.FromSeconds(1), 0, 1000, frequency: 1000, cores: 0));
    }
}
