using System.Collections.Generic;
using System.Linq;
using System.Windows.Data;
using Nexaflow.Features.Common;
using Nexaflow.IO.Terminal;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.ViewModels;
using NSubstitute;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// Headless state logic on the terminal view-model that backs the Console tab's panel/toolbar chrome:
/// the env-var add/edit overlay, the launch-time environment picker, the Configure-button gate, the
/// environment-variable filter view, and the AI-bar command gate. These are exercised without a live PTY
/// (a no-op <see cref="PseudoConsoleHostService"/>, as in <c>TerminalKeyHistoryTests</c>) so no cmd.exe is
/// spawned — the command classification / screen / key-history / env-block logic is covered elsewhere and
/// is not duplicated here.
/// </summary>
[TestClass]
[CoversNode("visuals-terminal")]
public class ConsoleViewModelCommandTests
{
    private sealed class FakePty : PseudoConsoleHostService
    {
        public override void Start(short cols = 220, short rows = 50) { }
        public override void WriteInput(string text) { }
        public override void SendCtrlC() { }
    }

    /// <summary>
    /// A terminal VM with a fixed environment list and an optional Configure section, so the overlay /
    /// picker / filter state can be driven directly. Mirrors the cmd feature's subclass shape without
    /// touching its ConsoleConfig or a real shell.
    /// </summary>
    private sealed class TestTerminal : TerminalViewModel
    {
        private readonly IReadOnlyList<TerminalEnvironment> _envs;
        private readonly string? _configSection;

        public TestTerminal(IReadOnlyList<TerminalEnvironment>? envs = null, string? configSection = null)
            : base(new FakePty(), Substitute.For<IShellServices>(), Substitute.For<IAIService>())
        {
            _envs          = envs ?? [];
            _configSection = configSection;
        }

        protected override IReadOnlyList<TerminalEnvironment> Environments => _envs;
        protected override string? ConfigSectionName => _configSection;
        protected override string? FindBoundEnvName(string folderPath) => null;
        protected override void PersistFolderBinding(string folderPath, string envName) { }
    }

    private static TerminalEnvironment Env(string name) => new() { Name = name };

    // ── Configure-button gate ────────────────────────────────────────────────

    [TestMethod]
    public void ShowConfigureButton_False_WhenNoConfigSection()
        => Assert.IsFalse(new TestTerminal().ShowConfigureButton,
            "with no config section, the top-bar Configure button is hidden");

    [TestMethod]
    public void ShowConfigureButton_True_WhenConfigSectionPresent()
        => Assert.IsTrue(new TestTerminal(configSection: "console").ShowConfigureButton,
            "a configurable shell shows the Configure deep-link button");

    // ── Env-var add/edit overlay ─────────────────────────────────────────────

    [TestMethod]
    public void AddEnvVar_OpensOverlay_AsNew_WithEditableName()
    {
        var vm = new TestTerminal();

        vm.AddEnvVarCommand.Execute(null);

        Assert.IsTrue(vm.EnvEditVisible, "Add opens the env-edit overlay");
        Assert.IsFalse(vm.EnvEditNameReadOnly, "a new variable can name itself");
        Assert.AreEqual(string.Empty, vm.EnvEditName);
        Assert.AreEqual(string.Empty, vm.EnvEditValue);
        Assert.AreEqual(EnvScope.Session, vm.EnvEditScope, "the overlay defaults to the Session scope");
    }

    [TestMethod]
    public void EditEnvVar_OpensOverlay_WithReadOnlyNameAndExistingValues()
    {
        var vm = new TestTerminal();

        vm.EditEnvVarCommand.Execute(new EnvVar { Name = "PATH", Value = "C:\\bin" });

        Assert.IsTrue(vm.EnvEditVisible);
        Assert.IsTrue(vm.EnvEditNameReadOnly, "the name is fixed when editing an existing variable");
        Assert.AreEqual("PATH", vm.EnvEditName);
        Assert.AreEqual("C:\\bin", vm.EnvEditValue);
    }

