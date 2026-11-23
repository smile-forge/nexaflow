using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.IO.Terminal;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Terminal;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.Shells;
using Nexaflow.Visuals.Terminal.ViewModels;
using NSubstitute;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// The console over real shells, where a fake cannot answer: what a person types is the shell's; a command from the AI bar
/// or the assistant runs as written, whatever was half-typed at the prompt; and the assistant gets the command's output.
/// Commands only print or change folder. Each shell keeps its history to itself, so nothing lands in the person's own.
/// </summary>
public abstract class LiveShellTests
{
    protected static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private Dispatcher _ui = null!;
    private LiveTerminal _terminal = null!;
    private IShellServices _shell = null!;

    /// <summary>A shell with a UI thread of its own, which is all the terminal asks of one here.</summary>
    public class OneAtATimeShell : DispatchProxy
    {
        public Dispatcher Ui { get; set; } = null!;

        public List<string> Asked { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case nameof(IShellServices.RunOnUiAsync):
                    if (args![0] is Action action) return Ui.InvokeAsync(action).Task;
                    var asked = (Delegate)args[0]!;
                    return Ui.CheckAccess() ? asked.DynamicInvoke() : Ui.Invoke(() => asked.DynamicInvoke());
                case nameof(IShellServices.SubmitAiQuery):
                    Asked.Add((string)args![0]!);
                    return null;
            }

