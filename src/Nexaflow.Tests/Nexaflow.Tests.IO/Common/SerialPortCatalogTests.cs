using Nexaflow.IO.Common.Hardware;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>The COM ports this machine has. Only lists what Windows knows — no port is opened.</summary>
[TestClass]
[CoversNode("iocommon-serial-ports")]
public class SerialPortCatalogTests
{
    [TestMethod]
    public void EveryPort_IsListedOnce_InNumberOrder()
    {
        var ports = SerialPortCatalog.List();
        if (ports.Count == 0) Assert.Inconclusive("no COM ports on this machine");

        Assert.AreEqual(ports.Count, ports.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var numbers = ports.Where(p => p.Name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)).Select(p => int.Parse(p.Name[3..])).ToList();
        CollectionAssert.AreEqual(numbers.Order().ToList(), numbers);
        foreach (var p in ports.Where(p => p.Description is not null))
            Assert.IsFalse(p.Description!.Contains("(COM", StringComparison.OrdinalIgnoreCase), $"{p.DisplayName}: the port is not named twice");
    }
}
