using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// A picture drawn with MOSS-Image and a LoRA from the file view's New picker, the user driving. In a workspace that draws
/// its high-quality pictures with MOSS-Image, Gen Image offers what to keep out and MOSS-Image's LoRAs — none meant for
/// adults only, since Enhanced AI Protection Mode is on until the user turns it off. Fast, chosen, takes no negative
/// prompt, so what to keep out goes; Create draws the picture in its eight steps into the folder under the name given,
/// fetching the LoRA first.
/// <para>
/// <c>NEXAFLOW_TEST_AI_DIR</c> names an AI folder holding MOSS-Image as the app keeps it, each file verified, which the
/// app is started on as its <c>NEXAFLOW_AI_DIR</c>; without it the journey is inconclusive.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("OwnedModel")]
[CoversNode("owned-images-moss")]
public class ImagesMossJourneyTests : UiJourneyTestBase
{
    private const string AiDirVariable = "NEXAFLOW_TEST_AI_DIR";

    private string _aiDir = "";
    private string _folder = "";

    /// <summary>The first picture loads MOSS-Image's parts, and fetches the LoRA where it is not here yet.</summary>
    protected override TimeSpan JourneyBudget => TimeSpan.FromMinutes(6);

    protected override IReadOnlyDictionary<string, string> LaunchEnvironment =>
        new Dictionary<string, string> { ["NEXAFLOW_AI_DIR"] = _aiDir };

    /// <summary>A workspace open on an empty folder, drawing its high-quality pictures with MOSS-Image.</summary>
    protected override void SeedConfig(string configDir)
    {
        var aiDir = Environment.GetEnvironmentVariable(AiDirVariable);
        if (string.IsNullOrEmpty(aiDir) || !Directory.Exists(aiDir))
        {
            Assert.Inconclusive($"{AiDirVariable} names no folder of Nexaflow's own models on this machine.");
            return;
        }
        _aiDir  = aiDir;
        _folder = Path.Combine(TestTemp.Root, "nexaflow-moss-" + Guid.NewGuid().ToString("N"));
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
                { "Id": "moss", "ProviderName": "Nexaflow", "Model": "moss-image", "Kind": "ImageGeneration", "AssemblyFileName": "Nexaflow.Providers.AiHost.dll" }
              ],
              "Assignments": {
                "HighQualityImageGeneration": [ "moss" ]
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
    public void APictureDrawnWithTheFastLora_LandsInTheFolderUnderItsName()
    {
        CheckDoes("New opens the picker", "NewMenuAction", () => WaitForId("CreateFileNameBox", 6) is not null, 10);
        CheckDoes("Gen Image shows what it draws with", "GenerateImageCreateAction", () => WaitForId("Images_GeneratePrompt", 6) is not null);
        CheckPresent("MOSS-Image is told what to keep out", "Images_GenerateKeepOut");
        Check("Its LoRAs are offered after none, none meant for adults only", () => LoraNames() is { Length: 8 } names
              && names.Contains("Fast (8 steps)") && names.Contains("Anime") && !names.Contains("Uncensored"));

        Check("Fast is chosen", () => PickLora("Fast (8 steps)"));
        Check("Fast draws with no negative prompt, so what to keep out goes", () =>
            WaitForFs(() => FindInAppWindows("Images_GenerateKeepOut") is null, 2));

        Check("What to draw is typed", () => TypeInto("Images_GeneratePrompt", "A red fox in a snowy forest at dawn"));
        Check("The name is given", () => TypeInto("CreateFileNameBox", "fox"));
        Check("Create is offered now", () => WaitForFs(() => WaitForId("FileSystem_CreateConfirm")?.IsEnabled == true, 2));

        CheckDoes("Create closes the picker and sets the picture drawing", "FileSystem_CreateConfirm", () => WaitForGone("CreateFileNameBox", 4));
        Check("The picture lands in the folder under its name", () =>
            WaitForFs(() => File.Exists(Path.Combine(_folder, "fox.png")), Affordable(240)));

        AssertJourney();
    }

    /// <summary>The names of the LoRAs offered, none first.</summary>
    private string[] LoraNames() =>
        WaitForId("Images_GenerateLora")?.AsComboBox() is { } loras ? [.. loras.Items.Select(i => i.Name)] : [];

    private bool PickLora(string name)
    {
        if (WaitForId("Images_GenerateLora")?.AsComboBox() is not { } loras) return false;
        var index = Array.IndexOf(loras.Items.Select(i => i.Name).ToArray(), name);
        if (index < 0) return false;
        var picked = loras.Select(index);
        return WaitForFs(() => loras.SelectedItem?.Name == picked.Name, 2);
    }
}
