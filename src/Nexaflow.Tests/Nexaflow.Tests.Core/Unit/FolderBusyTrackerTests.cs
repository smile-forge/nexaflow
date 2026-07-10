using System.IO;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]
[NoCoverage("folder busy-state tracker — shell plumbing, no single product node")]
public class FolderBusyTrackerTests
{
    [TestMethod]
    public void Mark_SetsMessage_ThenDisposeClearsIt()
    {
        var t = new FolderBusyTracker();
        var path = @"C:\some\folder";

        Assert.IsNull(t.MessageFor(path));

        var handle = t.Mark(path, "Removing…");
        Assert.AreEqual("Removing…", t.MessageFor(path));

        handle.Dispose();
        Assert.IsNull(t.MessageFor(path));
    }

    [TestMethod]
    public void Mark_IsRefCounted_ClearsOnlyAfterLastRelease()
    {
        var t = new FolderBusyTracker();
        var path = @"C:\folder";

        var a = t.Mark(path, "A");
        var b = t.Mark(path, "B");

        a.Dispose();
        Assert.IsNotNull(t.MessageFor(path), "still busy — one mark remains");

        b.Dispose();
        Assert.IsNull(t.MessageFor(path), "cleared once the last mark is released");
    }

    [TestMethod]
    public void Dispose_IsIdempotent()
    {
        var t = new FolderBusyTracker();
        var path = @"C:\folder";

        var a = t.Mark(path, "A");
        var b = t.Mark(path, "B");

        a.Dispose();
        a.Dispose();   // second dispose must not decrement the count again

        Assert.IsNotNull(t.MessageFor(path), "b's mark must survive a's double-dispose");
        b.Dispose();
        Assert.IsNull(t.MessageFor(path));
    }

    [TestMethod]
    public void MessageFor_NormalizesPath()
    {
        var t = new FolderBusyTracker();
        using var _ = t.Mark(@"C:\a\b", "x");

        // Trailing separator and casing shouldn't matter — same folder.
        Assert.AreEqual("x", t.MessageFor(@"C:\a\b" + Path.DirectorySeparatorChar));
        Assert.AreEqual("x", t.MessageFor(@"c:\a\b"));
    }

    [TestMethod]
    public void Changed_FiresOnMarkAndRelease()
    {
        var t = new FolderBusyTracker();
        int fired = 0;
        t.Changed += () => fired++;

        var h = t.Mark(@"C:\folder", "x");
        Assert.AreEqual(1, fired);

        h.Dispose();
        Assert.AreEqual(2, fired);
    }
}
