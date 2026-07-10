using System.Collections.Generic;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Console.ChatHandlers;
using Nexaflow.IO.Terminal;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.ViewModels;
using NSubstitute;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// The bar→console command handler. It must claim a recognised command on a terminal page, but never a
/// natural-language query (the PR#95 regression — the old handler hijacked every query while a console
/// was open), and never anything off a terminal.
/// </summary>
[TestClass]
[CoversNode("console-gt-prefix")]
public class ConsoleQueryHandlerTests
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
    public void Symbol_IsAngleBracket()
        => Assert.AreEqual(">", new ConsoleQueryHandler().Symbol);

    [TestMethod]
    public void CanProcess_RecognisedCommand_OnTerminal_HighScore()
    {
        var score = new ConsoleQueryHandler().CanProcess("dir", new TestTerminal());
        Assert.IsTrue(score > 0.8f, $"a recognised command should be a clear winner, got {score}");
    }

    [TestMethod]
    public void CanProcess_NaturalLanguage_OnTerminal_Zero()
    {
        // PR#95 regression: a plain question must fall through to the AI, not be run as a command.
        var score = new ConsoleQueryHandler().CanProcess("what does this folder contain", new TestTerminal());
        Assert.AreEqual(0f, score);
    }

    [TestMethod]
    public void CanProcess_OffTerminalPage_Zero()
    {
        var score = new ConsoleQueryHandler().CanProcess("dir", Substitute.For<IPageViewModel>());
        Assert.AreEqual(0f, score);
    }

    [TestMethod]
    public async Task ProcessAsync_RunsCommandSilently()
    {
        var vm = new TestTerminal();

        var result = await new ConsoleQueryHandler().ProcessAsync("dir", vm);

        Assert.IsNull(result, "running a command in the terminal produces no AI message");
        Assert.AreEqual("dir", vm.CommandHistory[0], "the command should have been sent to the terminal");
    }
}
