using System;
using System.IO;
using System.Linq;
using System.Threading;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>
/// Collecting what changed under a tree, for a holder that redoes only what moved: every path, once, handed over when
/// asked — and the one call a burst deserves, a quiet moment after it.
/// </summary>
[TestClass]
[CoversNode("iocommon-treewatch")]
public class TreeChangeWatcherTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = TestTemp.CreateSubdirectory("tree-watch-").FullName;
        Directory.CreateDirectory(Path.Combine(_root, "src", "deep"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static void Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail($"timed out waiting for {what}");
            Thread.Sleep(20);
        }
    }

    [TestMethod]
    public void AFileWrittenAnywhereBeneath_IsCollected_AndTakenOnce()
    {
        using var watcher = new TreeChangeWatcher(_root, _ => true);
        var file = Path.Combine(_root, "src", "deep", "A.cs");

        File.WriteAllText(file, "class A { }");
        Until(() => watcher.HasChanges, "the write to be collected");

        CollectionAssert.Contains(watcher.Take().Paths.ToList(), file);
        Assert.IsFalse(watcher.HasChanges, "what was taken is gone, and the next batch starts empty");
    }

    [TestMethod]
    public void ARename_IsCollectedByBothNames()
    {
        var from = Path.Combine(_root, "src", "Old.cs");
        var to   = Path.Combine(_root, "src", "New.cs");
        File.WriteAllText(from, "class A { }");

        using var watcher = new TreeChangeWatcher(_root, _ => true);
        File.Move(from, to);
        Until(() => watcher.HasChanges, "the rename to be collected");
        Thread.Sleep(100);

        var paths = watcher.Take().Paths.ToList();
        CollectionAssert.Contains(paths, from, "the old name, so whatever described it is let go");
        CollectionAssert.Contains(paths, to);
    }

    [TestMethod]
    public void APathItIsToldToIgnore_IsNotCollected()
    {
        using var watcher = new TreeChangeWatcher(_root, path => !path.Contains("deep", StringComparison.Ordinal));

        File.WriteAllText(Path.Combine(_root, "src", "deep", "Noise.cs"), "class N { }");
        File.WriteAllText(Path.Combine(_root, "src", "Signal.cs"), "class S { }");
        Until(() => watcher.HasChanges, "the relevant write to be collected");
        Thread.Sleep(200);

        var paths = watcher.Take().Paths.ToList();
        Assert.IsTrue(paths.Exists(p => p.EndsWith("Signal.cs", StringComparison.Ordinal)));
        Assert.IsFalse(paths.Exists(p => p.Contains("deep", StringComparison.Ordinal)));
    }

    /// <summary>A checkout is thousands of notifications in a second; what the holder wants is one call once it is over.</summary>
    [TestMethod]
    public void ABurst_IsOneCall_AQuietMomentAfterItsLastNotification()
    {
        using var watcher = new TreeChangeWatcher(_root, _ => true, quietMs: 200);
        var calls = 0;
        watcher.Changed += () => Interlocked.Increment(ref calls);

        for (var i = 0; i < 20; i++) File.WriteAllText(Path.Combine(_root, "src", $"F{i}.cs"), $"class F{i} {{ }}");
        Until(() => Volatile.Read(ref calls) > 0, "the burst to be reported");
        Thread.Sleep(600);

        Assert.AreEqual(1, Volatile.Read(ref calls));
        Assert.AreEqual(20, watcher.Take().Paths.Count(p => Path.GetFileName(p).StartsWith('F')));
    }
}