    [TestMethod]
    public void CancelEnvEdit_HidesOverlay()
    {
        var vm = new TestTerminal();
        vm.AddEnvVarCommand.Execute(null);

        vm.CancelEnvEditCommand.Execute(null);

        Assert.IsFalse(vm.EnvEditVisible, "Cancel dismisses the env-edit overlay");
    }

    // ── Launch-time environment picker ───────────────────────────────────────

    [TestMethod]
    public void ShowEnvironmentPicker_PopulatesOptions_AndShows()
    {
        var vm = new TestTerminal();

        vm.ShowEnvironmentPicker([Env("cmd"), Env("dev")], @"C:\proj");

        Assert.IsTrue(vm.EnvPickerVisible, "the picker becomes visible");
        Assert.IsFalse(vm.AlwaysUseHere, "the 'always use here' checkbox resets each time the picker opens");
        CollectionAssert.AreEqual(
            new[] { "cmd", "dev" },
            vm.EnvPickerOptions.Select(e => e.Name).ToList(),
            "the picker lists exactly the offered environments, in order");
    }

    [TestMethod]
    public void CancelEnvPicker_HidesPicker()
    {
        var vm = new TestTerminal(envs: [Env("cmd")]);
        vm.ShowEnvironmentPicker([Env("cmd")], @"C:\proj");

        vm.CancelEnvPickerCommand.Execute(null);

        Assert.IsFalse(vm.EnvPickerVisible, "Cancel dismisses the launch picker");
    }

    [TestMethod]
    public void PickEnvironment_HidesPicker()
    {
        var vm  = new TestTerminal(envs: [Env("cmd"), Env("dev")]);
        var dev = Env("dev");
        vm.ShowEnvironmentPicker([Env("cmd"), dev], @"C:\proj");

        vm.PickEnvironmentCommand.Execute(dev);

        Assert.IsFalse(vm.EnvPickerVisible, "picking an environment dismisses the picker");
    }

    // ── Environment-variable filter view ─────────────────────────────────────

    [TestMethod]
    public void EnvFilter_NarrowsTheEnvVarView_ByNameOrValue_CaseInsensitive()
    {
        var vm = new TestTerminal();
        vm.EnvVars.Clear();
        vm.EnvVars.Add(new EnvVar { Name = "PATH",    Value = "C:\\bin" });
        vm.EnvVars.Add(new EnvVar { Name = "TEMP",    Value = "C:\\tmp" });
        vm.EnvVars.Add(new EnvVar { Name = "EDITOR",  Value = "notepad" });

        var view = CollectionViewSource.GetDefaultView(vm.EnvVars);

        vm.EnvFilter = "path";   // matches the PATH name, case-insensitively
        Assert.AreEqual(1, view.Cast<EnvVar>().Count(), "the filter narrows to the matching name");
        Assert.AreEqual("PATH", view.Cast<EnvVar>().Single().Name);

        vm.EnvFilter = "notepad";   // matches only by value
        Assert.AreEqual("EDITOR", view.Cast<EnvVar>().Single().Name,
            "the filter also matches on the variable value");

        vm.EnvFilter = string.Empty;   // cleared → everything shows again
        Assert.AreEqual(3, view.Cast<EnvVar>().Count(), "clearing the filter restores the full list");
    }

    // ── AI-bar command gate ──────────────────────────────────────────────────
    // (The recognised-command / natural-language routing is covered by ConsoleQueryHandlerTests via
    //  CanProcess; only the VM-level blank-input contract — which differs from the raw classifier — is new.)

    [TestMethod]
    public void IsConsoleCommand_BlankInput_IsNotACommand()
        => Assert.IsFalse(new TestTerminal().IsConsoleCommand("   "),
            "blank input is not routed to the shell (unlike the raw classifier, which treats it as an empty Enter)");
}
