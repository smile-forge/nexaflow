using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Interaction;
using Nexaflow.Features.Common;
using Nexaflow.Features.Wsl.ChatHandlers;
using Nexaflow.Features.Wsl.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Common.Terminal;
using Nexaflow.Tests.Features.Console;
using Nexaflow.Tests.Fixtures;
using Nexaflow.IO.Terminal;
using Nexaflow.IO.Terminal.Shells;
using NSubstitute;

namespace Nexaflow.Tests.Features.Wsl;

/// <summary>
/// A WSL distro's shell describes the distro, not this computer: its folder as Linux names it, the folder's files listed
/// through the distro, its own variables — live at each prompt, or as a new shell starts — and a file dropped on it named
/// as the distro names it. An edit goes into the shell, or into its startup file between Nexaflow's own marks.
/// </summary>
[TestClass]
public sealed class WslShellTerminalTests
{
    private const string Esc = "\x1b";
    private const string St  = "\x1b\\";
    private const string Home = $@"{TerminalFiles.Distro}\home\adam";

    /// <summary>A distro's shell's variables as its host would read them.</summary>
    private sealed class FakeEnvironment : IShellEnvironment
    {
        public Dictionary<string, string> Live { get; } = new(StringComparer.Ordinal) { ["EDITOR"] = "vim" };
        public Dictionary<string, string> Startup { get; } = new(StringComparer.Ordinal) { ["HOME"] = "/home/adam" };
        public string? StartupFile { get; set; } = "/home/adam/.profile";
        public int StartupReads { get; private set; }

        public IReadOnlyList<ShellEnvironmentView> Views { get; } = [ShellEnvironmentView.Live, ShellEnvironmentView.Startup];

        public ValueTask<IReadOnlyDictionary<string, string>?> ReadAsync(ShellEnvironmentView view, CancellationToken ct)
        {
            if (view == ShellEnvironmentView.Startup) StartupReads++;
            var read = new Dictionary<string, string>(view == ShellEnvironmentView.Live ? Live : Startup, StringComparer.Ordinal);
            return ValueTask.FromResult<IReadOnlyDictionary<string, string>?>(read);
        }

        public ValueTask<string?> FindStartupFileAsync(CancellationToken ct) => ValueTask.FromResult(StartupFile);
    }

    private readonly FakeEnvironment _environment = new();
    private readonly FakeTerminal _distro;
    private readonly IShellServices _shell = Substitute.For<IShellServices>();
    private readonly IVirtualFileSystem _files;

    public WslShellTerminalTests()
    {
        _distro = new FakeTerminal { ShellEnvironment = _environment };
        _files  = TerminalFiles.With(_distro);
        _files.GetDisplayName(Arg.Any<string>())
              .Returns(ci => LocationPath.DecodeSegment(ci.Arg<string>()[(ci.Arg<string>().LastIndexOf('\\') + 1)..]));
        _shell.RunOnUiAsync(Arg.Any<Action>()).Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
    }

    private async Task<WslShellViewModel> StartedOn(string folder)
    {
        var terminal = new WslShellViewModel(TerminalShellRoute.ForLocationShell(_files, folder), _shell, Substitute.For<IAIService>());
        await terminal.ShellStarted;
        return terminal;
    }

    private void PromptReturns() => _distro.Emit(Encoding.UTF8.GetBytes($"{Esc}]133;D;0{St}{Esc}]133;A{St}$ {Esc}]133;B{St}"));

    private static async IAsyncEnumerable<LocationEntry> Listed(params LocationEntry[] entries)
    {
        await Task.CompletedTask;
        foreach (var entry in entries) yield return entry;
    }

    [TestMethod]
    [CoversNode("wslshell-shell")]
    public async Task ItRunsTheDistrosOwnShell_InTheFolder()
    {
        await StartedOn(Home);

        Assert.AreEqual(Home, _distro.Launched!.WorkingDirectory, "the file system's path: the location names its own");
    }

