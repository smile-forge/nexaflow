using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed;
using Nexaflow.Features.Compressed.FileActions;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>
/// Zip It and Unzip Here once a progress host is available.
/// <para>
/// Both used to run their whole archive on the caller's thread, so a large one froze the window with
/// nothing to look at and no way to stop it. With a host they hand the work over and return at once;
/// without one they behave exactly as they did, which is what keeps them usable anywhere.
/// </para>
/// </summary>
[TestClass]
public class ArchiveProgressActionTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexa-arcact-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);

        // The actions reach for the process-wide VFS by design, so this process needs a zip backend.
        if (!VirtualFileSystem.Instance.CanCreate("a.zip"))
            VirtualFileSystem.Instance.RegisterHandler(new ZipArchiveHandler());
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string Folder(string name, params string[] files)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), f);
        return dir;
    }

    private string Archive(string name, params string[] files)
    {
        var src  = Folder(name + "-src", files);
        var path = Path.Combine(_root, name + ".zip");
        VirtualFileSystem.Instance.CreateArchive(path, src);
        return path;
    }

    // ── Unzip here ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("compressed-unzip-here")]
    public async Task UnzipHereHandsTheWorkOverAndReturnsAtOnce()
    {
        var archive = Archive("photos", "a.txt", "b.txt");
        var host    = new RecordingHost();
        var action  = new UnzipHereAction(Substitute.For<IShellServices>(), host);

        Assert.IsTrue(action.PerformAction(archive), "the action reports that it started, not that it finished");
        Assert.AreEqual(1, host.Requests.Count);
        Assert.AreEqual("Extracting", host.Requests[0].Verb);
        Assert.AreEqual("photos.zip", host.Requests[0].Subject);

        await host.Completion;

        var dest = Path.Combine(_root, "photos");
        CollectionAssert.AreEquivalent(new[] { "a.txt", "b.txt" },
            Directory.GetFiles(dest).Select(Path.GetFileName).ToArray());
        Assert.IsTrue(host.Results[0].Completed);
    }

    /// <summary>
    /// The row measures against the destination's volume, because that is the disk the bytes land on —
    /// gate it on the archive's own path and two extractions onto different disks would take turns for
    /// no reason.
    /// </summary>
    [TestMethod]
    [CoversNode("compressed-unzip-here")]
    public void UnzipHereNamesTheDestinationItWillWriteTo()
    {
        var archive = Archive("docs", "a.txt");
        var host    = new RecordingHost();

        new UnzipHereAction(Substitute.For<IShellServices>(), host).PerformAction(archive);

        Assert.AreEqual(Path.Combine(_root, "docs"), host.Requests[0].DestinationPath);
    }

    [TestMethod]
    [CoversNode("compressed-unzip-here")]
    public async Task UnzipHereWithNoHostStillExtractsThereAndThen()
    {
        var archive = Archive("plain", "a.txt");

        Assert.IsTrue(new UnzipHereAction(Substitute.For<IShellServices>()).PerformAction(archive));

        // Nothing to await — with no host the work is already done by the time it returns.
        await Task.CompletedTask;
        Assert.IsTrue(File.Exists(Path.Combine(_root, "plain", "a.txt")));
    }

    /// <summary>Several archives are several cancellable rows, not one opaque wait — the same shape as
    /// dropping several folders.</summary>
    [TestMethod]
    [CoversNode("compressed-unzip-here")]
    public void UnzipHereGivesEachArchiveItsOwnRow()
    {
        var first  = Archive("one", "a.txt");
        var second = Archive("two", "b.txt");
        var host   = new RecordingHost();

        new UnzipHereAction(Substitute.For<IShellServices>(), host)
            .PerformAction(new[] { first, second });

        Assert.AreEqual(2, host.Requests.Count);
    }

    // ── Zip it ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The name is picked synchronously but the write is now seconds away, so it has to be claimed
    /// before the action returns. Without that, two Zip Its in quick succession both pick it and the
    /// second silently replaces the first.
    /// </summary>
    [TestMethod]
    [CoversNode("compressed-zip-it")]
    public async Task ZipItClaimsItsNameBeforeItReturns()
    {
        var folder = Folder("reports", "one.txt", "two.txt");
        var host   = new RecordingHost();
        var dest   = Path.Combine(_root, "reports.zip");

        Assert.IsTrue(new ZipItAction(Substitute.For<IShellServices>(), new CompressedConfig(), host)
            .PerformAction(folder));

        Assert.IsTrue(File.Exists(dest), "the name is taken the moment the action returns");
        Assert.AreEqual("Compressing", host.Requests[0].Verb);

        await host.Completion;

        Assert.IsTrue(new FileInfo(dest).Length > 0, "and the real archive replaces the placeholder");
        var back = Path.Combine(_root, "back");
        VirtualFileSystem.Instance.ExtractAll(dest, back);
        CollectionAssert.AreEquivalent(new[] { "one.txt", "two.txt" },
            Directory.GetFiles(back).Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    [CoversNode("compressed-zip-it")]
    public async Task StoppingAZipLeavesNoPlaceholderAndNoTemp()
    {
        var folder = Folder("doomed", "one.txt", "two.txt");
        var host   = new RecordingHost();
        host.Cts.Cancel();

        Assert.IsTrue(new ZipItAction(Substitute.For<IShellServices>(), new CompressedConfig(), host)
            .PerformAction(folder));

        await host.Completion;

        Assert.IsFalse(host.Results[0].Completed, "a stopped run says so rather than throwing");
        Assert.IsFalse(File.Exists(Path.Combine(_root, "doomed.zip")),
            "the claimed name is given back when the run it was claimed for does not happen");
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.nexatmp").Length);
    }

    [TestMethod]
    [CoversNode("compressed-zip-it")]
    public void ZipItWithNoHostStillZipsThereAndThen()
    {
        var folder = Folder("sync", "one.txt");

        Assert.IsTrue(new ZipItAction(Substitute.For<IShellServices>(), new CompressedConfig())
            .PerformAction(folder));

        Assert.IsTrue(File.Exists(Path.Combine(_root, "sync.zip")));
    }

    /// <summary>
    /// Both actions used to ask the strip to refresh the moment they returned. Now that they return
    /// before the work starts, that refresh would provably show nothing — the queue refreshes when the
    /// operation actually finishes.
    /// </summary>
    [TestMethod]
    [CoversNode("compressed-zip-it")]
    [CoversNode("compressed-unzip-here")]
    public void NeitherActionAsksForARefreshItCannotHaveEarned()
    {
        var shell = Substitute.For<IShellServices>();
        Assert.IsFalse(new ZipItAction(shell, new CompressedConfig()).RequiresRefresh);
        Assert.IsFalse(new UnzipHereAction(shell).RequiresRefresh);
    }

    /// <summary>Runs the work as soon as it is handed over and keeps what it said, so a test can await
    /// the operation an action deliberately does not wait for.</summary>
    private sealed class RecordingHost : IFileOperationHost
    {
        public List<FileOperationRequest> Requests { get; } = [];
        public List<TransferProgress> Reports { get; } = [];
        public List<TransferResult> Results { get; } = [];
        public CancellationTokenSource Cts { get; } = new();
        public Task Completion { get; private set; } = Task.CompletedTask;

        public Task Run(FileOperationRequest request,
                        Func<IProgress<TransferProgress>, CancellationToken, Task<TransferResult>> work)
        {
            Requests.Add(request);
            return Completion = Execute(work);
        }

        private async Task Execute(Func<IProgress<TransferProgress>, CancellationToken, Task<TransferResult>> work)
            => Results.Add(await work(new Sink(Reports), Cts.Token));

        private sealed class Sink(List<TransferProgress> into) : IProgress<TransferProgress>
        {
            public void Report(TransferProgress value) { lock (into) into.Add(value); }
        }
    }
}
