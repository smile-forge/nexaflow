using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Operations;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using Nexaflow.Features.Compressed.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Features.Common.Viewlets;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// The queue as a host for work it did not plan — an archive being built or unpacked.
/// <para>
/// The point of the seam is that a feature which cannot reference this one still gets the same row,
/// the same cancel button and the same volume gate as a copy. So these tests drive it exactly as
/// another feature would: through <see cref="IFileOperationHost"/>, never through the queue's own type.
/// </para>
/// </summary>
[TestClass]
[CoversNode("file-operations-panel")]
public class FileOperationHostTests
{
    private static (FileOperationQueue Queue, IFileOperationHost Host) Host()
    {
        var queue = FileOperationQueue.For(Substitute.For<IShellServices>().Runs());
        return (queue, queue);
    }

    private static FileOperationRequest Request(string dest = @"C:\somewhere\out")
        => new("Extracting", "photos.zip", "photos", dest);

    private static TransferResult Ok() => new(true, 0, 0, [], [], [], []);

    [TestMethod]
    public async Task WorkHandedToTheHostGetsARowThatSaysWhatItIs()
    {
        var (queue, host) = Host();

        await host.Run(Request(), (_, _) => Task.FromResult(Ok()));

        var op = queue.Operations.Single();
        Assert.AreEqual("Extracting photos.zip to photos", op.Title,
            "the row reads as a sentence, the same way a copy's does");
        Assert.AreEqual(FileOperationState.Completed, op.State);
    }

    [TestMethod]
    public async Task ProgressReportedByTheWorkReachesTheRow()
    {
        var (queue, host) = Host();

        await host.Run(Request(), (progress, _) =>
        {
            progress.Report(new TransferProgress(TransferPhase.Running,
                BytesDone: 512, BytesTotal: 1024, ItemsDone: 1, ItemsTotal: 2,
                CurrentItem: "a.txt", BytesPerSecond: 0, Remaining: null, Paused: null));
            return Task.FromResult(Ok());
        });

        var op = queue.Operations.Single();
        Assert.AreEqual(1024, op.BytesTotal, "the row measures against what the work announced");
        Assert.AreEqual(2, op.ItemsTotal);
    }

    /// <summary>
    /// The work is handed the row's own token, so the panel's cancel button reaches it. Without this
    /// an archive would show a cancel button that did nothing.
    /// </summary>
    [TestMethod]
    public async Task CancellingTheRowCancelsTheWork()
    {
        var (queue, host) = Host();
        var started  = new TaskCompletionSource();
        var observed = false;

        var run = host.Run(Request(), async (_, ct) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { observed = true; }
            return new TransferResult(false, 0, 0, [], [], [], []);
        });

        await started.Task;
        queue.Operations.Single().Cancel();
        await run;

        Assert.IsTrue(observed, "the work must see the row's own token");
        Assert.AreEqual(FileOperationState.Cancelled, queue.Operations.Single().State);
    }

    /// <summary>
    /// A failure the work describes has to land on the row. The alternative — letting it escape — is
    /// what leaves a row spinning for ever, because the shell queue skips its completion callback.
    /// </summary>
    [TestMethod]
    public async Task AFailureTheWorkReportsShowsOnTheRow()
    {
        var (queue, host) = Host();

        await host.Run(Request(), (_, _) => Task.FromResult(
            new TransferResult(true, 0, 0,
                [new TransferItemFailure("a.zip", "extract", 0, "no room")], [], [], [])));

        var op = queue.Operations.Single();
        Assert.AreEqual(FileOperationState.Failed, op.State);
        Assert.IsTrue(op.HasProblems);
        CollectionAssert.Contains(op.Problems.ToList(), "no room");
    }

    /// <summary>Work that throws anyway must still finish its row rather than abandon it.</summary>
    [TestMethod]
    public async Task WorkThatThrowsStillEndsItsRow()
    {
        var (queue, host) = Host();

        await host.Run(Request(), (_, _) => throw new InvalidOperationException("boom"));

        var op = queue.Operations.Single();
        Assert.IsTrue(op.IsFinished, "an unfinished row is a spinner nobody can stop");
        Assert.AreEqual(FileOperationState.Failed, op.State);
        CollectionAssert.Contains(op.Problems.ToList(), "boom");
    }

    /// <summary>
    /// Two operations onto the same disk take turns — interleaving them halves both. The second is
    /// visibly <see cref="FileOperationState.Queued"/> meanwhile, which is what makes the wait
    /// something the user can see rather than take on trust.
    /// </summary>
    [TestMethod]
    public async Task WorkOntoTheSameVolumeQueuesRatherThanInterleaving()
    {
        var (queue, host) = Host();
        var firstRunning = new TaskCompletionSource();
        var letFirstGo   = new TaskCompletionSource();

        var first = host.Run(Request(@"C:\one"), async (_, _) =>
        {
            firstRunning.SetResult();
            await letFirstGo.Task;
            return Ok();
        });

        await firstRunning.Task;
        var second = host.Run(Request(@"C:\two"), (_, _) => Task.FromResult(Ok()));

        var waiting = queue.Operations.Last();
        Assert.AreEqual(FileOperationState.Queued, waiting.State,
            "the same volume gate a copy uses, so an archive cannot jump the queue");

        letFirstGo.SetResult();
        await Task.WhenAll(first, second);
        Assert.IsTrue(queue.Operations.All(o => o.State == FileOperationState.Completed));
    }

    /// <summary>
    /// The whole seam, end to end and without a window: the registry builds an action from another
    /// feature, hands it the progress host, and the action puts a row in the queue instead of doing the
    /// work on the caller's thread.
    /// <para>
    /// This is what the journey can only observe indirectly. If the host is not injected the action still
    /// works — it just blocks — so every filesystem assertion still passes and only the missing panel says
    /// anything is wrong.
    /// </para>
    /// </summary>
    [TestMethod]
    public void TheRegistryHandsAnArchiveActionTheProgressHost()
    {
        var shell = Substitute.For<IShellServices>().Runs();
        shell.DiscoverImplementations<IFileAction>().Returns([typeof(UnzipHereAction)]);
        shell.DiscoverImplementations<IFolderAction>().Returns([]);
        shell.DiscoverImplementations<IFileCreateAction>().Returns([]);
        shell.DiscoverImplementations<IFolderViewlet>().Returns([]);
        shell.DiscoverImplementations<IThisPcItemProvider>().Returns([]);

        var registry = FileSystemFeatureRegistry.For(
            shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>());

        var action = registry.FileActions.OfType<UnzipHereAction>().SingleOrDefault();
        Assert.IsNotNull(action, "the registry did not build the archive action at all");

        var root    = Path.Combine(Path.GetTempPath(), "nexa-di-" + Guid.NewGuid().ToString("N")[..8]);
        var payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "a.txt"), "alpha");

        var archive = Path.Combine(root, "bundle.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(payload, archive);

        try
        {
            Assert.IsTrue(action!.PerformAction(archive));

            // A row in the queue is the proof: with no host the action extracts inline and adds nothing.
            var queue = FileOperationQueue.For(shell);
            Assert.AreEqual(1, queue.Operations.Count,
                "the action ran the extraction itself instead of handing it to the progress host");
            Assert.AreEqual("Extracting", queue.Operations[0].Verb);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}