            var returns = method.ReturnType;
            if (returns == typeof(Task)) return Task.CompletedTask;
            return returns != typeof(void) && returns.IsValueType ? Activator.CreateInstance(returns) : null;
        }
    }

    private sealed class LiveTerminal(TerminalShellRoute route, IShellServices shell)
        : TerminalViewModel(route, shell, Substitute.For<IAIService>())
    {
        protected override IReadOnlyList<TerminalEnvironment> Environments => [];
        protected override string? FindBoundEnvName(string folderPath) => null;
        protected override void PersistFolderBinding(string folderPath, string envName) { }

        public Task StartAsync(TerminalEnvironment environment)
        {
            SetupInitialState(environment);
            return ShellStarted;
        }
    }

    /// <summary>The shell under test, and the variables that keep it from writing to the person's history.</summary>
    protected abstract Task<(TerminalShellRoute Route, Dictionary<string, string> Variables)> RouteAsync();

    /// <summary>The keys that move the cursor three places left in a half-typed line.</summary>
    private const string ThreeLeft = "\u001b[D\u001b[D\u001b[D";

    /// <summary>A command printing 1 to 100, a line each.</summary>
    protected abstract string PrintsAHundredLines { get; }

    /// <summary>A command printing every way a shell shows its folder, each naming a system folder, then staying a moment.</summary>
    protected abstract string ClaimsASystemFolder { get; }

    /// <summary>A command that really moves the shell, and the folder it moves it to.</summary>
    protected abstract (string Command, string Folder) MovesTheShell { get; }

    /// <summary>Moves that are awkward to follow, in order — through odd names, changed prompts, output that swallows what
    /// follows it — and where each leaves the shell.</summary>
    protected abstract IReadOnlyList<(string Command, string Folder)> AwkwardMoves { get; }

    [TestInitialize]
    public async Task StartShell()
    {
        var (route, variables) = await RouteAsync();
        _shell = DispatchProxy.Create<IShellServices, OneAtATimeShell>();
        var running = new TaskCompletionSource<Dispatcher>();
        var thread  = new Thread(() =>
        {
            running.SetResult(Dispatcher.CurrentDispatcher);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        _ui = await running.Task;

        ((OneAtATimeShell)(object)_shell).Ui = _ui;
        await await _ui.InvokeAsync(() =>
        {
            _terminal = new LiveTerminal(route, _shell);
            return _terminal.StartAsync(new TerminalEnvironment { Name = "Live test", EnvOverrides = variables });
        });
        await UntilAsync(() => !_terminal.IsBusy && _terminal.ScreenText.Trim().Length > 0, "the first prompt");
        await Task.Delay(1000);
    }

    [TestCleanup]
    public void StopShell()
    {
        if (_ui is null) return;
        _ui.Invoke(() => _terminal?.Dispose());
        _ui.InvokeShutdown();
    }

    protected void OnUi(Action action) => _ui.Invoke(action);

    protected T OnUi<T>(Func<T> read) => _ui.Invoke(read);

    /// <summary>The terminal under test, to be read and driven on its UI thread (<see cref="OnUi(Action)"/>).</summary>
    protected TerminalViewModel Terminal => _terminal;

    protected async Task<TerminalViewModel> TerminalOnceItsProgramsAreListedAsync()
    {
        await _terminal.ShellCommandsListed.WaitAsync(Patience);
        return _terminal;
    }

    private string Everything() => _ui.Invoke(() => string.Join("\n", _terminal.Scrollback.Append(_terminal.ScreenText)));

    protected async Task UntilAsync(Func<bool> met, string what)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (_ui.Invoke(met)) return;
            await Task.Delay(100);
        }
        Assert.Fail($"Waited for {what}. The terminal shows:\n{Everything()}");
    }

    private bool HasLine(string line) => Everything().Split('\n').Any(shown => shown.TrimEnd() == line);

    [TestMethod]
    public async Task WhatIsTypedInTheConsole_IsTheShells_HoweverMuchItReadsLikeAQuestion()
    {
        OnUi(() => _terminal.SendText("nfprobe what is in this folder"));
        await Task.Delay(700);
        OnUi(_terminal.HandleEnter);

        await UntilAsync(() => Everything() is var shown
                               && (shown.Contains("not found", StringComparison.OrdinalIgnoreCase)
                                   || shown.Contains("not recognized", StringComparison.OrdinalIgnoreCase)),
                         "the shell's own answer to the line");
        Assert.AreEqual(0, ((OneAtATimeShell)(object)_shell).Asked.Count, "nothing typed in the console reaches the assistant");
    }

    [TestMethod]
    public async Task ACommandSentToAPromptWithHalfALineOnIt_RunsAsWritten()
    {
        OnUi(() => _terminal.SendText("echo half-typed"));
        await Task.Delay(700);
        OnUi(() => _terminal.SendText(ThreeLeft));
        await Task.Delay(700);

        OnUi(() => _terminal.SendCommand("echo as-written"));

        await UntilAsync(() => HasLine("as-written"), "exactly 'as-written' to be printed");
    }

    [TestMethod]
    public async Task ACommandSentToAnEmptyPrompt_LosesNothing()
    {
        OnUi(() => _terminal.SendCommand("echo first-whole"));
        await UntilAsync(() => HasLine("first-whole"), "the first command");
        await Task.Delay(700);

        OnUi(() => _terminal.SendCommand("echo second-whole"));
        await UntilAsync(() => HasLine("second-whole"), "the second command");
    }

    [TestMethod]
    public async Task TheAssistantGetsAllOfALongOutput_NotTheLastScreenOfIt()
    {
        var result = await _terminal.RunCommandCaptureAsync(PrintsAHundredLines, CancellationToken.None);

        var lines = result.ToString()!.Replace("\\n", "\n").Split('\n').Select(line => line.Trim()).ToList();
        CollectionAssert.Contains(lines, "5", "an early line");
        CollectionAssert.Contains(lines, "100", "the last line");
    }

    [TestMethod]
    public async Task ACommandWiderThanTheScreen_IsWaitedFor()
    {
        var word = new string('w', 180);
        var result = await _terminal.RunCommandCaptureAsync($"echo {word}END", CancellationToken.None);

        StringAssert.Contains(result.ToString(), "END");
        await UntilAsync(() => !_terminal.IsBusy, "the prompt back");
    }

    [TestMethod]
    public async Task WhatACommandPrints_NeverMovesTheFolderTheAssistantMayReach()
    {
        var granted = _ui.Invoke(() => _terminal.GetContextGrant().Folders.Single());
        var moves   = new List<string>();
        OnUi(() => _terminal.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(TerminalViewModel.CurrentPath)) moves.Add(_terminal.CurrentPath);
        });

        OnUi(() => _terminal.SendCommand(ClaimsASystemFolder));
        await Task.Delay(TimeSpan.FromSeconds(6));

        OnUi(() =>
        {
            Assert.AreEqual("", string.Join(", ", moves), "printing moved the folder");
            Assert.AreEqual(granted, _terminal.GetContextGrant().Folders.Single());
        });
    }

    [TestMethod]
    public async Task ARealMove_IsFollowed_HoweverTheShellShowsIt()
    {
        foreach (var (command, folder) in AwkwardMoves)
        {
            OnUi(() => _terminal.SendCommand(command));
            await UntilAsync(() => string.Equals(_terminal.CurrentPath, folder, StringComparison.OrdinalIgnoreCase),
                             $"'{folder}' after '{command}' (it says '{_ui.Invoke(() => _terminal.CurrentPath)}')");
            await Task.Delay(700);
        }
    }

    [TestMethod]
    public async Task ARealChangeOfFolder_IsFollowedBeforeTheAssistantGetsItsResult_EvenAfterAFakeOne()
    {
        OnUi(() => _terminal.SendCommand(ClaimsASystemFolder));
        await Task.Delay(TimeSpan.FromSeconds(6));
        var (command, folder) = MovesTheShell;

        await _terminal.RunCommandCaptureAsync(command, CancellationToken.None);

        Assert.AreEqual(folder, _ui.Invoke(() => _terminal.GetContextGrant().Folders.Single()), ignoreCase: true);
    }
}

