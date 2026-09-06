using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Operations;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using Nexaflow.IO.Common;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// The copy/move/delete queue — the thing that stands between a drag-drop and the disk.
/// <para>
/// The first test here is the bug that caused all of this: three folders dragged onto a tab, of which
/// only the first arrived. The copy ran inside the OLE drop callback, so the window stopped pumping
/// messages and Windows had nowhere to deliver the next two drops. Queuing is what fixes it, and
/// "three drops, three arrivals" is the assertion that says so.
/// </para>
/// </summary>
[TestClass]
[CoversNode("winfs-drag-drop")]
public class FileOperationQueueTests
{
    private string _scratch = string.Empty;

    [TestInitialize]
    public void CreateScratch()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "nexa-fileops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
    }

    [TestCleanup]
    public void RemoveScratch() { try { Directory.Delete(_scratch, recursive: true); } catch { } }

    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>().Runs();
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
        return shell;
    }

    private string Folder(string name, params string[] files)
    {
        var dir = Path.Combine(_scratch, name);
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), name + "/" + f);
        return dir;
    }

    // ── The reported bug ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task ThreeFoldersDroppedOneAfterAnotherAllArrive()
    {
        var one   = Folder("one",   "a.txt");
        var two   = Folder("two",   "b.txt");
        var three = Folder("three", "c.txt");
        var dest  = Folder("dest");

        var queue = FileOperationQueue.For(Shell());

        // Nothing waits between these — that is the point. The first used to block the UI thread for
        // as long as it took, and a window with no message pump cannot be handed a second drop.
        var ops = new[]
        {
            queue.EnqueueDrop([one],   dest, move: false),
            queue.EnqueueDrop([two],   dest, move: false),
            queue.EnqueueDrop([three], dest, move: false),
        };

        Assert.IsTrue(ops.All(o => o is not null), "every drop was accepted");
        await Task.WhenAll(ops.Select(o => o!.Completion));

        Assert.AreEqual("one/a.txt",   File.ReadAllText(Path.Combine(dest, "one",   "a.txt")));
        Assert.AreEqual("two/b.txt",   File.ReadAllText(Path.Combine(dest, "two",   "b.txt")));
        Assert.AreEqual("three/c.txt", File.ReadAllText(Path.Combine(dest, "three", "c.txt")));
    }

    [TestMethod]
    public void EnqueueingReturnsBeforeTheWorkIsDone()
    {
        var src  = Folder("src", "a.txt");
        var dest = Folder("dest");

        var op = FileOperationQueue.For(Shell()).EnqueueDrop([src], dest, move: false);

        Assert.IsNotNull(op);
        Assert.IsFalse(op.Completion.IsCompleted, "the enqueue does no IO of its own");
    }

    // ── Shared across tabs ────────────────────────────────────────────────────

    [TestMethod]
    public void TwoTabsOnTheSameWorkspaceShareOneQueue()
    {
        var shell = Shell();
        var ai    = Substitute.For<IAIService>();
        var cfg   = new Dictionary<Type, IFeatureConfig>();

        var first  = new FileSystemViewModel(_scratch, shell, ai, cfg);
        var second = new FileSystemViewModel(_scratch, shell, ai, cfg);

        Assert.AreSame(first.Operations, second.Operations,
            "an operation started in one tab has to be visible from the next");
    }

    [TestMethod]
    public void ADifferentWorkspaceGetsItsOwnQueue()
        => Assert.AreNotSame(FileOperationQueue.For(Shell()), FileOperationQueue.For(Shell()));

    // ── Refusals ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task DroppingAFolderIntoItselfIsRefusedRatherThanAttempted()
    {
        var src   = Folder("src", "a.txt");
        var inner = Path.Combine(src, "inner");
        Directory.CreateDirectory(inner);

        var shell = Shell();
        var op    = FileOperationQueue.For(shell).EnqueueDrop([src], inner, move: false);

        Assert.IsNull(op, "nothing is queued when there is nothing legal to do");
        shell.Received().ShowError(Arg.Is<string>(s => s.Contains("into itself")));
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task ANameAlreadyTakenIsRenamedRatherThanOverwritten()
    {
        var src  = Folder("src", "a.txt");
        var dest = Folder("dest");
        Directory.CreateDirectory(Path.Combine(dest, "src"));
        File.WriteAllText(Path.Combine(dest, "src", "a.txt"), "already here");

        var op = FileOperationQueue.For(Shell()).EnqueueDrop([src], dest, move: false);
        await op!.Completion;

        Assert.AreEqual("already here", File.ReadAllText(Path.Combine(dest, "src", "a.txt")),
            "nothing that was already there is touched");
        Assert.AreEqual("src/a.txt", File.ReadAllText(Path.Combine(dest, "src (2)", "a.txt")));
    }

    // ── Cancelling ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CancellingAMoveLeavesTheSourceWhereItIs()
    {
        var src  = Folder("src", "a.txt");
        var dest = Folder("dest");

        var op = FileOperationQueue.For(Shell()).EnqueueDrop([src], dest, move: true);
        op!.Cancel();
        await op.Completion;

        Assert.AreEqual(FileOperationState.Cancelled, op.State);
        Assert.IsTrue(Directory.Exists(src));
    }

    /// <summary>
    /// A stop that lands while the sources are still being measured is still a stop.
    /// <para>
    /// The scan throws <see cref="OperationCanceledException"/> where the run reports a cancellation as an
    /// ordinary result, so a general catch reads it as a fault and the row says "Failed" for something the
    /// user asked to stop. Whether a cancellation lands at the volume gate or inside the scan is a race —
    /// which is why this surfaced as an intermittent CI failure of
    /// <see cref="CancellingAMoveLeavesTheSourceWhereItIs"/> rather than as a reliable one.
    /// </para>
    /// <para>
    /// Enough files that the measuring takes long enough to aim at, and the wait for
    /// <see cref="FileOperationState.Scanning"/> is what aims at it. The assertion holds either way now,
    /// so the test cannot itself be flaky — it only makes the interesting path the likely one.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task CancellingWhileTheSourcesAreStillBeingMeasuredIsAStopNotAFailure()
    {
        var src  = Folder("src");
        for (var i = 0; i < 4_000; i++)
            File.WriteAllText(Path.Combine(src, $"f{i:D4}.txt"), "x");
        var dest = Folder("dest");

        var op = FileOperationQueue.For(Shell()).EnqueueDrop([src], dest, move: false);
        Assert.IsNotNull(op);

        var spun = System.Diagnostics.Stopwatch.StartNew();
        while (op!.State != FileOperationState.Scanning && spun.ElapsedMilliseconds < 2_000)
            await Task.Yield();

        op.Cancel();
        await op.Completion;

        Assert.AreEqual(FileOperationState.Cancelled, op.State,
            "a cancelled scan is a cancellation, not a fault");
        Assert.IsFalse(op.HasProblems, "and it has nothing to report as a problem");
    }

    /// <summary>
    /// A cancel is heard long before the run can actually stop — the engine only looks between files —
    /// so the row has to say so the instant the button is pressed. It did not: the glyph, the byte
    /// counter and the transfer rate all carried on exactly as before, and the Cancel button went on
    /// offering itself, so the only evidence the click had landed was the operation ending some minutes
    /// later. That reads as a dead button, and gets pressed again.
    /// </summary>
    [TestMethod]
    [CoversNode("file-operations-panel")]
    public void PressingCancelChangesTheRowAtOnce_EvenThoughTheRunCannotStopYet()
    {
        var op = new FileOperation("Copying", "3 items", "Archive", itemsTotal: 3);
        op.Publish(new TransferProgress(
            TransferPhase.Running, BytesDone: 1_000, BytesTotal: 10_000, ItemsDone: 1, ItemsTotal: 3,
            CurrentItem: "big.bin", BytesPerSecond: 500, Remaining: TimeSpan.FromSeconds(18), Paused: null));

        string runningDetail = op.Detail;
        Assert.IsTrue(op.CanCancel);

        op.Cancel();

        Assert.AreEqual("Stopping…", op.Detail, "the row has to say the click landed");
        Assert.AreNotEqual(runningDetail, op.Detail);
        Assert.AreNotEqual("↻", op.StatusGlyph, "a cancelling run must not wear the running glyph");
        Assert.IsFalse(op.CanCancel, "cancelling already — the button should stop inviting the same click");
        Assert.IsFalse(op.IsFinished, "it has not actually stopped yet, and must not claim to have");

        // Progress that arrives while it is unwinding must not talk over the cancel.
        op.Publish(new TransferProgress(
            TransferPhase.Running, BytesDone: 2_000, BytesTotal: 10_000, ItemsDone: 1, ItemsTotal: 3,
            CurrentItem: "big.bin", BytesPerSecond: 500, Remaining: TimeSpan.FromSeconds(16), Paused: null));

        Assert.AreEqual("Stopping…", op.Detail);
    }

    /// <summary>
    /// A clean copy has to clear its own row. It did not, and a session's worth of copying left a dozen
    /// rows all reading "Done." — because retirement hung off the background task returning, which
    /// happens before the row has been told the outcome. Judged on a state that had not arrived yet,
    /// every completed operation looked unfinished and was left alone.
    /// </summary>
    [TestMethod]
    public async Task ACleanCopyTakesItsOwnRowAwayAfterwards()
    {
        var src   = Folder("src", "a.txt");
        var dest  = Folder("dest");
        var queue = FileOperationQueue.For(Shell());

        var op = queue.EnqueueDrop([src], dest, move: false);
        Assert.IsNotNull(op);
        await op.Completion;

        Assert.AreEqual(FileOperationState.Completed, op.State,
                        "the outcome is settled before Completion fires — retirement is judged on it");

        Assert.IsTrue(await Settles(() => !queue.Operations.Contains(op)),
                      "a finished row clears itself out of the way rather than accumulating");
    }

    /// <summary>
    /// A row that has stopped must stop looking like one that is working. The bar is indeterminate while
    /// there is no byte total to measure against — and anything that ends without ever getting one (a
    /// cancelled scan, a delete) left that true after the work was over, so a row reading "Stopped."
    /// went on sweeping underneath itself, and the next copy inherited the animation.
    /// </summary>
    [TestMethod]
    [CoversNode("file-operations-panel")]
    public void AStoppedRowStopsAnimating()
    {
        var op = new FileOperation("Copying", "3 items", "Archive", itemsTotal: 3);
        Assert.IsTrue(op.IsIndeterminate, "nothing measured yet — the bar sweeps");

        op.Cancel();
        Assert.IsTrue(op.IsIndeterminate, "still unwinding: there is something to wait for");

        op.Finish(new TransferResult(
            Completed: false, BytesTransferred: 0, ItemsTransferred: 0,
            Failures: [], SkippedReparsePoints: [], RenamedDestinations: [], PartialDestinations: []));

        Assert.IsTrue(op.IsFinished);
        Assert.IsFalse(op.IsIndeterminate, "nothing left to wait for, so nothing left to animate");
    }

    /// <summary>Polls a condition for a few seconds — the retire delay is a real one.</summary>
    private static async Task<bool> Settles(Func<bool> condition, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(200);
        }
        return condition();
    }
}
