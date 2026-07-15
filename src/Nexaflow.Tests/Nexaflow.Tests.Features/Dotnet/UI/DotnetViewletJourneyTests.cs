using System.IO;
using FlaUI.Core.AutomationElements;
using Nexaflow.Tests.Features.WindowsFileSystem.UI;
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
[CoversNode("viewlet")]
public class DotnetViewletJourneyTests : FileSystemUiTestBase
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
    [CoversNode("dotnet-target-picker")]
    public void SolutionFolder_ShowsViewletWithOpenButton_AndAnEnabledRun()
    {
        NavigateFileBrowserTo(SolutionFolder());

        Assert.IsNotNull(WaitForId("Dotnet_Viewlet", 10),
            "The .NET viewlet did not appear for a folder holding a solution.");

        // Single target → the caret picker is collapsed, but the open button must still be there.
        var open = WaitForId("Dotnet_OpenTargetButton", 5);
        Assert.IsNotNull(open, "The open-target button is missing when the folder has a single target.");
        Assert.IsTrue(open!.IsEnabled, "The open-target button should be enabled — there is a target.");

        // The solution resolved to App.csproj; Lib is a library and is never a run target.
        var run = WaitForId("Dotnet_RunButton", 5);
        Assert.IsNotNull(run, "The Run button is missing.");
        Assert.IsTrue(run!.IsEnabled,
            "Run is disabled — the solution did not resolve to a runnable startup project.");

        // Only one runnable project, so there is no choice to offer. A Collapsed element is absent from
        // the automation tree entirely, so a direct lookup (not WaitForId) is the right probe.
        Assert.IsNull(MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Dotnet_StartupButton")),
            "The startup-project caret should be hidden when only one project is runnable.");

        Assert.IsFalse(App.HasExited, "App crashed while showing the .NET viewlet.");
    }
}