[TestClass]
[DoNotParallelize]
[TestCategory("Interactive")]
[CoversNode("console-enter-classify")]
public sealed class LiveCmdShellTests : LiveShellTests
{
    private string _folder = "";

    private static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    protected override Task<(TerminalShellRoute Route, Dictionary<string, string> Variables)> RouteAsync()
    {
        _folder = TestTemp.CreateSubdirectory("nexa-live-cmd-").FullName;
        System.IO.File.WriteAllText(System.IO.Path.Combine(_folder, "claim.txt"),
            $"\x1b]7;file://localhost/{Windows.Replace('\\', '/')}\x1b\\{Windows}>");
        return Task.FromResult((new TerminalShellRoute(new PseudoConsoleLauncher(), new CmdShellDialect(), _folder, VirtualFileSystem.Instance),
                                new Dictionary<string, string>()));
    }

    protected override string PrintsAHundredLines => "for /L %i in (1,1,100) do @echo %i";

    protected override string ClaimsASystemFolder => "type claim.txt & ping -n 3 127.0.0.1 >nul";

    protected override (string Command, string Folder) MovesTheShell
            => ($"cd /d \"{System.IO.Path.GetDirectoryName(_folder)}\"", System.IO.Path.GetDirectoryName(_folder)!);

        protected override IReadOnlyList<(string Command, string Folder)> AwkwardMoves
        {
            get
            {
                var odd = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(_folder, "a b ü & (x) 日本")).FullName;
                System.IO.File.WriteAllText(System.IO.Path.Combine(_folder, "hide.txt"), "\x1b]133;B\x1b\\\x1b]0;never ended");
                return
                [
                    ($"cd /d \"{odd}\"", odd),
                    ("cd ..", _folder),
                    ("type hide.txt & cd /d C:\\", "C:\\"),
                    ($"prompt $G & cd /d \"{odd}\"", odd),
                    ($"pushd \"{Windows}\"", Windows),
                    ("popd", odd),
                ];
            }
        }
}

[TestClass]
[DoNotParallelize]
[TestCategory("Interactive")]
[TestCategory("Wsl")]
[CoversNode("wslshell-shell")]
public sealed class LiveWslShellTests : LiveShellTests
{
    private LocationSessionPool? _pool;
    private LocationSet? _set;
    private LocationFileSystem? _files;

