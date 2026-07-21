using System.IO;
using FlaUI.Core.AutomationElements;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Dotnet.UI;

/// <summary>
/// End-to-end UI journey for the .NET folder viewlet, driven through the real shell. Uses the shape that
/// was broken: a solution at the folder root with its projects in <em>subfolders</em>, so the folder scan
/// finds exactly one target (the solution).
/// <list type="bullet">
///   <item>the open-in-default-app button used to be nested inside the multi-target picker, so it vanished
///         in exactly this single-target case;</item>
///   <item>Run used to shell out to a bare <c>dotnet run</c> in the folder — which finds no project there
///         and fails with "cannot find the file". It now resolves the solution to a startup project.</item>
/// </list>
/// Requires an interactive desktop session — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("dotnet-ui")]
public class DotnetViewletJourneyTests : UiJourneyTestBase
{
    private const string GuiApp = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>WinExe</OutputType></PropertyGroup></Project>";
    private const string Library = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup/></Project>";

    private string _folder = string.Empty;

    /// <summary>A solution folder whose only top-level .NET file is the solution itself — one runnable
    /// project (App), one library that must not be offered as a run target.</summary>
    private string SolutionFolder()
    {
        if (_folder.Length > 0) return _folder;

        _folder = Path.Combine(Path.GetTempPath(), "nexadotnetui_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_folder, "App"));
        Directory.CreateDirectory(Path.Combine(_folder, "Lib"));
        File.WriteAllText(Path.Combine(_folder, "App", "App.csproj"), GuiApp);
        File.WriteAllText(Path.Combine(_folder, "Lib", "Lib.csproj"), Library);
        File.WriteAllText(Path.Combine(_folder, "My.slnx"),
            """<Solution><Project Path="App/App.csproj" /><Project Path="Lib/Lib.csproj" /></Solution>""");
        return _folder;
    }

    [TestCleanup]
    public void RemoveFolder()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); } catch { }
    }

    [TestMethod]
    [TestCategory("UI")]
    public void Dotnet_Controls_RespondInOnePass()
    {
        NavigateFileBrowserTo(SolutionFolder());

        Assert.IsNotNull(WaitForId("Dotnet_Viewlet", 10),
            "The .NET viewlet did not appear for a folder holding a solution.");

        // Single target → the caret picker is collapsed, but the open button must still be there.
        var open = CheckPresent("Open target", "Dotnet_OpenTargetButton");
        Check("Open target is enabled — there is a target", () => open is { IsEnabled: true });

        // The solution resolved to App.csproj; Lib is a library and is never a run target.
        var run = CheckPresent("Run", "Dotnet_RunButton");
        Check("Run is enabled — the solution resolved to a runnable startup project",
              () => run is { IsEnabled: true });

        // Every verb button is reachable and enabled once a target is selected. They are not *invoked* —
        // a real dotnet build in a UI test would be slow and machine-dependent; the commands themselves are
        // unit-tested in DotnetViewletViewModelTests.
        foreach (var (label, id) in new[]
                 {
                     ("Restore", "Dotnet_RestoreButton"), ("Build", "Dotnet_BuildButton"),
                     ("Test",    "Dotnet_TestButton"),    ("Clean", "Dotnet_CleanButton"),
                 })
        {
            var button = CheckPresent(label, id);
            Check($"{label} is enabled", () => button is { IsEnabled: true });
        }

        // Only one runnable project, so there is no choice to offer. A Collapsed element is absent from
        // the automation tree entirely, so a direct lookup (not WaitForId) is the right probe.
        Check("Startup caret hidden when only one project is runnable",
              () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Dotnet_StartupButton")) is null);

        // Idle: nothing is running, so the progress strip (and its Stop button) is collapsed.
        Check("Stop is hidden while idle",
              () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Dotnet_StopButton")) is null);

        AssertJourney();
    }
}
