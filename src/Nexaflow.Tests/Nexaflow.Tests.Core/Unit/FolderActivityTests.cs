using System;
using System.IO;
using System.Windows.Threading;
using Nexaflow.Core.Models;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The file system is the session's, not a workspace's: a folder being changed is busy in every workspace, and a file several
/// workspaces watch goes on being watched for those still running when one of them ends.
/// </summary>
[TestClass]
[CoversNode("workspaces")]
public class FolderActivityTests
{
    [TestMethod]
    public void AFolderBusyInOneWorkspace_IsBusyInAnother()
    {
        var factory = TestRuntimes.FactoryWith(tasks: null, notify: _ => { });
        IShellServices home   = factory.CreateUnstarted(new Workspace { Name = "Home" }).ShellServices;
        IShellServices videos = factory.CreateUnstarted(new Workspace { Name = "Videos" }).ShellServices;
        var folder = TestTemp.CreateSubdirectory("busy-").FullName;

        using (home.MarkFolderBusy(folder, "Moving…"))
            Assert.AreEqual("Moving…", videos.GetFolderBusyMessage(folder), "one person, one folder: the wait shows wherever it is looked at");

        Assert.IsNull(videos.GetFolderBusyMessage(folder));
    }

    [TestMethod]
    [TestCategory("Desktop")]
    [DoNotParallelize]
    public void AFileTwoWorkspacesWatch_IsStillWatchedForTheOneRunning_WhenTheOtherEnds() => UiThread.Run(() =>
    {
        var factory = TestRuntimes.FactoryWith(tasks: null, notify: _ => { });
        var home    = factory.CreateUnstarted(new Workspace { Name = "Home" });
        var videos  = factory.CreateUnstarted(new Workspace { Name = "Videos" });
        var file    = Path.Combine(TestTemp.CreateSubdirectory("watched-").FullName, "notes.txt");
        File.WriteAllText(file, "first");

        int homeTold = 0, videosTold = 0;
        home.ShellServices.WatchFile(file, () => homeTold++);
        videos.ShellServices.WatchFile(file, () => videosTold++);

        _ = home.DisposeAsync();
        File.WriteAllText(file, "second");
        PumpUntil(() => videosTold > 0);

        Assert.AreEqual(0, homeTold, "an ended workspace's page hears nothing more");
    });

    private static void PumpUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var frame = new DispatcherFrame();
        var check = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
                                        (_, _) => { if (done() || DateTime.UtcNow > deadline) frame.Continue = false; },
                                        Dispatcher.CurrentDispatcher);
        try { if (!done()) Dispatcher.PushFrame(frame); }
        finally { check.Stop(); }
        Assert.IsTrue(done(), "the change did not arrive");
    }
}
