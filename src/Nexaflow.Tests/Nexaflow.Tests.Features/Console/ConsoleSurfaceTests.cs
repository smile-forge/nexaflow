using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.Interaction;
using Nexaflow.IO.Terminal;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Terminal;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.ViewModels;
using NSubstitute;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// The Console tab's panels and chrome, driven through the terminal view-model with a no-op PTY so no
/// real shell is spawned: the Files listing, the History panel's re-run, the busy indicator, and the
/// dissolve rule that takes an AI banner down the moment the user starts typing again.
/// </summary>
[TestClass]
public class ConsoleSurfaceTests
{
    private sealed class TestTerminal : TerminalViewModel
    {
        public TestTerminal(IShellServices shell)
            : base(new FakeTerminal().AsCmd(), shell, Substitute.For<IAIService>()) { }

        protected override IReadOnlyList<TerminalEnvironment> Environments => [];
        protected override string? FindBoundEnvName(string folderPath) => null;
        protected override void PersistFolderBinding(string folderPath, string envName) { }
    }

    private static TestTerminal Terminal(out IShellServices shell)
    {
        shell = Substitute.For<IShellServices>();
        return new TestTerminal(shell);
    }

    private string _tmp = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tmp = Path.Combine(TestTemp.Root, "nexa-console-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    // ── Files panel ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("console-files-panel")]
    public async Task TheFilesPanelListsFilesFirst_ThenFolders()
    {
        // Explorer puts folders first. This panel does the opposite on purpose: it exists mainly to drag
        // a path onto the console or the AI bar, and that is nearly always a file.
        Directory.CreateDirectory(Path.Combine(_tmp, "zeta-folder"));
        Directory.CreateDirectory(Path.Combine(_tmp, "alpha-folder"));
        File.WriteAllText(Path.Combine(_tmp, "b.txt"), "");
        File.WriteAllText(Path.Combine(_tmp, "a.txt"), "");

        var listed = await TerminalFileList.ListAsync(VirtualFileSystem.Instance, _tmp, path => path, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "a.txt", "b.txt", "alpha-folder", "zeta-folder" },
                                  listed.Select(e => Path.GetFileName(e.FullPath)).ToArray());
        Assert.IsFalse(listed[0].IsDirectory);
        Assert.IsTrue(listed[3].IsDirectory);
    }

    [TestMethod]
    [CoversNode("console-files-panel")]
    public async Task AFolderTheShellCannotReachListsEmpty_RatherThanThrowing()
    {
        // The panel follows a live shell, which can be sitting anywhere — including a path that has just
        // been removed under it, or one it has no rights to.
        var files = VirtualFileSystem.Instance;
        Assert.AreEqual(0, (await TerminalFileList.ListAsync(files, Path.Combine(_tmp, "gone"), p => p, CancellationToken.None)).Count);
        Assert.AreEqual(0, (await TerminalFileList.ListAsync(files, null, p => p, CancellationToken.None)).Count);
        Assert.AreEqual(0, (await TerminalFileList.ListAsync(files, "", p => p, CancellationToken.None)).Count);
    }

    [TestMethod]
    [CoversNode("console-files-panel")]
    public void DoubleClickingAFolderCdsIntoIt_ButAFileIsNotACommand()
    {
        var vm = Terminal(out _);

        vm.NavigateInto(new TerminalFsEntry(@"C:\work\src", "src", @"C:\work\src", isDirectory: true));
        Assert.AreEqual(1, vm.CommandHistory.Count);
        StringAssert.Contains(vm.CommandHistory[0], @"cd /d ""C:\work\src""",
                              "the quoted /d form, so a drive change works and a space in the path survives");

        vm.NavigateInto(new TerminalFsEntry(@"C:\work\notes.txt", "notes.txt", @"C:\work\notes.txt", isDirectory: false));
        Assert.AreEqual(1, vm.CommandHistory.Count, "a file is there to be dragged, not to be run");
    }

    // ── History panel ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("console-history-panel")]
    public void HistoryIsMostRecentFirst()
    {
        var vm = Terminal(out _);

        vm.SendCommand("dir");
        vm.SendCommand("cd ..");
        vm.SendCommand("git status");

        CollectionAssert.AreEqual(new[] { "git status", "cd ..", "dir" }, vm.CommandHistory.ToArray());
    }

    [TestMethod]
    [CoversNode("console-history-panel")]
    public void RerunningACommandMovesItBackToTheTop_RatherThanDuplicatingIt()
    {
        var vm = Terminal(out _);
        vm.SendCommand("dir");
        vm.SendCommand("git status");

        vm.RerunCommand.Execute("dir");

        CollectionAssert.AreEqual(new[] { "dir", "git status" }, vm.CommandHistory.ToArray(),
                                  "a re-run is the same command, so the list stays a set of distinct ones");
    }

