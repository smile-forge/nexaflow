using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>
/// Unit tests for the bulk copy/move/delete engine. The ones that matter most are the guarantees a
/// drag-and-drop of 200 GB onto a full disk broke: a move must not delete a source whose copy
/// failed, one bad item must not abandon the rest of the tree, and running out of space must park
/// the run rather than half-finish it.
/// </summary>
[TestClass]
[DoNotParallelize]   // FreeSpaceProbe is a static seam — two of these at once would answer for each other
[NoCoverage("bulk file transfer engine — no single product node")]
public class FileTransferEngineTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexaflow-transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        FileTransferEngine.FreeSpaceProbe = null;
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Dir(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    private string FileWith(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static Task<TransferResult> Run(
        TransferKind kind, string source, string destination,
        ConflictPolicy conflicts = ConflictPolicy.AutoRename,
        TransferScan? scan = null,
        IProgress<TransferProgress>? progress = null,
        IFileTransferPrompt? prompt = null,
        CancellationToken ct = default)
        => FileTransferEngine.RunAsync(
            new FileTransferRequest(kind, [new TransferItem(source, destination)], conflicts),
            scan, progress, prompt, ct);

    // ── Copy ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ACopiedTreeArrivesWhole_AndTheSourceIsUntouched()
    {
        var src = Dir("src");
        FileWith(@"src\top.txt", "top");
        FileWith(@"src\sub\mid.txt", "mid");
        FileWith(@"src\sub\deep\leaf.txt", "leaf");

        var dest = Path.Combine(_root, "dest");
        var result = await Run(TransferKind.Copy, src, dest);

        Assert.IsTrue(result.Completed);
        Assert.AreEqual(0, result.Failures.Count, string.Join("; ", result.Failures.Select(f => f.Message)));
        Assert.AreEqual("top",  File.ReadAllText(Path.Combine(dest, "top.txt")));
        Assert.AreEqual("mid",  File.ReadAllText(Path.Combine(dest, "sub", "mid.txt")));
        Assert.AreEqual("leaf", File.ReadAllText(Path.Combine(dest, "sub", "deep", "leaf.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(src, "top.txt")), "a copy leaves the source alone");
    }

    [TestMethod]
    public async Task NoPartialFileSurvivesASuccessfulCopy()
    {
        var src = Dir("src");
        FileWith(@"src\a.bin", new string('x', 200_000));

        var dest = Path.Combine(_root, "dest");
        await Run(TransferKind.Copy, src, dest);

        Assert.AreEqual(0, Directory.GetFiles(dest, "*" + ".nexaflow-partial").Length,
            "the temporary name is renamed away once the bytes are verified");
    }

    // ── Move ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ASameVolumeFolderMoveIsARename_SoNoBytesAreRead()
    {
        var src = Dir("src");
        FileWith(@"src\big.bin", new string('x', 500_000));

        var dest = Path.Combine(_root, "dest");
        var result = await Run(TransferKind.Move, src, dest);

        Assert.IsTrue(result.Completed);
        Assert.AreEqual(0L, result.BytesTransferred, "a rename moves no bytes");
        Assert.IsFalse(Directory.Exists(src));
        Assert.AreEqual(500_000, File.ReadAllText(Path.Combine(dest, "big.bin")).Length);
    }

    [TestMethod]
    public async Task AMoveDoesNotDeleteASourceWhoseCopyFailed()
    {
        // A directory sitting where a file must land makes that one file's copy fail. The rename
        // fast path cannot apply because the destination tree already exists.
        var src = Dir("src");
        FileWith(@"src\good.txt", "good");
        FileWith(@"src\blocked.txt", "blocked");

        var dest = Dir("dest");
        Directory.CreateDirectory(Path.Combine(dest, "blocked.txt"));

        var result = await Run(TransferKind.Move, src, dest, ConflictPolicy.Resume);

        Assert.IsTrue(result.Failures.Count > 0, "the blocked file is reported");
        Assert.IsTrue(File.Exists(Path.Combine(src, "blocked.txt")),
            "the source of a copy that failed is still there");
        Assert.IsTrue(Directory.Exists(src),
            "an incomplete move cannot take the source folder with it");
        Assert.AreEqual("good", File.ReadAllText(Path.Combine(dest, "good.txt")),
            "the rest of the tree still arrived");
        Assert.IsFalse(File.Exists(Path.Combine(src, "good.txt")),
            "a source whose copy was verified is removed");
    }

    // ── Per-item tolerance ────────────────────────────────────────────────────

    [TestMethod]
    public async Task OneUnreadableFileIsRecorded_AndEverySiblingStillArrives()
    {
        var src = Dir("src");
        FileWith(@"src\first.txt", "first");
        var locked = FileWith(@"src\locked.bin", "locked");
        FileWith(@"src\last.txt", "last");
        FileWith(@"src\sub\nested.txt", "nested");

        var dest = Path.Combine(_root, "dest");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await Run(TransferKind.Copy, src, dest);

            Assert.IsTrue(result.Completed, "a per-item fault is not a failed run");
            Assert.AreEqual(1, result.Failures.Count);
            StringAssert.Contains(result.Failures[0].Path, "locked.bin");
        }

        // The whole tree was attempted — the walk did not stop at the first fault.
        Assert.AreEqual("first",  File.ReadAllText(Path.Combine(dest, "first.txt")));
        Assert.AreEqual("last",   File.ReadAllText(Path.Combine(dest, "last.txt")));
        Assert.AreEqual("nested", File.ReadAllText(Path.Combine(dest, "sub", "nested.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(dest, "locked.bin" + ".nexaflow-partial")),
            "a failed copy cleans up after itself");
    }

    [TestMethod]
    public async Task ASourceThatDoesNotExistIsNotAComplaint()
    {
        var result = await Run(TransferKind.Copy, Path.Combine(_root, "ghost"), Path.Combine(_root, "dest"));

        Assert.IsTrue(result.Completed);
        Assert.AreEqual(0, result.Failures.Count);
    }

    // ── Conflict policy ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task AutoRenameGivesTheSecondArrivalItsOwnName()
    {
        FileWith(@"src\a.txt", "one");
        var dest = Dir("dest");
        File.WriteAllText(Path.Combine(dest, "a.txt"), "already here");

        var result = await Run(TransferKind.Copy, Path.Combine(_root, "src", "a.txt"),
                               Path.Combine(dest, "a.txt"), ConflictPolicy.AutoRename);

        Assert.AreEqual("already here", File.ReadAllText(Path.Combine(dest, "a.txt")), "nothing is overwritten");
        Assert.AreEqual("one", File.ReadAllText(Path.Combine(dest, "a (2).txt")));
        CollectionAssert.Contains(result.RenamedDestinations.ToArray(), Path.Combine(dest, "a (2).txt"));
    }

    [TestMethod]
    public async Task ResumeContinuesIntoTheSameFolderRatherThanMakingASecondOne()
    {
        var src = Dir("src");
        FileWith(@"src\done.txt", "done");
        FileWith(@"src\todo.txt", "todo");

        var dest = Dir("dest");
        File.WriteAllText(Path.Combine(dest, "done.txt"), "done");

        var result = await Run(TransferKind.Copy, src, dest, ConflictPolicy.Resume);

        Assert.IsTrue(result.Completed);
        Assert.AreEqual(0, result.RenamedDestinations.Count, "a resume never invents a second folder");
        Assert.IsFalse(Directory.Exists(dest + " (2)"));
        Assert.AreEqual("todo", File.ReadAllText(Path.Combine(dest, "todo.txt")));
    }

    // ── Out of space ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task WithNobodyToAsk_ATooBigCopyFailsBeforeItWritesAnything()
    {
        var src = Dir("src");
        FileWith(@"src\big.bin", new string('x', 100_000));
        var dest = Path.Combine(_root, "dest");

        FileTransferEngine.FreeSpaceProbe = _ => 10;   // ten bytes free

        var result = await Run(TransferKind.Copy, src, dest);

        Assert.AreEqual(1, result.Failures.Count);
        Assert.AreEqual(112, result.Failures[0].Win32Code, "reported as ERROR_DISK_FULL");
        StringAssert.Contains(result.Failures[0].Message, "free space");
        Assert.IsFalse(File.Exists(Path.Combine(dest, "big.bin")), "nothing was written");
    }

    [TestMethod]
    public async Task RunningOutOfSpaceParksTheRun_AndFreeingSomeLetsItFinish()
    {
        var src = Dir("src");
        FileWith(@"src\big.bin", new string('x', 100_000));
        var dest = Dir("dest");   // already there, so the rename cannot apply and bytes must actually move

        // Full on the first look, roomy once the caller says it has freed some.
        var prompt = new ScriptedPrompt(PauseDecision.Retry);
        FileTransferEngine.FreeSpaceProbe = _ => prompt.Answered == 0 ? 10 : long.MaxValue;

        var result = await Run(TransferKind.Move, src, dest, ConflictPolicy.Resume, prompt: prompt);

        Assert.AreEqual(1, prompt.Answered, "the caller was asked exactly once");
        Assert.AreEqual(PauseReason.OutOfSpace, prompt.LastReason);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(0, result.Failures.Count);
        Assert.AreEqual(100_000, File.ReadAllText(Path.Combine(dest, "big.bin")).Length);
    }

    [TestMethod]
    public async Task DecliningAPausedRunLeavesTheSourceWhereItIs()
    {
        var src = Dir("src");
        FileWith(@"src\big.bin", new string('x', 100_000));
        var dest = Dir("dest");   // already there, so the move must copy rather than rename

        var prompt = new ScriptedPrompt(PauseDecision.Cancel);
        FileTransferEngine.FreeSpaceProbe = _ => 10;

        var result = await Run(TransferKind.Move, src, dest, ConflictPolicy.Resume, prompt: prompt);

        Assert.IsFalse(result.Completed, "declining ends the run as cancelled");
        Assert.IsTrue(File.Exists(Path.Combine(src, "big.bin")), "a move that never copied deletes nothing");
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CancellingLeavesNoPartialFileBehind()
    {
        var src = Dir("src");
        FileWith(@"src\a.bin", new string('x', 4_000_000));
        var dest = Path.Combine(_root, "dest");

        using var cts = new CancellationTokenSource();
        var progress = new Progress<TransferProgress>(p => { if (p.BytesDone > 0) cts.Cancel(); });

        var result = await Run(TransferKind.Copy, src, dest, progress: progress, ct: cts.Token);

        Assert.IsFalse(result.Completed);
        if (Directory.Exists(dest))
            Assert.AreEqual(0, Directory.GetFiles(dest, "*" + ".nexaflow-partial").Length);
    }

    [TestMethod]
    public async Task ACancelledMoveKeepsTheSource()
    {
        var src = Dir("src");
        FileWith(@"src\a.txt", "a");
        FileWith(@"src\b.txt", "b");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Same volume, so the rename would be instant — cancellation has to be checked first.
        var result = await Run(TransferKind.Move, src, Path.Combine(_root, "dest"), ct: cts.Token);

        Assert.IsFalse(result.Completed);
        Assert.IsTrue(Directory.Exists(src));
    }

    // ── Reparse points ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AJunctionIsRecordedAndNeverFollowed()
    {
        var src    = Dir("src");
        var target = Dir("target");
        FileWith(@"target\inside.txt", "inside");
        FileWith(@"src\real.txt", "real");

        try { Directory.CreateSymbolicLink(Path.Combine(src, "link"), target); }
        catch (Exception ex) { Assert.Inconclusive($"this machine will not create a directory link: {ex.Message}"); }

        var dest   = Path.Combine(_root, "dest");
        var result = await Run(TransferKind.Copy, src, dest);

        Assert.AreEqual(1, result.SkippedReparsePoints.Count);
        Assert.IsTrue(Directory.Exists(Path.Combine(dest, "link")), "the link is recreated as an empty folder");
        Assert.IsFalse(File.Exists(Path.Combine(dest, "link", "inside.txt")), "its target is not copied through it");
        Assert.AreEqual("real", File.ReadAllText(Path.Combine(dest, "real.txt")));
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task DeleteRemovesTheWholeTree()
    {
        var src = Dir("src");
        FileWith(@"src\a.txt", "a");
        FileWith(@"src\sub\b.txt", "b");

        var result = await FileTransferEngine.RunAsync(
            new FileTransferRequest(TransferKind.Delete, [new TransferItem(src, src)]));

        Assert.IsTrue(result.Completed);
        Assert.AreEqual(0, result.Failures.Count);
        Assert.IsFalse(Directory.Exists(src));
    }

    [TestMethod]
    public async Task DeleteKeepsWhatItCouldNotRemove_AndSaysSo()
    {
        var src = Dir("src");
        var locked = FileWith(@"src\locked.bin", "locked");
        FileWith(@"src\gone.txt", "gone");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await FileTransferEngine.RunAsync(
                new FileTransferRequest(TransferKind.Delete, [new TransferItem(src, src)]));

            Assert.AreEqual(1, result.Failures.Count);
            Assert.IsTrue(Directory.Exists(src), "a folder is not removed while something in it survives");
        }

        Assert.IsFalse(File.Exists(Path.Combine(src, "gone.txt")), "the rest was still deleted");
    }

    // ── Progress ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ProgressOnlyEverGoesForwards_AndFinishesOnTheTotal()
    {
        var src = Dir("src");
        FileWith(@"src\a.bin", new string('x', 300_000));
        FileWith(@"src\b.bin", new string('y', 300_000));

        var scan   = await FileTransferEngine.ScanAsync([src]);
        var seen   = new List<TransferProgress>();
        var dest   = Path.Combine(_root, "dest");

        await Run(TransferKind.Copy, src, dest, scan: scan,
                  progress: new SynchronousProgress(seen.Add));

        Assert.AreEqual(600_000, scan.TotalBytes);
        Assert.AreEqual(2, scan.TotalFiles);
        Assert.IsFalse(scan.Partial);

        long last = 0;
        foreach (var p in seen)
        {
            Assert.IsTrue(p.BytesDone >= last, "byte progress never goes backwards");
            last = p.BytesDone;
        }

        Assert.AreEqual(TransferPhase.Finished, seen[^1].Phase);
        Assert.AreEqual(600_000, seen[^1].BytesDone);
        Assert.AreEqual(2, seen[^1].ItemsDone);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Answers every pause the same way and counts how often it was asked.</summary>
    private sealed class ScriptedPrompt(PauseDecision decision) : IFileTransferPrompt
    {
        public int Answered { get; private set; }
        public PauseReason? LastReason { get; private set; }

        public Task<PauseDecision> OnPausedAsync(PauseReason reason, string detail, CancellationToken ct)
        {
            LastReason = reason;
            Answered++;
            return Task.FromResult(decision);
        }
    }

    /// <summary>Records on the calling thread, so the assertions see every report.
    /// <see cref="Progress{T}"/> posts to a context and would drop the tail.</summary>
    private sealed class SynchronousProgress(Action<TransferProgress> onReport) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => onReport(value);
    }
}
