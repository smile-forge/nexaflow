using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A folder watched through the shell's change notifications: what changes in it is told once a burst settles, naming the
/// entries that changed; a watch that is let go is told nothing more; and a folder the shell cannot name is not watched.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("winfs-folder-watch")]
public class ShellFolderWatcherTests
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = TestTemp.CreateSubdirectory("watch-").FullName;
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    /// <summary>The shell names the entry, or — when it has coalesced several notifications — says the whole folder changed.</summary>
    [TestMethod]
    public void AFileMadeInTheFolder_IsTold() => UiThread.Run(() =>
    {
        using var watcher = new ShellFolderWatcher(Dispatcher.CurrentDispatcher);
        var told = new List<FolderChange>();
        using var watch = watcher.Watch(_folder, told.Add);
        Armed(told);

        var made = Path.Combine(_folder, "made.txt");
        File.WriteAllText(made, "hello");
        PumpUntil(() => told.Any(change => change.Touches(made)));
    });

    [TestMethod]
    public void ABurstOfChanges_IsToldOnce() => UiThread.Run(() =>
    {
        using var watcher = new ShellFolderWatcher(Dispatcher.CurrentDispatcher);
        var told = new List<FolderChange>();
        using var watch = watcher.Watch(_folder, told.Add);
        Armed(told);

        for (int i = 0; i < 5; i++) File.WriteAllText(Path.Combine(_folder, $"file{i}.txt"), "x");
        PumpUntil(() => told.Count > 0);
        PumpFor(TimeSpan.FromMilliseconds(600));

        string seen = string.Join(" | ", told.Select(c => (c.Whole ? "whole; " : "") + string.Join(", ", c.Items)));
        Assert.AreEqual(1, told.Count, "five files made at once are one change: " + seen);
        Assert.IsTrue(Enumerable.Range(0, 5).All(i => told[0].Touches(Path.Combine(_folder, $"file{i}.txt"))), "naming each: " + seen);
    });

    [TestMethod]
    public void AWatchLetGo_IsToldNothingMore() => UiThread.Run(() =>
    {
        using var watcher = new ShellFolderWatcher(Dispatcher.CurrentDispatcher);
        var told = new List<FolderChange>();
        var watch = watcher.Watch(_folder, told.Add);
        Armed(told);
        watch.Dispose();

        File.WriteAllText(Path.Combine(_folder, "late.txt"), "x");
        PumpFor(TimeSpan.FromMilliseconds(800));

        Assert.AreEqual(0, told.Count);
    });

    [TestMethod]
    public void TwoWatchesOnOneFolder_AreBothTold() => UiThread.Run(() =>
    {
        using var watcher = new ShellFolderWatcher(Dispatcher.CurrentDispatcher);
        var first = new List<FolderChange>();
        var second = new List<FolderChange>();
        using var one = watcher.Watch(_folder, first.Add);
        using var two = watcher.Watch(_folder, second.Add);
        Armed(first);
        second.Clear();

        File.WriteAllText(Path.Combine(_folder, "shared.txt"), "x");
        PumpUntil(() => first.Count > 0 && second.Count > 0);
    });

    /// <summary>
    /// Waits until the shell is listening: its registration arms a moment after the call returns, and a change made before
    /// then goes unheard (the folder's own listing is what covers that moment in the app). Writes a marker until one is told,
    /// then lets that settle and forgets it.
    /// </summary>
    private void Armed(List<FolderChange> told)
    {
        for (int i = 0; i < 50 && told.Count == 0; i++)
        {
            File.WriteAllText(Path.Combine(_folder, $"arm{i}.txt"), "x");
            PumpFor(TimeSpan.FromMilliseconds(100));
        }
        PumpFor(TimeSpan.FromMilliseconds(500));
        Assert.IsTrue(told.Count > 0, "the shell never started listening");
        told.Clear();
    }

    private static void PumpUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var frame = new DispatcherFrame();
        var check = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
                                        (_, _) => { if (done() || DateTime.UtcNow > deadline) frame.Continue = false; },
                                        Dispatcher.CurrentDispatcher);
        try { if (!done()) Dispatcher.PushFrame(frame); }
        finally { check.Stop(); }
        Assert.IsTrue(done(), "the shell's notification did not arrive");
    }

    private static void PumpFor(TimeSpan time)
    {
        var until = DateTime.UtcNow + time;
        PumpUntil(() => DateTime.UtcNow >= until);
    }
}
