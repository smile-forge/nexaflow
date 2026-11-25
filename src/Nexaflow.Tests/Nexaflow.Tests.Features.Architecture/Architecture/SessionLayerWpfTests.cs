using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// The shell's session and its workspace runtimes name no UI toolkit (docs/Architecture.md, Ownership &amp; Lifetime): what only
/// WPF can make — the UI thread's dispatcher, the clipboard, a window — reaches them through <c>IUiThread</c> and the
/// <c>ShellPlatform</c> the app gives the host. Kept so, another toolkit's shell is a second adapter over the same session
/// rather than a rewrite of it.
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class SessionLayerWpfTests
{
    private static readonly string Root = RepoRoot.Locate();

    /// <summary>The session layer: the host, the runtime and what builds it, and what they share — the logs among it.</summary>
    private static readonly string[] SessionLayer =
    [
        "src/Nexaflow.Core/Services/ShellHost.cs",
        "src/Nexaflow.Core/Services/IUiThread.cs",
        "src/Nexaflow.Core/Services/RuntimeSessionServices.cs",
        "src/Nexaflow.Core/Services/WorkspaceRuntimeFactory.cs",
        "src/Nexaflow.Core/Services/FeatureScope.cs",
        "src/Nexaflow.Core/Services/FolderActivity.cs",
        "src/Nexaflow.Core/Models/WorkspaceRuntime.cs",
        "src/Nexaflow.Core/Services/Logging/ShellLogging.cs",
        "src/Nexaflow.Core/Services/Logging/LogDestination.cs",
        "src/Nexaflow.Core/Services/Logging/CrashLogProvider.cs",
        "src/Nexaflow.Core/Services/Logging/SessionLogProvider.cs",
        "src/Nexaflow.Core/Services/Logging/BindingErrorLogProvider.cs",
        "src/Nexaflow.Core/Services/Logging/FaultBurst.cs",
        "src/Nexaflow.Core/Services/Logging/StartupMilestones.cs",
    ];

    private static readonly Regex CommentsAndLiterals = new(
        @"@""(?:[^""]|"""")*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])+'|/\*.*?\*/|//[^\n]*",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex Wpf = new(@"\bSystem\.Windows\b", RegexOptions.Compiled);

    [TestMethod]
    [TestCategory("Unit")]
    public void The_session_layer_names_no_UI_toolkit()
    {
        var naming = SessionLayer
            .Where(path => Wpf.IsMatch(CommentsAndLiterals.Replace(File.ReadAllText(Path.Combine(Root, path)), "")))
            .ToList();

        Assert.AreEqual(0, naming.Count,
            "the session layer names WPF:\n" + string.Join("\n", naming.Select(path => $"  {path}"))
          + "\nReach the UI thread through IUiThread, and have the app hand over what only WPF can make in ShellPlatform.");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Every_listed_file_is_there()
    {
        var missing = SessionLayer.Where(path => !File.Exists(Path.Combine(Root, path))).ToList();

        Assert.AreEqual(0, missing.Count, "renamed or moved — update the list:\n" + string.Join("\n", missing));
    }
}
