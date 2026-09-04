using System;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using System.Threading.Tasks;
using Nexaflow.Features.WindowsFileSystem.Operations;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// Delete — the one action on the strip that cannot be undone by clicking it again.
/// <para>
/// The test here is the gate, not the effect. A normal delete does nothing itself: it raises the
/// confirmation and hands the actual recycle to the confirm callback, so declining has to leave the file
/// exactly where it was and reach nothing. Shift-delete deliberately skips that gate and deletes
/// permanently — that one is asserted end to end on a real temporary file, because "it skipped the
/// confirmation" and "it deleted the file" are two separate claims and both need to be true.
/// </para>
/// </summary>
[TestClass]
[CoversNode("winfs-act-delete")]
public class DeleteActionTests
{
    private string _scratch = string.Empty;

    [TestInitialize]
    public void CreateScratch()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "nexa-del-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
    }

    [TestCleanup]
    public void RemoveScratch() { try { Directory.Delete(_scratch, recursive: true); } catch { } }

    private string File_(string name)
    {
        var p = Path.Combine(_scratch, name);
        File.WriteAllText(p, "content");
        return p;
    }

    // ── The gate ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void ANormalDeleteAsksFirst_AndDoesNothingUntilItIsAnswered()
    {
        var shell = Substitute.For<IShellServices>();   // never runs either callback
        var file = File_("keep.txt");

        var acted = new DeleteFile(shell).PerformAction(file);

        Assert.IsFalse(acted, "the delete is deferred to the confirmation, so nothing has happened yet");
        Assert.IsTrue(File.Exists(file));
        shell.ReceivedWithAnyArgs().ShowConfirmation(default!, default!, default!, default!);
        shell.DidNotReceive().RequestRefresh();
    }

    [TestMethod]
    public void DecliningTheConfirmationLeavesTheFileAlone()
    {
        var shell = Substitute.For<IShellServices>();
        shell.When(s => s.ShowConfirmation(Arg.Any<string>(), Arg.Any<string>(),
                                           Arg.Any<Action>(), Arg.Any<Action>()))
             .Do(ci => ((Action)ci[3]).Invoke());       // the cancel callback
        var file = File_("keep.txt");

        new DeleteFile(shell).PerformAction(file);

        Assert.IsTrue(File.Exists(file));
        shell.DidNotReceive().RequestRefresh();
    }

    [TestMethod]
    public void TheConfirmationNamesOneFile_ButCountsASelection()
    {
        var shell = Substitute.For<IShellServices>();
        var messages = new System.Collections.Generic.List<string>();
        shell.When(s => s.ShowConfirmation(Arg.Any<string>(), Arg.Any<string>(),
                                           Arg.Any<Action>(), Arg.Any<Action>()))
             .Do(ci => messages.Add(ci.ArgAt<string>(1)));

        new DeleteFile(shell).PerformAction(File_("budget.xlsx"));
        new DeleteFile(shell).PerformAction([File_("a.txt"), File_("b.txt"), File_("c.txt")]);

        StringAssert.Contains(messages[0], "budget.xlsx",
                              "with one file the prompt should say which one — that is the whole check");
        StringAssert.Contains(messages[1], "3 items");
    }

    [TestMethod]
    public void DeletingNothingAsksNothing()
    {
        var shell = Substitute.For<IShellServices>();

        Assert.IsFalse(new DeleteFile(shell).PerformAction([]));

        shell.DidNotReceiveWithAnyArgs().ShowConfirmation(default!, default!, default!, default!);
    }

    // ── Shift: past the gate ──────────────────────────────────────────────────

    [TestMethod]
    public async Task ShiftDeleteSkipsTheConfirmation_AndTheFileIsGoneForGood()
    {
    var shell = Substitute.For<IShellServices>().Runs();
    var file = File_("gone.txt");

    var acted = new DeleteFile(shell).PerformAction(file, force: true);

    Assert.IsTrue(acted);
    await FileOperationQueue.For(shell).Operations[^1].Completion;
    Assert.IsFalse(File.Exists(file), "a forced delete is permanent — not the Recycle Bin");
        shell.DidNotReceiveWithAnyArgs().ShowConfirmation(default!, default!, default!, default!);
        shell.Received().RequestRefresh();
    }

    // ── Declared shape ────────────────────────────────────────────────────────

    [TestMethod]
    public void ItDeclaresItselfDestructive_WhichIsWhatMakesTheButtonRed()
    {
        var action = new DeleteFile(Substitute.For<IShellServices>());

        Assert.IsTrue(action.IsDestructive);
        Assert.IsFalse(action.AppliesToRoot, "no Delete button over a drive root");
        Assert.IsFalse(action.AppliesToDrives);
    }
}
