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
/// Every command only prints. Each shell keeps its history to itself, so nothing lands in the person's own.
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

    private void OnUi(Action action) => _ui.Invoke(action);

    protected async Task<TerminalViewModel> TerminalOnceItsProgramsAreListedAsync()
    {
        await _terminal.ShellCommandsListed.WaitAsync(Patience);
        return _terminal;
    }

    private string Everything() => _ui.Invoke(() => string.Join("\n", _terminal.Scrollback.Append(_terminal.ScreenText)));

    private async Task UntilAsync(Func<bool> met, string what)
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
}

[TestClass]
[DoNotParallelize]
[TestCategory("Interactive")]
[CoversNode("console-enter-classify")]
public sealed class LiveCmdShellTests : LiveShellTests
{
    protected override Task<(TerminalShellRoute Route, Dictionary<string, string> Variables)> RouteAsync()
        => Task.FromResult((new TerminalShellRoute(new PseudoConsoleLauncher(), new CmdShellDialect(),
                                                   TestTemp.CreateSubdirectory("nexa-live-cmd-").FullName),
                            new Dictionary<string, string>()));

    protected override string PrintsAHundredLines => "for /L %i in (1,1,100) do @echo %i";
}

[TestClass]
[DoNotParallelize]
[TestCategory("Interactive")]
[TestCategory("Wsl")]
[CoversNode("console-location-shell")]
public sealed class LiveWslShellTests : LiveShellTests
{
    private LocationSessionPool? _pool;
    private LocationSet? _set;

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

        return (TerminalShellRoute.ForFolder(files, $@"{_set.Mounts[0].Root}\tmp", new PseudoConsoleLauncher(), new CmdShellDialect(files)),
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

    [TestMethod]
    public async Task TheBarKnowsTheDistrosOwnPrograms_ThoughWindowsHasNone()
    {
        var terminal = await TerminalOnceItsProgramsAreListedAsync();

        Assert.AreEqual(ConsoleLineVerdict.Command, terminal.JudgeConsoleLine("dpkg"), "a program of the distro's, with no flag to give it away");
        Assert.AreEqual(ConsoleLineVerdict.Command, terminal.JudgeConsoleLine("apt update"));
        Assert.AreEqual(ConsoleLineVerdict.Unclear, terminal.JudgeConsoleLine("nexaflow-no-such-program"));
    }
}