    [TestMethod]
    [CoversNode("wslshell-env-panel")]
    public async Task InADistro_TheShellShowsItsFolder_ItsFiles_AndItsOwnVariables()
    {
        var name   = $"nexaflow-console-{Guid.NewGuid():N}";
        var folder = $@"{_set!.Mounts[0].Root}\tmp\{name}";
        await _files!.CreateDirectoryAsync(folder);
        await _files.WriteAllTextAsync(_files.ChildPath(folder, "at 12:30.txt"), "half past");
        try
        {
            OnUi(() => Terminal.SendCommand($"cd '/tmp/{name}' && export NEXAFLOW_PROBE='from the distro'"));

            await UntilAsync(() => Terminal.ShownPath == $"/tmp/{name}", "the folder, as Linux names it");
            await UntilAsync(() => Terminal.Files.Any(f => f.Name == "at 12:30.txt"), "its file, as Linux names it");
            await UntilAsync(() => Terminal.EnvVars.Any(v => v.Name == "NEXAFLOW_PROBE" && v.Value == "from the distro"),
                             "the shell's own export, live");
            Assert.IsFalse(OnUi(() => Terminal.EnvVars.Any(v => v.Name.Equals("SystemRoot", StringComparison.OrdinalIgnoreCase))),
                           "none of this computer's");
            Assert.IsTrue(OnUi(() => Terminal.EnvScopes.Contains(EnvScope.Startup)), "variables can be kept for new shells");

            OnUi(() => Terminal.ListsStartupEnvironment = true);
            await UntilAsync(() => Terminal.EnvVars.Any(v => v.Name == "HOME") && Terminal.EnvVars.All(v => v.Name != "NEXAFLOW_PROBE"),
                             "a new shell's variables, without this one's export");
        }
        finally
        {
            OnUi(() => Terminal.SendCommand("cd /tmp"));
            await _files.DeleteAsync(folder, recursive: true);
        }
    }

    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    protected override async Task<(TerminalShellRoute Route, Dictionary<string, string> Variables)> RouteAsync()
    {
        var provider = new WslLocationProvider();
        var distros  = await provider.DiscoverAsync(CancellationToken.None);
        if (distros.Count == 0) Assert.Inconclusive("WSL has no distro installed on this machine.");

        var registry = new LocationProviderRegistry();
        registry.Register(provider);
        _pool = new LocationSessionPool(registry, new StoredCredentialSource(new NoSecrets()));
        _set  = _pool.Acquire([distros[0]]);
        var files = new LocationFileSystem(VirtualFileSystem.Instance);
        files.RegisterLocation(_set.Mounts[0]);
        _files = files;

        return (TerminalShellRoute.ForLocationShell(files, $@"{_set.Mounts[0].Root}\tmp"),
                new Dictionary<string, string> { ["HISTFILE"] = "/dev/null" });
    }

    [TestCleanup]
    public async Task ReleaseDistro()
    {
        if (_pool is null) return;
        _pool.Release(_set);
        await _pool.DisposeAsync();
    }

    protected override string PrintsAHundredLines => "seq 1 100";

    protected override string ClaimsASystemFolder
        => @"printf '\033]7;file://localhost/\033\\\033]133;A\033\\\033]133;B\033\\'; sleep 2";

    protected override (string Command, string Folder) MovesTheShell
            => ("cd /usr/share", $@"{_set!.Mounts[0].Root}\usr\share");

        protected override IReadOnlyList<(string Command, string Folder)> AwkwardMoves
        {
            get
            {
                var root = _set!.Mounts[0].Root;
                var name = $"nexaflow-move-{Guid.NewGuid():N}";
                return
                [
                    ($"mkdir '/tmp/{name}' \"/tmp/{name}/\"$'a b:\\nü' && cd \"/tmp/{name}/\"$'a b:\\nü'", $@"{root}\tmp\{name}\a b%3A%0Aü"),
                    ($"cd /tmp && rmdir \"/tmp/{name}/\"$'a b:\\nü' '/tmp/{name}'", $@"{root}\tmp"),
                    (@"printf '\033]0;never ended'; cd /etc", $@"{root}\etc"),
                    ("PS1='> '; cd /", root),
                    ("cd /bin", $@"{root}\usr\bin"),
                    ("exec bash --norc", $@"{root}\usr\bin"),
                    ("cd /var", $@"{root}\var"),
                ];
            }
        }

    [TestMethod]
    public async Task TheBarKnowsTheDistrosOwnPrograms_ThoughWindowsHasNone()
    {
        var terminal = await TerminalOnceItsProgramsAreListedAsync();

        Assert.AreEqual(ConsoleLineVerdict.Command, terminal.JudgeConsoleLine("dpkg"), "a program of the distro's, with no flag to give it away");
        Assert.AreEqual(ConsoleLineVerdict.Command, terminal.JudgeConsoleLine("apt update"));
        Assert.AreEqual(ConsoleLineVerdict.Unclear, terminal.JudgeConsoleLine("nexaflow-no-such-program"));
    }
}
