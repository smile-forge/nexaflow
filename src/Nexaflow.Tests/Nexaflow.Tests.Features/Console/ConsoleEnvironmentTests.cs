using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.ViewModels;
using NSubstitute;

namespace Nexaflow.Tests.Features.Console;

/// <summary>The Console's cmd prompt lists this computer's variables, and keeps a change for the session, the Windows user
/// or the machine.</summary>
[TestClass]
[CoversNode("console-env-panel")]
public sealed class ConsoleEnvironmentTests
{
    private sealed class TestTerminal(FakeTerminal local, IShellServices shell)
        : TerminalViewModel(local.AsCmd(@"C:\work"), shell, Substitute.For<IAIService>())
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

    [TestMethod]
    public async Task ThisComputersShell_ListsThisComputersVariables_AndItsScopes()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<System.Action>()).Returns(ci => { ci.Arg<System.Action>()(); return Task.CompletedTask; });
        var terminal = new TestTerminal(new FakeTerminal(), shell);
        await terminal.Start();

        Assert.IsTrue(terminal.EnvVars.Any(v => string.Equals(v.Name, "PATH", System.StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(terminal.SwitchesEnvironment);
        CollectionAssert.AreEqual(new[] { EnvScope.Session, EnvScope.User, EnvScope.Machine }, terminal.EnvScopes.ToArray());
        Assert.AreEqual(@"C:\work", terminal.ShownPath);
    }
}
