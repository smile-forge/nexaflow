using System.Collections.Generic;
using System.Windows.Input;
using Nexaflow.Features.Common;
using Nexaflow.IO.Terminal;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.ViewModels;
using NSubstitute;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// Up/Down command-history recall in the AI bar while a '>'-prefixed command targets a terminal. Uses a
/// no-op PTY so no real shell is spawned (constructing a terminal VM otherwise launches cmd.exe).
/// </summary>
[TestClass]
[CoversNode("console-bar-history-keys")]
public class TerminalKeyHistoryTests
{
    private sealed class FakePty : PseudoConsoleHostService
    {
        public override void Start(short cols = 220, short rows = 50) { }
        public override void WriteInput(string text) { }
        public override void SendCtrlC() { }
    }

    private sealed class TestTerminal : TerminalViewModel
    {
        public TestTerminal() : base(new FakePty(), Substitute.For<IShellServices>(), Substitute.For<IAIService>()) { }
        protected override IReadOnlyList<TerminalEnvironment> Environments => [];
        protected override string? FindBoundEnvName(string folderPath) => null;
        protected override void PersistFolderBinding(string folderPath, string envName) { }
    }

    [TestMethod]
    public void HandleChatKey_NonConsoleText_NotHandled()
    {
        var vm = new TestTerminal();
        vm.SendCommand("dir");

        var r = vm.HandleChatKey(Key.Up, ModifierKeys.None, "hello", 5);

        Assert.IsFalse(r.Handled, "Up in a plain AI message should not be claimed by the terminal");
    }

    [TestMethod]
    public void HandleChatKey_Up_RecallsMostRecentCommand()
    {
        var vm = new TestTerminal();
        vm.SendCommand("first");
        vm.SendCommand("second");   // most recent

        var r = vm.HandleChatKey(Key.Up, ModifierKeys.None, ">", 1);

        Assert.IsTrue(r.Handled);
        Assert.AreEqual(">second", r.NewText);
    }

    [TestMethod]
    public void HandleChatKey_UpTwice_WalksToOlder()
    {
        var vm = new TestTerminal();
        vm.SendCommand("first");
        vm.SendCommand("second");

        vm.HandleChatKey(Key.Up, ModifierKeys.None, ">", 1);
        var r = vm.HandleChatKey(Key.Up, ModifierKeys.None, ">second", 7);

        Assert.AreEqual(">first", r.NewText);
    }

    [TestMethod]
    public void HandleChatKey_Down_ReturnsToEmptyPrompt()
    {
        var vm = new TestTerminal();
        vm.SendCommand("only");

        vm.HandleChatKey(Key.Up, ModifierKeys.None, ">", 1);            // ">only"
        var r = vm.HandleChatKey(Key.Down, ModifierKeys.None, ">only", 5);

        Assert.IsTrue(r.Handled);
        Assert.AreEqual(">", r.NewText);
    }

    [TestMethod]
    public void HandleChatKey_NewCommand_ResetsHistoryCursor()
    {
        var vm = new TestTerminal();
        vm.SendCommand("a");
        vm.SendCommand("b");
        vm.HandleChatKey(Key.Up, ModifierKeys.None, ">", 1);            // cursor at "b"
        vm.SendCommand("c");                                            // resets; "c" most recent

        var r = vm.HandleChatKey(Key.Up, ModifierKeys.None, ">", 1);

        Assert.AreEqual(">c", r.NewText, "after sending a command, Up should recall it, not resume mid-history");
    }
}
