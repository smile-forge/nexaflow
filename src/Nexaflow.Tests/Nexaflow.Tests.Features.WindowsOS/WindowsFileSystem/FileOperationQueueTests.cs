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
}
