using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Nexaflow.Features.Common;
using Nexaflow.IO.Terminal;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.ViewModels;
using NSubstitute;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// The console's folder over a real cmd, where a fake cannot answer: it is what the assistant may reach and what the tab
/// reopens at, so it follows every real move — odd names, a changed prompt, output that swallows the prompt — and never a
/// folder that is only printed. Commands only print or change folder, inside a folder of the test's own.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("Interactive")]
[CoversNode("ioterm-transport")]
public sealed class LiveCmdShellTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private Dispatcher _ui = null!;
    private LiveTerminal _terminal = null!;
    private string _folder = "";

    /// <summary>A shell with a UI thread of its own, which is all the terminal asks of one here.</summary>
    public class UiThreadShell : DispatchProxy
    {
        public Dispatcher Ui { get; set; } = null!;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IShellServices.RunOnUiAsync))
            {
                if (args![0] is Action action) return Ui.InvokeAsync(action).Task;
                var asked = (Delegate)args[0]!;
                return Ui.CheckAccess() ? asked.DynamicInvoke() : Ui.Invoke(() => asked.DynamicInvoke());
            }
            var returns = method.ReturnType;
            if (returns == typeof(Task)) return Task.CompletedTask;
            return returns != typeof(void) && returns.IsValueType ? Activator.CreateInstance(returns) : null;
        }
    }

    private sealed class LiveTerminal(IShellServices shell)
        : TerminalViewModel(new PseudoConsoleLauncher(), shell, Substitute.For<IAIService>())
    {
        protected override IReadOnlyList<TerminalEnvironment> Environments => [];
        protected override string? FindBoundEnvName(string folderPath) => null;
        protected override void PersistFolderBinding(string folderPath, string envName) { }

        public Task StartAsync(string folder)
        {
            SetupInitialState(new TerminalEnvironment { Name = "Live test" }, folder);
            return ShellStarted;
        }
    }

    [TestInitialize]
    public async Task StartShell()
    {
        _folder = TestTemp.CreateSubdirectory("nexa-live-cmd-").FullName;
        File.WriteAllText(Path.Combine(_folder, "claim.txt"), $"{Windows}>");
        File.WriteAllText(Path.Combine(_folder, "hide.txt"), "\x1b]0;never ended");

        var running = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread  = new Thread(() =>
        {
            running.SetResult(Dispatcher.CurrentDispatcher);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        _ui = await running.Task;

        var shell = DispatchProxy.Create<IShellServices, UiThreadShell>();
        ((UiThreadShell)(object)shell).Ui = _ui;
        await await _ui.InvokeAsync(() =>
        {
            _terminal = new LiveTerminal(shell);
            return _terminal.StartAsync(_folder);
        });
        await UntilAsync(() => string.Equals(_terminal.CurrentPath, _folder, StringComparison.OrdinalIgnoreCase), "the start folder");
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

    [TestMethod]
    public async Task WhatACommandPrints_NeverMovesTheFolderTheAssistantMayReach()
    {
        var moves = new List<string>();
        OnUi(() => _terminal.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(TerminalViewModel.CurrentPath)) moves.Add(_terminal.CurrentPath);
        });

        OnUi(() => _terminal.SendCommand("type claim.txt & ping -n 3 127.0.0.1 >nul"));
        await Task.Delay(TimeSpan.FromSeconds(6));

        OnUi(() =>
        {
            Assert.AreEqual("", string.Join(", ", moves), "printing moved the folder");
            CollectionAssert.AreEqual(new[] { _folder }, _terminal.GetContextGrant().Folders.ToArray());
        });
    }

    [TestMethod]
    public async Task ARealMove_IsFollowed_HoweverTheShellShowsIt()
    {
        var odd = Directory.CreateDirectory(Path.Combine(_folder, "a b ü & (x) 日本")).FullName;
        (string Command, string Folder)[] moves =
        [
            ($"cd /d \"{odd}\"", odd),
            ("cd ..", _folder),
            ("type hide.txt & cd /d C:\\", "C:\\"),
            ($"prompt $G & cd /d \"{odd}\"", odd),
            ($"pushd \"{Windows}\"", Windows),
            ("popd", odd),
        ];

        foreach (var (command, folder) in moves)
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
        OnUi(() => _terminal.SendCommand("type claim.txt & ping -n 3 127.0.0.1 >nul"));
        await Task.Delay(TimeSpan.FromSeconds(6));
        var parent = Path.GetDirectoryName(_folder)!;

        await _terminal.RunCommandCaptureAsync($"cd /d \"{parent}\"", CancellationToken.None);

        Assert.AreEqual(parent, _ui.Invoke(() => _terminal.GetContextGrant().Folders.Single()), ignoreCase: true);
    }
}
