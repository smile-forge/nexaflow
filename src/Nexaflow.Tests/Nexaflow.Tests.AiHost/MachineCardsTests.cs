using Nexaflow.AiHost.Client;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>This machine's NVIDIA cards as the SDK reads them before any host runs, through the driver's own library.</summary>
[TestClass]
[CoversNode("ai-host")]
public class MachineCardsTests
{
    [TestMethod]
    public void ThisMachinesCards_AreNamedWithTheirMemory_BesideTheCudaVersionTheirDriverRuns()
    {
        var machine = MachineCards.Probe();
        if (machine.Cards.Count == 0) Assert.Inconclusive("This machine has no NVIDIA card NVML reads.");

        foreach (var card in machine.Cards)
        {
            Assert.IsFalse(string.IsNullOrEmpty(card.Name));
            Assert.IsTrue(card.TotalBytes > 1L << 30, $"{card.Name} has {card.TotalBytes} bytes");
            StringAssert.Matches(card.BusId, new System.Text.RegularExpressions.Regex("^[0-9A-Fa-f]+:[0-9A-Fa-f]+:[0-9A-Fa-f]+\\.[0-9A-Fa-f]$"));
        }
        Assert.IsNotNull(machine.CudaDriver, "a driver that manages a card says which CUDA it runs");
        Assert.IsTrue(machine.CudaDriver.Major >= 11, $"CUDA {machine.CudaDriver}");
    }
}
