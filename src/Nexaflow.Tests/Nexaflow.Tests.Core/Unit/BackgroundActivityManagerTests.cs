using Nexaflow.Core.Services;
using TaskStatus = Nexaflow.Core.Models.TaskStatus;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]
public class BackgroundActivityManagerTests
{
    // The manager captures the constructing thread's dispatcher (the test thread here), so its
    // Dispatch sees CheckAccess == true and runs every action synchronously — all mutations inline.

    [TestMethod]
    public void Constructor_InsertsIdlePlaceholder()
    {
        var mgr = new BackgroundActivityManager();

        Assert.AreEqual(1, mgr.Tasks.Count);
        Assert.AreEqual("Idle", mgr.Tasks[0].Description);
        Assert.IsFalse(mgr.IsActive);
    }

    [TestMethod]
    public void StartActivity_RemovesIdleAndAddsTask()
    {
        var mgr = new BackgroundActivityManager();

        mgr.StartActivity("Syncing");

        Assert.AreEqual(1, mgr.Tasks.Count);
        Assert.AreEqual("Syncing", mgr.Tasks[0].Description);
        Assert.IsTrue(mgr.IsActive);
    }

    [TestMethod]
    public void Complete_SetsStatusCompleted()
    {
        var mgr = new BackgroundActivityManager();
        var handle = mgr.StartActivity("Work");
        var task = mgr.Tasks[0];

        handle.Complete();

        Assert.AreEqual(TaskStatus.Completed, task.Status);
    }

    [TestMethod]
    public void Fail_SetsStatusFailedAndAppendsReason()
    {
        var mgr = new BackgroundActivityManager();
        var handle = mgr.StartActivity("Work");
        var task = mgr.Tasks[0];

        handle.Fail("disk full");

        Assert.AreEqual(TaskStatus.Failed, task.Status);
        StringAssert.Contains(task.Description, "disk full");
    }

    [TestMethod]
    public void Fail_NullReason_DoesNotAppendSeparator()
    {
        var mgr = new BackgroundActivityManager();
        var handle = mgr.StartActivity("Work");
        var task = mgr.Tasks[0];

        handle.Fail(null);

        Assert.AreEqual(TaskStatus.Failed, task.Status);
        Assert.AreEqual("Work", task.Description);
    }

    [TestMethod]
    public void LastComplete_RestoresIdlePlaceholderAndClearsIsActive()
    {
        var mgr = new BackgroundActivityManager();
        var handle = mgr.StartActivity("Work");

        handle.Complete();

        Assert.IsFalse(mgr.IsActive);
        // Idle is re-inserted; the completed task stays, idle is prepended
        Assert.IsTrue(mgr.Tasks.Any(t => t.Description == "Idle"));
    }

    [TestMethod]
    public void IsActiveChanged_FiresOnStartAndOnLastComplete()
    {
        var mgr = new BackgroundActivityManager();
        var events = new List<bool>();
        mgr.IsActiveChanged += (_, v) => events.Add(v);

        var handle = mgr.StartActivity("Work");
        handle.Complete();

        CollectionAssert.AreEqual(new[] { true, false }, events);
    }

    [TestMethod]
    public void MultipleActivities_IdleAbsentUntilBothComplete()
    {
        var mgr = new BackgroundActivityManager();
        var h1 = mgr.StartActivity("A");
        var h2 = mgr.StartActivity("B");

        h1.Complete();
        Assert.IsTrue(mgr.IsActive);
        Assert.IsFalse(mgr.Tasks.Any(t => t.Description == "Idle"));

        h2.Complete();
        Assert.IsFalse(mgr.IsActive);
        Assert.IsTrue(mgr.Tasks.Any(t => t.Description == "Idle"));
    }
}
