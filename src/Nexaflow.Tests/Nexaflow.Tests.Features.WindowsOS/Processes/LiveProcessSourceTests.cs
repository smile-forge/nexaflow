using Nexaflow.Features.Processes.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Processes;

/// <summary>What <see cref="LiveProcessSource"/> reads off this machine. Concurrent use is
/// <see cref="LiveProcessSourceConcurrencyTests"/>.</summary>
[TestClass]
[CoversNode("processes-live-source")]
public class LiveProcessSourceTests
{
    [TestMethod]
    public void Snapshot_PutsEachProcesssMemoryOnItsRow()
    {
        using var source = new LiveProcessSource();

        var self = source.Snapshot().Processes.Single(p => p.Pid == Environment.ProcessId);

        Assert.IsTrue(self.PrivateBytes > 0, "this process's private bytes are on its row");
        Assert.IsTrue(self.WorkingSet > 0, "this process's working set is on its row");
        Assert.IsFalse(self.AccessDenied, "a process can always read itself");
    }
}