    [TestMethod]
    [CoversNode("wslshell-path-bar")]
    public async Task TheTopBar_ShowsTheDistrosFolderAsLinuxNamesIt()
    {
        var terminal = await StartedOn(Home);
        Assert.AreEqual("/home/adam", terminal.ShownPath);

        _distro.WorkingDirectory = "/srv/a b/x:y";
        PromptReturns();

        Assert.AreEqual("/srv/a b/x:y", terminal.ShownPath, "the host's name for it, not the file system's");
    }

    [TestMethod]
    [CoversNode("wslshell-files-panel")]
    public async Task TheFilesPanel_ListsTheDistrosFolderThroughTheDistro_NamedAsLinuxNamesThem()
    {
        _files.EnumerateAsync(Home, Arg.Any<CancellationToken>())
              .Returns(Listed(new LocationEntry("src", true, 0, DateTime.Now), new LocationEntry("at 12%3A30.txt", false, 4, DateTime.Now)));

        var terminal = await StartedOn(Home);

        CollectionAssert.AreEqual(new[] { "at 12:30.txt", "src" }, terminal.Files.Select(f => f.Name).ToArray(), "files first, then folders");
        Assert.AreEqual("/home/adam/at 12:30.txt", terminal.Files[0].ShownPath);
        Assert.AreEqual($@"{Home}\at 12%3A30.txt", terminal.Files[0].FullPath, "the file system's own path, for what opens it");
    }

    [TestMethod]
    [CoversNode("wslshell-env-panel")]
    public async Task TheEnvironmentPanel_ListsTheDistrosVariables_NotThisComputers()
    {
        var terminal = await StartedOn(Home);

        CollectionAssert.AreEqual(new[] { "EDITOR" }, terminal.EnvVars.Select(v => v.Name).ToArray());
        Assert.IsTrue(terminal.SwitchesEnvironment, "live and a new shell's are both offered");
        CollectionAssert.AreEqual(new[] { EnvScope.Live, EnvScope.Startup }, terminal.EnvScopes.ToArray(), "not Windows' user and machine");
    }

    [TestMethod]
    [CoversNode("wslshell-env-panel")]
    public async Task TheLiveListIsReadAtEachPrompt_ANewShellsOnlyWhenShown()
    {
        var terminal = await StartedOn(Home);
        _environment.Live["EDITOR"] = "nano";

        PromptReturns();
        Assert.AreEqual("nano", terminal.EnvVars.Single().Value, "an export at the last prompt shows");

        terminal.ListsStartupEnvironment = true;
        CollectionAssert.AreEqual(new[] { "HOME" }, terminal.EnvVars.Select(v => v.Name).ToArray());
        Assert.AreEqual(1, _environment.StartupReads);

        PromptReturns();
        Assert.AreEqual(1, _environment.StartupReads, "a new shell is not started at every prompt");
        Assert.IsFalse(terminal.ListsLiveEnvironment);
    }

    [TestMethod]
    [CoversNode("wslshell-env-edit")]
    public async Task ALiveVariable_IsExportedInTheShell_AndANameItCannotTake_IsRefused()
    {
        var terminal = await StartedOn(Home);

        Assert.IsTrue(await terminal.WriteEnvVarAsync(EnvScope.Live, "GREETING", "it's"));
        StringAssert.Contains(_distro.Written, @"export GREETING='it'\''s'");

        Assert.IsFalse(await terminal.WriteEnvVarAsync(EnvScope.Live, "1-bad", "x"));
        _shell.ReceivedWithAnyArgs().ShowError(default!);
        Assert.IsFalse(_distro.Written.Contains("1-bad"), "nothing is typed for it");
    }