    [TestMethod]
    [CoversNode("console-history-panel")]
    public void ABlankCommandIsNotRecorded()
    {
        var vm = Terminal(out _);

        vm.SendCommand("   ");

        Assert.AreEqual(0, vm.CommandHistory.Count);
    }

    // ── Path bar / busy indicator ─────────────────────────────────────────────

    [TestMethod]
    [CoversNode("console-path-bar")]
    public void TheBusyIndicatorLightsWhenACommandIsSent_AndCtrlCClearsIt()
    {
        var vm = Terminal(out _);
        Assert.IsFalse(vm.IsBusy);

        vm.SendCommand("ping localhost -n 30");
        Assert.IsTrue(vm.IsBusy, "the 'running.' indicator is the only sign a long command is still going");

        vm.SendCtrlC();
        Assert.IsFalse(vm.IsBusy, "interrupting is the way out of a hung command — it has to clear the flag");
    }

    // ── Copy / paste menu ─────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("console-copy-paste")]
    public void CtrlCInterruptsTheRunningProgram_ItDoesNotJustCopy()
    {
        // The terminal deliberately breaks the usual Windows meaning of Ctrl-C: at a shell, interrupting is
        // what it has to do. Copying lives on the right-click menu instead.
        var vm = Terminal(out _);
        vm.SendCommand("ping localhost -n 30");

        vm.SendCtrlC();

        Assert.IsFalse(vm.IsBusy);
    }

    // ── Inline AI banner ──────────────────────────────────────────────────────

    // ── Drop onto the AI bar ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("console-bar-drop")]
    public void ADroppedPathWithASpaceIsQuoted()
    {
        var data = new DragPayload((DragKinds.Files, new[] { @"C:\Program Files\app.exe" }));

        Assert.AreEqual(@"""C:\Program Files\app.exe""", TerminalDropLogic.BuildInsertText(data),
                        "unquoted, cmd would read this as two arguments");
    }

    [TestMethod]
    [CoversNode("console-bar-drop")]
    public void SeveralDroppedPathsBecomeOneSpaceSeparatedArgumentList()
    {
        var data = new DragPayload((DragKinds.Files, new[] { @"C:\a.txt", @"C:\my docs\b.txt" }));

        Assert.AreEqual(@"C:\a.txt ""C:\my docs\b.txt""", TerminalDropLogic.BuildInsertText(data),
                        "only the path that needs quoting gets it");
    }

    [TestMethod]
    [CoversNode("console-bar-drop")]
    [DataRow("x&echo.INJECTED")]
    [DataRow("x&echo INJECTED (1).txt")]
    [DataRow("%OS%.txt")]
    [DataRow("%OS%%OS% 100%.txt")]
    [DataRow("50% off.txt")]
    [DataRow("a^b.txt")]
    [DataRow("a,b;c=d.txt")]
    public void ADroppedName_MeansOnlyItselfToCmd(string name)
    {
        var path = Path.Combine(_tmp, name);
        File.WriteAllText(path, "READ-IT");
        var typed = TerminalDropLogic.BuildInsertText(new DragPayload((DragKinds.Files, new[] { path })));

        using var cmd = Process.Start(new ProcessStartInfo("cmd.exe", "/d /q")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _tmp,
        })!;
        cmd.StandardInput.WriteLine($"type {typed}");
        cmd.StandardInput.Close();
        var said = cmd.StandardOutput.ReadToEnd();
        cmd.WaitForExit();

        StringAssert.Contains(said, "READ-IT", typed);
        Assert.IsFalse(said.Contains("INJECTED", StringComparison.Ordinal), $"the name ran: {typed}");
    }

    [TestMethod]
    [CoversNode("console-bar-drop")]
    [DataRow("C:\\a.txt\rcalc")]
    [DataRow("C:\\a.txt\ncalc")]
    [DataRow("C:\\a\"&calc")]
    public void APathThatWouldPressEnterOrEndItsQuotesIsNotTyped(string path)
    {
        Assert.IsNull(TerminalDropLogic.BuildInsertText(new DragPayload((DragKinds.Files, new[] { path }))));
    }

    [TestMethod]
    [CoversNode("console-bar-drop")]
    public void DroppingSomethingThatIsNotAFileInsertsNothing()
    {
        Assert.IsNull(TerminalDropLogic.BuildInsertText(new DragPayload((DragKinds.Text, "hello"))));
    }
}
