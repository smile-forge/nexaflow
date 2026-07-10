using System.Linq;
using Nexaflow.Features.SystemInfo.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.SystemInfo;

[TestClass]
[CoversNode("sysinfo-services")]
public class ServicesCollectorTests
{
    [TestMethod]
    public void Collect_ReturnsServices_WithStatusAndStartMode()
    {
        var rows = new ServicesCollector().Collect();

        Assert.IsTrue(rows.Count > 0, "Expected at least one Windows service.");
        Assert.IsTrue(rows.All(r => !string.IsNullOrEmpty(r.Status)), "Every row should have a status.");
        Assert.IsTrue(rows.All(r => !string.IsNullOrEmpty(r.StartMode)), "Every row should have a start mode.");
    }

    [TestMethod]
    public void ReadState_ForACollectedService_ReturnsState()
    {
        var collector = new ServicesCollector();
        var any = collector.Collect().FirstOrDefault();
        Assert.IsNotNull(any);

        var state = collector.ReadState(any!.Name);

        Assert.IsNotNull(state);
        Assert.IsFalse(string.IsNullOrEmpty(state!.Value.Status));
        Assert.IsFalse(string.IsNullOrEmpty(state.Value.StartMode));
    }
}
