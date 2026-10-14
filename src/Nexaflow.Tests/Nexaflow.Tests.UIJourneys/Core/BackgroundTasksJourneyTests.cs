using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.UI;

/// <summary>
/// Slow work the assistant leaves running, from the AI bar, on Nexaflow's own models. Asked for a picture, with only a
/// high-quality model to draw with, the assistant has it approved, then replies while it is drawn in the background: the bar
/// shows a pill counting the workspace's background tasks, which opens their list — what each is doing, for how long — and
/// Stop there ends the drawing, and the pill goes.
/// <para>
/// <c>NEXAFLOW_TEST_AI_DIR</c> names an AI folder holding the workspace's models as the app keeps them — Gemma 4 E4B and
/// Z-Image Turbo, each file verified — which the app is started on as its <c>NEXAFLOW_AI_DIR</c>; without it the journey is
/// inconclusive.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("OwnedModel")]
[CoversNode("aibar-background-tasks")]
public class BackgroundTasksJourneyTests : UiJourneyTestBase
{
    private const string AiDirVariable = "NEXAFLOW_TEST_AI_DIR";

    private string _aiDir = "";
    private string _folder = "";

    /// <summary>The first request loads the language model, and the approved drawing the picture model.</summary>
    protected override TimeSpan JourneyBudget => TimeSpan.FromMinutes(6);

    protected override IReadOnlyDictionary<string, string> LaunchEnvironment =>
        new Dictionary<string, string> { ["NEXAFLOW_AI_DIR"] = _aiDir };

    /// <summary>A workspace that converses on Gemma and draws, at high quality only, with Z-Image.</summary>
    protected override void SeedConfig(string configDir)
    {
        var aiDir = Environment.GetEnvironmentVariable(AiDirVariable);
        if (string.IsNullOrEmpty(aiDir) || !Directory.Exists(aiDir))
        {
            Assert.Inconclusive($"{AiDirVariable} names no folder of Nexaflow's own models on this machine.");
            return;
        }
        _aiDir  = aiDir;
        _folder = Directory.CreateTempSubdirectory("nexaflow-background-").FullName;

        var version = FileVersionInfo.GetVersionInfo(FindAppExe()).FileVersion ?? "1.0.0.0";
        var workspaces = Path.Combine(configDir, "workcontexts");
        Directory.CreateDirectory(workspaces);
        File.WriteAllText(Path.Combine(workspaces, $"config_{version}.json"), $$"""
            {
              "ConfigName": "workcontexts",
              "FriendlyName": "Workspaces",
              "Contexts": [
                {
                  "DefaultTabs": [
                    { "PageKind": "FileSystem", "PageParams": { "mode": "path", "path": {{JsonSerializer.Serialize(_folder)}} }, "Pane": 0, "Title": "pictures", "IsActive": true }
                  ],
                  "LastSessionTabs": [],
                  "Name": "Default",
                  "Color": "#5B8CFF",
                  "Icon": "⬡"
                }
              ]
            }
            """);
        var abilities = Path.Combine(configDir, "Contexts", "Default", "ai-abilities");
        Directory.CreateDirectory(abilities);
        File.WriteAllText(Path.Combine(abilities, $"config_{version}.json"), """
            {
              "ConfigName": "ai-abilities",
              "FriendlyName": "AI",
              "Columns": [
                { "Id": "chat", "ProviderName": "Nexaflow", "Model": "gemma-4-e4b-it", "Kind": "Language",        "AssemblyFileName": "Nexaflow.Providers.AiHost.dll" },
                { "Id": "draw", "ProviderName": "Nexaflow", "Model": "z-image-turbo",  "Kind": "ImageGeneration", "AssemblyFileName": "Nexaflow.Providers.AiHost.dll" }
              ],
              "Assignments": {
                "Conversation":               [ "chat" ],
                "Disambiguation":             [ "chat" ],
                "HighQualityImageGeneration": [ "draw" ]
              }
            }
            """);
    }

    [TestCleanup]
    public void RemoveFolder()
    {
        try { if (_folder.Length > 0 && Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); } catch { }
    }

    [TestMethod]
    public void APictureDrawnInTheBackground_IsListedFromThePill_AndStoppedThere()
    {
        Check("the request is typed and sent", () => InFront() && Ask("Draw a picture of a lighthouse at dusk, in watercolour."));

        // A picture at high quality is approved first, in the overlay or a page's own banner.
        var accept = WaitForAnyId(["AiOverlay_Accept", "AiBanner_Accept"], Affordable(120));
        Check("drawing at high quality is put to the user first", () => accept is not null);
        if (accept is not null) CheckDoes("approving it sets it drawing", accept, () => WaitForId("AiBar_BackgroundTasks", Affordable(60)) is not null);

        Check("the pill counts the workspace's background tasks", () => WaitForId("AiBar_BackgroundTasks", 5)?.Name is { Length: > 0 });
        CheckDoes("the pill opens their list, each with Stop", "AiBar_BackgroundTasks", () => WaitForId("AiBar_BackgroundTaskStop", 10) is not null);
        CheckDoes("Stop ends the drawing, and the pill goes with it", "AiBar_BackgroundTaskStop",
                  () => WaitForGone("AiBar_BackgroundTaskStop", 20) && WaitForGone("AiBar_BackgroundTasks", 20));
        Check("nothing was drawn into the folder", () => Directory.GetFiles(_folder).Length == 0);

        AssertJourney();
    }

    /// <summary>Types a request into the bar and sends it with Enter, as a user does.</summary>
    private bool Ask(string text)
    {
        var bar = WaitForId("AiInputBox", 10);
        if (bar is null || !WaitForFs(() => bar.IsEnabled, 30)) return false;
        bar.Click();
        Keyboard.Type(text);
        Wait.UntilInputIsProcessed();
        if (!InFront()) return false;
        Keyboard.Press(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => bar.AsTextBox().Text.Length == 0, 10);
    }

    /// <summary>The first of <paramref name="ids"/> on screen within <paramref name="seconds"/>, or null.</summary>
    private string? WaitForAnyId(IReadOnlyList<string> ids, int seconds)
    {
        string? found = null;
        WaitForFs(() => (found = ids.FirstOrDefault(id => WaitForId(id, 0) is not null)) is not null, seconds);
        return found;
    }
}
