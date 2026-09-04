using System;
using System.IO;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Operations;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// The panel above the folder tree, and specifically when it is allowed to appear.
/// <para>
/// The debounce is the whole design: a drop that finishes in 80 ms must never make the tree jump, and
/// one still going after <see cref="FileOperationsPanelViewModel.ExpandDelayMs"/> must say so rather
/// than leaving the window looking hung — which is what it did.
/// </para>
/// These run off any UI thread, which is why the wait is a <c>Task.Delay</c> and a marshal rather
/// than a <c>DispatcherTimer</c>.
/// </summary>
[TestClass]
[DoNotParallelize]   // FileTransferEngine.FreeSpaceProbe is a static seam
[CoversNode("winfs-drag-drop")]
public class FileOperationsPanelTests
{
    private string _scratch = string.Empty;

    [TestInitialize]
    public void CreateScratch()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "nexa-panel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
    }

    [TestCleanup]
    public void RemoveScratch()
    {
        FileTransferEngine.FreeSpaceProbe = null;
        try { Directory.Delete(_scratch, recursive: true); } catch { }
    }

    private string Folder(string name, params string[] files)
    {
        var dir = Path.Combine(_scratch, name);
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "x");
        return dir;
    }

    private static (FileOperationQueue Queue, FileOperationsPanelViewModel Panel) Panel()
    {
        var shell = Substitute.For<IShellServices>().Runs();
        var queue = FileOperationQueue.For(shell);
        return (queue, new FileOperationsPanelViewModel(queue, shell));
    }

    [TestMethod]
    public void WithNothingHappeningThePanelIsNotThere()
    {
        var (_, panel) = Panel();
        panel.Attach();

        Assert.IsFalse(panel.IsVisible);
    }

    [TestMethod]
    public async Task AnOperationStillGoingAfterTheDelayOpensThePanel()
    {
        var src  = Folder("src", "a.txt");
        var dest = Folder("dest");

        // Parks the run on "out of space" and leaves it there, so it is reliably still going.
        FileTransferEngine.FreeSpaceProbe = _ => 0;

        var (queue, panel) = Panel();
        panel.Attach();

        var op = queue.EnqueueDrop([src], dest, move: false);
        Assert.IsFalse(panel.IsVisible, "not immediately — a fast operation must not make the tree jump");

        await Task.Delay(FileOperationsPanelViewModel.ExpandDelayMs + 400);
        Assert.IsTrue(panel.IsVisible, "work that is still going has to say so");

        op!.Cancel();
        await op.Completion;
        panel.Detach();
    }

    [TestMethod]
    public async Task AnOperationThatFinishesQuicklyNeverOpensThePanel()
    {
        var src  = Folder("src", "a.txt");
        var dest = Folder("dest");

        var (queue, panel) = Panel();
        panel.Attach();

        var op = queue.EnqueueDrop([src], dest, move: false);
        await op!.Completion;

        await Task.Delay(FileOperationsPanelViewModel.ExpandDelayMs + 400);
        Assert.IsFalse(panel.IsVisible, "it was over before the panel was due to appear");
        panel.Detach();
    }

    [TestMethod]
    public async Task DetachingAbandonsAPendingOpen()
    {
        var src  = Folder("src", "a.txt");
        var dest = Folder("dest");
        FileTransferEngine.FreeSpaceProbe = _ => 0;

        var (queue, panel) = Panel();
        panel.Attach();

        var op = queue.EnqueueDrop([src], dest, move: false);
        panel.Detach();   // the tab closed while the expand was pending

        await Task.Delay(FileOperationsPanelViewModel.ExpandDelayMs + 400);
        Assert.IsFalse(panel.IsVisible);

        op!.Cancel();
        await op.Completion;
    }

    [TestMethod]
    public async Task ThePanelSaysHowMuchIsWaitingOnWhat()
    {
        var one  = Folder("one",  "a.txt");
        var two  = Folder("two",  "b.txt");
        var dest = Folder("dest");
        FileTransferEngine.FreeSpaceProbe = _ => 0;

        var (queue, panel) = Panel();
        panel.Attach();

        var first  = queue.EnqueueDrop([one], dest, move: false);
        var second = queue.EnqueueDrop([two], dest, move: false);

        await Task.Delay(300);

        // Same volume, so the second is behind the first rather than fighting it for the disk.
        StringAssert.Contains(panel.Summary, "waiting");

        first!.Cancel();
        second!.Cancel();
        await Task.WhenAll(first.Completion, second.Completion);
        panel.Detach();
    }

    /// <summary>
    /// The bug this exists for: a running copy reports progress several times a second, and the debounce
    /// restarted its countdown on every one of those reports, so it never elapsed. The panel only ever
    /// appeared for an operation that had gone quiet — precisely the one not worth showing — and in the
    /// app it therefore never appeared at all.
    /// </summary>
    [TestMethod]
    public async Task ASteadyStreamOfProgressDoesNotKeepResettingTheCountdown()
    {
        var src  = Folder("src", "a.txt");
        var dest = Folder("dest");
        FileTransferEngine.FreeSpaceProbe = _ => 0;   // parks the operation so it stays busy throughout

        var (queue, panel) = Panel();
        panel.Attach();

        var op = queue.EnqueueDrop([src], dest, move: false);

        var reports = Task.Run(async () =>
        {
            for (var i = 0; i < 25; i++)
            {
                queue.PublishOnUi(() => { });
                await Task.Delay(100);
            }
        });

        await Task.Delay(FileOperationsPanelViewModel.ExpandDelayMs + 900);
        Assert.IsTrue(panel.IsVisible, "the panel has to appear while progress is still arriving");

        op!.Cancel();
        await op.Completion;
        await reports;
        panel.Detach();
    }

    /// <summary>
    /// A copy belongs to the workspace, not to the tab that happened to start it. Every file browser in
    /// the same runtime shares one queue, so opening a second one mid-copy shows the same work rather
    /// than an empty panel.
    /// </summary>
    [TestMethod]
    public async Task EveryFileBrowserInTheWorkspaceShowsTheSameCopies()
    {
        var src  = Folder("src", "a.txt");
        var dest = Folder("dest");
        FileTransferEngine.FreeSpaceProbe = _ => 0;   // parks it so both panels have time to notice

        var shell = Substitute.For<IShellServices>().Runs();
        var queue = FileOperationQueue.For(shell);

        var started = new FileOperationsPanelViewModel(queue, shell);
        started.Attach();

        var op = queue.EnqueueDrop([src], dest, move: false);

        // The second browser opens after the copy is already under way.
        var opened = new FileOperationsPanelViewModel(queue, shell);
        opened.Attach();

        await Task.Delay(FileOperationsPanelViewModel.ExpandDelayMs + 700);

        Assert.AreSame(started.Operations, opened.Operations, "both browsers read the one queue");
        CollectionAssert.Contains(opened.Operations, op, "the second browser lists a copy it did not start");
        Assert.IsTrue(started.IsVisible, "the browser that started it shows the panel");
        Assert.IsTrue(opened.IsVisible, "and so does one opened while it was already running");

        op!.Cancel();
        await op.Completion;
        started.Detach();
        opened.Detach();
    }
}
