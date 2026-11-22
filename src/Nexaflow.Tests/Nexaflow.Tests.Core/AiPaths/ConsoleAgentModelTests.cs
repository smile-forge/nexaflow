using System.Reflection;
using System.Windows.Threading;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.IO.Terminal;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.Shells;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Tests.Core.AiPaths;

/// <summary>
/// Telling the assistant what to do in the console, whole: a real model, the real <c>run_command</c>, a real shell. The
/// answer is on the first line of a file longer than the screen, so it is found only if the command ran in that shell, in
/// that folder, and all it printed came back.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[TestCategory("Interactive")]
[CoversNode("console-routing-group")]
public sealed class ConsoleAgentModelTests
{
    private const string Code = "PELICAN-4417";

    private static readonly string Facts =
        string.Join("\n", Enumerable.Range(1, 80).Select(n => n == 1 ? $"code {Code}" : $"filler line {n}")) + "\n";

    [ClassCleanup]
    public static Task Stop() => OwnedModelHarness.StopAsync();

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

    private sealed class LiveTerminal(TerminalShellRoute route, IShellServices shell, IAIService ai) : TerminalViewModel(route, shell, ai)
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

    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    [TestMethod]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task InCmd_TheAssistantRunsWhatIsAskedAndReadsAllItPrinted()
    {
        var model  = new RecordingModel(await OwnedModelHarness.ModelAsync());
        var folder = TestTemp.CreateSubdirectory("nexa-agent-console-").FullName;
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(folder, "facts.txt"), Facts.Replace("\n", "\r\n"));

        await AsksAsync(model, new TerminalShellRoute(new PseudoConsoleLauncher(), new CmdShellDialect(), folder), []);
    }

    [TestMethod]
    [TestCategory("Wsl")]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task InADistrosShell_TheAssistantRunsWhatIsAskedAndReadsAllItPrinted()
    {
        var provider = new WslLocationProvider();
        var distros  = await provider.DiscoverAsync(CancellationToken.None);
        if (distros.Count == 0) Assert.Inconclusive("WSL has no distro installed on this machine.");
        var model = new RecordingModel(await OwnedModelHarness.ModelAsync());

        var registry = new LocationProviderRegistry();
        registry.Register(provider);
        await using var pool = new LocationSessionPool(registry, new StoredCredentialSource(new NoSecrets()));
        var set   = pool.Acquire([distros[0]]);
        var files = new LocationFileSystem(VirtualFileSystem.Instance);
        files.RegisterLocation(set.Mounts[0]);

        var folder = $@"{set.Mounts[0].Root}\tmp\nexaflow-agent-{Guid.NewGuid():N}";
        await files.CreateDirectoryAsync(folder);
        try
        {
            await files.WriteAllTextAsync($@"{folder}\facts.txt", Facts);
            await AsksAsync(model, TerminalShellRoute.ForFolder(files, folder, new PseudoConsoleLauncher(), new CmdShellDialect(files)),
                            new() { ["HISTFILE"] = "/dev/null" });
        }
        finally
        {
            await files.DeleteAsync(folder, recursive: true);
            pool.Release(set);
        }
    }

    private static async Task AsksAsync(RecordingModel model, TerminalShellRoute route, Dictionary<string, string> variables)
    {
        var running = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread  = new Thread(() =>
        {
            running.SetResult(Dispatcher.CurrentDispatcher);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var ui    = await running.Task;
        var shell = DispatchProxy.Create<IShellServices, UiThreadShell>();
        ((UiThreadShell)(object)shell).Ui = ui;

        // Made and run on that thread, as the app's are: the service's shell services take the dispatcher they are made on.
        var service = ui.Invoke(() => OwnedModelHarness.Service(model));
        LiveTerminal terminal = null!;
        try
        {
            await await ui.InvokeAsync(() =>
            {
                terminal = new LiveTerminal(route, shell, service);
                return terminal.StartAsync(new TerminalEnvironment { Name = "Agent test", EnvOverrides = variables });
            });
            for (int i = 0; i < 100 && ui.Invoke(() => terminal.IsBusy || terminal.ScreenText.Trim().Length == 0); i++)
                await Task.Delay(200);
            await Task.Delay(1000);

            var approver = new Approver();
            using var patience = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var response = await (await ui.InvokeAsync(() => service.RunAgentAsync(
                               terminal, "Print facts.txt in this folder and tell me the code on its first line.",
                               true, approver, patience.Token))).WaitAsync(patience.Token);

            Assert.IsTrue(approver.Batches.Any(batch => batch.Contains("run_command")), "it was asked to approve the command\n" + model.Transcript);
            StringAssert.Contains(response?.Text ?? "", Code, "the first of 80 lines came back\n" + model.Transcript);
        }
        finally
        {
            ui.Invoke(() => terminal?.Dispose());
            ui.InvokeShutdown();
        }
    }
}
