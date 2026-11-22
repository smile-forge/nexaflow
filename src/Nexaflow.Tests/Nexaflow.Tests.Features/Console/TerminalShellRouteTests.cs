using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Common.Terminal;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.Shells;
using Nexaflow.Visuals.Terminal.ViewModels;
using NSubstitute;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// The shell a console runs is the one that reaches its folder. On a location with a shell it is the location's own, handed
/// the folder as the file system names it and spoken to in its dialect: it says where it is (OSC 7) and when a prompt is
/// back (OSC 133), and a <c>cd</c> to it names the location's path. Anywhere else it is the local shell, started where this
/// disk holds the folder — or in the profile when no disk does. A location that cannot be reached says so in the terminal.
/// </summary>
[TestClass]
[CoversNode("console-location-shell")]
public sealed class TerminalShellRouteTests
{
    private const string Esc = "\x1b";
    private const string St  = "\x1b\\";

    private sealed class TestTerminal(TerminalShellRoute route, IShellServices shell)
        : TerminalViewModel(route, shell, Substitute.For<IAIService>())
    {
        protected override IReadOnlyList<TerminalEnvironment> Environments => [];
        protected override string? FindBoundEnvName(string folderPath) => null;
        protected override void PersistFolderBinding(string folderPath, string envName) { }

        public Task Start()
        {
            SetupInitialState(null);
            return ShellStarted;
        }
    }

    private readonly FakeTerminal _distro = new();
    private readonly FakeTerminal _local  = new();
    private readonly IShellServices _shell = Substitute.For<IShellServices>();

    public TerminalShellRouteTests()
        => _shell.RunOnUiAsync(Arg.Any<Action>()).Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });

    private TerminalShellRoute RouteFor(string? folder)
    {
        var files = TerminalFiles.With(_distro);
        return TerminalShellRoute.ForFolder(files, folder, _local, new CmdShellDialect(files));
    }

    private async Task<TestTerminal> StartedOn(string? folder)
    {
        var terminal = new TestTerminal(RouteFor(folder), _shell);
        await terminal.Start();
        return terminal;
    }

    private void DistroSays(string text) => _distro.Emit(Encoding.UTF8.GetBytes(text));

    [TestMethod]
    public async Task AFolderOnALocationWithAShell_StartsThatLocationsShellThere_InItsDialect()
    {
        var folder = $@"{TerminalFiles.Distro}\home\adam";

        var route = RouteFor(folder);
        await new TestTerminal(route, _shell).Start();

        Assert.AreEqual(folder, _distro.Launched!.WorkingDirectory, "the file system's path: the location names its own");
        Assert.IsNull(_local.Launched, "no local shell starts");
        Assert.IsInstanceOfType<PosixShellDialect>(route.Dialect);
    }

    [TestMethod]
    public async Task AFolderOnThisDisk_StartsTheLocalShellThere()
    {
        var terminal = await StartedOn(@"C:\work");

        Assert.AreEqual(@"C:\work", _local.Launched!.WorkingDirectory);
        Assert.AreEqual(@"C:\work", terminal.CurrentPath);
        Assert.IsNull(_distro.Launched);
    }

    [TestMethod]
    public async Task AMountedFolder_StartsTheLocalShellWhereTheDiskHoldsIt()
    {
        await StartedOn($@"{TerminalFiles.Docs}\notes");

        Assert.AreEqual($@"{TerminalFiles.DocsOnDisk}\notes", _local.Launched!.WorkingDirectory);
    }

    [TestMethod]
    public async Task AFolderNoShellReaches_StartsTheLocalShellInTheProfile()
    {
        await StartedOn($@"{TerminalFiles.Nas}\srv");

        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), _local.Launched!.WorkingDirectory);
        Assert.IsNull(_distro.Launched);
    }

    [TestMethod]
    public async Task ALocationThatCannotBeReached_SaysSoInTheTerminal()
    {
        var files = TerminalFiles.With(_distro);
        files.GetTerminalLauncherAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(ValueTask.FromException<ITerminalLauncher?>(
                 new LocationException(LocationError.Disconnected, TerminalFiles.Distro, "Probe is not running.")));
        var route    = TerminalShellRoute.ForFolder(files, $@"{TerminalFiles.Distro}\home", _local, new CmdShellDialect(files));
        var terminal = new TestTerminal(route, _shell);

        await terminal.Start();

        Assert.IsTrue(terminal.Scrollback.Any(line => line.Contains("Probe is not running.")), "the failure is shown where the shell would be");
        Assert.IsNull(_local.Launched, "no other shell stands in for the location's");
    }

    [TestMethod]
    public async Task TheConsoleFollowsTheFolderTheLocationsShellReports_AndKnowsWhenItsPromptIsBack()
    {
        var terminal = await StartedOn($@"{TerminalFiles.Distro}\home\adam");

        DistroSays($"{Esc}]133;C{St}");
        Assert.IsTrue(terminal.IsBusy, "a command runs once its output starts");

        DistroSays($"{Esc}]133;D;0{St}{Esc}]7;file://probe/srv/a%20b/x%3Ay{St}{Esc}]133;A{St}adam@probe:/srv$ {Esc}]133;B{St}");

        Assert.AreEqual($@"{TerminalFiles.Distro}\srv\a b\x%3Ay", terminal.CurrentPath);
        Assert.IsFalse(terminal.IsBusy, "the prompt is back");
    }

    [TestMethod]
    public async Task ReopeningOnAnotherFolder_ChangesToItInTheLocationsOwnWords()
    {
        var terminal = await StartedOn($@"{TerminalFiles.Distro}\home\adam");

        terminal.ApplyParams($@"{TerminalFiles.Distro}\srv\it's here", null);

        StringAssert.Contains(_distro.Written, @"cd -- '/srv/it'\''s here'" + "\r");
    }

    [TestMethod]
    public async Task ReopeningOnAFolderTheShellCannotReach_SendsNothing_AndSaysSo()
    {
        var terminal = await StartedOn($@"{TerminalFiles.Distro}\home\adam");

        terminal.ApplyParams(@"C:\work", null);

        Assert.AreEqual(string.Empty, _distro.Written);
        Assert.AreEqual(1, terminal.Scrollback.Count, "the console says it cannot go there");
    }

    [TestMethod]
    public async Task WordsForTheAssistant_AreReadFromWhereTypingStarted_AndClearedTheLocationsWay()
    {
        var terminal = await StartedOn($@"{TerminalFiles.Distro}\home\adam");
        DistroSays($"{Esc}]133;A{St}adam@probe:~$ {Esc}]133;B{St}why is the disk full");

        terminal.HandleEnter();

        _shell.Received().SubmitAiQuery("why is the disk full");
        Assert.AreEqual("\x15", _distro.Written, "Ctrl+U clears the line — Esc is no clear in readline");
    }
}