    [TestMethod]
    [CoversNode("wslshell-env-edit")]
    public async Task AVariableKeptForNewShells_GoesBetweenNexaflowsMarks_InTheDistrosStartupFile()
    {
        const string profile = $@"{TerminalFiles.Distro}\home\adam\.profile";
        _files.ExistsAsync(profile, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(true));
        _files.ReadAllTextAsync(profile, Arg.Any<Encoding?>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult("umask 022\n"));
        var terminal = await StartedOn(Home);
        terminal.ListsStartupEnvironment = true;

        Assert.IsTrue(await terminal.WriteEnvVarAsync(EnvScope.Startup, "EDITOR", "vim"));

        await _files.Received().WriteAllTextAsync(profile,
            "umask 022\n\n# >>> Nexaflow: variables set from its Environment panel >>>\nexport EDITOR='vim'\n# <<< Nexaflow <<<\n",
            Arg.Any<Encoding?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(2, _environment.StartupReads, "a new shell's list is read again");
    }

    [TestMethod]
    [CoversNode("wslshell-bar-drop")]
    public async Task AFileDropped_IsGivenByItsOwnLinuxPath_HoweverItWasNamed()
    {
        _files.TryToVirtual(Arg.Any<string>()).Returns((string?)null);   // a path no location's folder holds
        _files.TryToVirtual(@"\\wsl.localhost\Probe\home\adam\a b.txt").Returns($@"{Home}\a b.txt");
        var terminal = await StartedOn(Home);
        var data = new DragPayload((DragKinds.Files, new[] { @"\\wsl.localhost\Probe\home\adam\a b.txt", $@"{Home}\it's.txt" }));

        const string words = @"'/home/adam/a b.txt' '/home/adam/it'\''s.txt'";
        Assert.AreEqual(words, terminal.FormatDroppedPaths(data), "on the console");
        Assert.AreEqual(words, new WslShellDropHandler().BuildInsertText(data, terminal), "on its AI bar");
        Assert.IsTrue(new WslShellDropHandler().CanHandle(terminal));
    }

    [TestMethod]
    [CoversNode("wslshell-bar-routing")]
    public async Task TheAiBar_RunsACommandInTheDistrosShell_AndNoOtherTerminals()
    {
        var terminal = await StartedOn(Home);

        Assert.IsTrue(new WslShellQueryHandler().CanProcess("ls -la", false, terminal) > 0.8f);
        Assert.IsTrue(new WslShellKeyHandler().CanHandle(terminal));
        Assert.AreEqual(0f, new WslShellQueryHandler().CanProcess("dir", true, Substitute.For<IPageViewModel>()));
        Assert.IsFalse(new WslShellDropHandler().CanHandle(Substitute.For<IPageViewModel>()), "another page's drop is not this feature's");
    }
}

/// <summary>The startup file a POSIX shell reads, with a variable kept for new shells: the user's own lines stand, and
/// Nexaflow's go between its own marks, changed in place rather than added twice.</summary>
[TestClass]
[CoversNode("wslshell-env-edit")]
public sealed class PosixStartupFileTests
{
    private const string Start = "# >>> Nexaflow: variables set from its Environment panel >>>";
    private const string End   = "# <<< Nexaflow <<<";

    private static readonly PosixShellDialect Shell = new("::wsl-probe", "Probe", StringComparison.Ordinal);

    [TestMethod]
    public void AnEmptyFile_GetsTheMarksAndTheLine()
        => Assert.AreEqual($"{Start}\nexport A='1'\n{End}\n", Shell.KeepInStartupFile("", "A", "1"));

    [TestMethod]
    public void TheUsersLines_StandAsWritten_WithTheMarksAfterThem()
        => Assert.AreEqual($"alias ll='ls -l'\n\n{Start}\nexport A='1'\n{End}\n", Shell.KeepInStartupFile("alias ll='ls -l'", "A", "1"));

    [TestMethod]
    public void AKeptVariable_IsChangedInPlace_AndANewOneAddedBesideIt()
    {
        var once  = Shell.KeepInStartupFile("export A='mine'\n", "A", "1")!;
        var again = Shell.KeepInStartupFile(Shell.KeepInStartupFile(once, "A", "2")!, "B", "3");

        Assert.AreEqual($"export A='mine'\n\n{Start}\nexport A='2'\nexport B='3'\n{End}\n", again,
                        "the user's own export of A is theirs; only Nexaflow's line changes");
    }

    [TestMethod]
    public void AValueOnMoreThanOneLine_OrANameNoShellTakes_IsNotKept()
    {
        Assert.IsNull(Shell.KeepInStartupFile("", "A", "one\ntwo"));
        Assert.IsNull(Shell.KeepInStartupFile("", "A-B", "1"));
        Assert.IsNull(new CmdShellDialect().KeepInStartupFile("", "A", "1"), "cmd keeps no startup file");
    }
}
