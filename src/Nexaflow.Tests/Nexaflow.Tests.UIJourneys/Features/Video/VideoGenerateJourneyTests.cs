using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.Video.UI;

/// <summary>
/// A clip made from the file view's New picker, the user driving. In a workspace that makes clips with Wan2.2 FastWan,
/// New offers Gen Video: chosen, it shows what happens and what FastWan offers — its two shapes, its length and a seed,
/// and nothing it does not take — and Create sets the clip making as one of the workspace's background tasks, which the
/// AI bar's pill lists, with Stop.
/// <para>
/// <c>NEXAFLOW_TEST_AI_DIR</c> names an AI folder holding Wan2.2 FastWan as the app keeps it, each file verified, which
/// the app is started on as its <c>NEXAFLOW_AI_DIR</c>; without it the journey is inconclusive.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("OwnedModel")]
[CoversNode("video-gen-video")]
public class VideoGenerateJourneyTests : UiJourneyTestBase
{
    private const string AiDirVariable = "NEXAFLOW_TEST_AI_DIR";

    private string _aiDir = "";
    private string _folder = "";

    /// <summary>Starting the clip loads the video model's prompt encoder before the job is listed.</summary>
    protected override TimeSpan JourneyBudget => TimeSpan.FromMinutes(4);

    protected override IReadOnlyDictionary<string, string> LaunchEnvironment =>
        new Dictionary<string, string> { ["NEXAFLOW_AI_DIR"] = _aiDir };

    /// <summary>A workspace open on an empty folder, making clips with Wan2.2 FastWan.</summary>
    protected override void SeedConfig(string configDir)
    {
        var aiDir = Environment.GetEnvironmentVariable(AiDirVariable);
        if (string.IsNullOrEmpty(aiDir) || !Directory.Exists(aiDir))
        {
            Assert.Inconclusive($"{AiDirVariable} names no folder of Nexaflow's own models on this machine.");
            return;
        }
        _aiDir  = aiDir;
        _folder = Path.Combine(TestTemp.Root, "nexaflow-gen-video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);

        var version = FileVersionInfo.GetVersionInfo(BuiltOutput.App).FileVersion ?? "1.0.0.0";
        var workspaces = Path.Combine(configDir, "workcontexts");
        Directory.CreateDirectory(workspaces);
        File.WriteAllText(Path.Combine(workspaces, $"config_{version}.json"), $$"""
            {
              "ConfigName": "workcontexts",
              "FriendlyName": "Workspaces",
              "Contexts": [
                {
                  "DefaultTabs": [
                    { "PageKind": "FileSystem", "PageParams": { "mode": "path", "path": {{JsonSerializer.Serialize(_folder)}} }, "Pane": 0, "Title": "videos", "IsActive": true }
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
                { "Id": "film", "ProviderName": "Nexaflow", "Model": "fastwan2.2-ti2v-5b", "Kind": "VideoGeneration", "AssemblyFileName": "Nexaflow.Providers.AiHost.dll" }
              ],
              "Assignments": {
                "VideoGeneration": [ "film" ]
              }
            }
            """);
    }

    [TestCleanup]
    public void CleanupFolder()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); } catch { }
    }

    [TestMethod]
    public void AClipMadeFromTheNewPicker_RunsAsABackgroundTask()
    {
        CheckDoes("New opens the picker", "NewMenuAction", () => WaitForId("CreateFileNameBox", 6) is not null, 10);
        CheckDoes("Gen Video shows what it makes clips with", "GenerateVideoCreateAction", () => WaitForId("Video_GeneratePrompt", 6) is not null);
        CheckPresent("Its options sit in the picker", "Video_GenerateOptions");
        Check("Nothing to download: the model is here", () => FindInAppWindows("Video_GenerateDownload") is null);
        Check("FastWan is offered at one size, told nothing to keep out", () =>
            FindInAppWindows("Video_GenerateSize") is null && FindInAppWindows("Video_GenerateKeepOut") is null);
        Check("Create waits for what happens", () => WaitForId("FileSystem_CreateConfirm") is { } create && !create.IsEnabled);

        Check("What happens is typed", () => TypeInto("Video_GeneratePrompt", "Waves roll onto a pebble beach at dawn."));
        Check("Portrait is picked of its two shapes", () => Pick("Video_GenerateShape", count: 2, index: 1));
        Check("Its one length is chosen", () => Pick("Video_GenerateLength", count: 1, index: 0));
        Check("A seed is typed", () => TypeInto("Video_GenerateSeed", "7"));
        Check("The name is given", () => TypeInto("CreateFileNameBox", "waves"));

        // Created, the picker goes and the clip is made in the background, listed from the AI bar's pill — and stopped
        // there, since a whole clip takes minutes.
        Check("Create closes the picker and sets the clip making", () =>
            PressInDialog("FileSystem_CreateConfirm") && WaitForId("AiBar_BackgroundTasks", Affordable(60)) is not null);
        CheckDoes("The pill lists it, with Stop", "AiBar_BackgroundTasks", () => WaitForId("AiBar_BackgroundTaskStop", 10) is not null);
        CheckDoes("Stop ends it, and the pill goes with it", "AiBar_BackgroundTaskStop",
                  () => WaitForGone("AiBar_BackgroundTaskStop", 20) && WaitForGone("AiBar_BackgroundTasks", 20));

        AssertJourney();
    }

    /// <summary>Picks the item at <paramref name="index"/> of a list of <paramref name="count"/>.</summary>
    private bool Pick(string automationId, int count, int index)
    {
        if (WaitForId(automationId)?.AsComboBox() is not { } list || list.Items.Length != count) return false;
        var picked = list.Select(index);
        return WaitForFs(() => list.SelectedItem?.Name == picked.Name, 2);
    }
}
