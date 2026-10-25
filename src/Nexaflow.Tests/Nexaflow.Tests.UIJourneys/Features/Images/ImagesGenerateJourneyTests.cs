using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// A picture drawn from the file view's New picker, the user driving. In a workspace that draws pictures with Z-Image
/// Turbo, New offers Gen Image: chosen, it shows what to draw and what Z-Image offers — its three shapes and a seed, and
/// nothing it does not take — and Create draws the picture in the background into the folder under the name given.
/// <para>
/// <c>NEXAFLOW_TEST_AI_DIR</c> names an AI folder holding Z-Image Turbo as the app keeps it, each file verified, which the
/// app is started on as its <c>NEXAFLOW_AI_DIR</c>; without it the journey is inconclusive.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("OwnedModel")]
[CoversNode("images-gen-image")]
public class ImagesGenerateJourneyTests : UiJourneyTestBase
{
    private const string AiDirVariable = "NEXAFLOW_TEST_AI_DIR";

    private string _aiDir = "";
    private string _folder = "";

    /// <summary>The first picture loads Z-Image's parts before it draws.</summary>
    protected override TimeSpan JourneyBudget => TimeSpan.FromMinutes(5);

    protected override IReadOnlyDictionary<string, string> LaunchEnvironment =>
        new Dictionary<string, string> { ["NEXAFLOW_AI_DIR"] = _aiDir };

    /// <summary>A workspace open on an empty folder, drawing pictures with Z-Image Turbo.</summary>
    protected override void SeedConfig(string configDir)
    {
        var aiDir = Environment.GetEnvironmentVariable(AiDirVariable);
        if (string.IsNullOrEmpty(aiDir) || !Directory.Exists(aiDir))
        {
            Assert.Inconclusive($"{AiDirVariable} names no folder of Nexaflow's own models on this machine.");
            return;
        }
        _aiDir  = aiDir;
        _folder = Path.Combine(TestTemp.Root, "nexaflow-gen-image-" + Guid.NewGuid().ToString("N"));
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
                { "Id": "draw", "ProviderName": "Nexaflow", "Model": "z-image-turbo", "Kind": "ImageGeneration", "AssemblyFileName": "Nexaflow.Providers.AiHost.dll" }
              ],
              "Assignments": {
                "ImageGeneration": [ "draw" ]
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
    public void APictureDrawnFromTheNewPicker_LandsInTheFolderUnderItsName()
    {
        CheckDoes("New opens the picker", "NewMenuAction", () => WaitForId("CreateFileNameBox", 6) is not null, 10);
        CheckDoes("Gen Image shows what it draws with", "GenerateImageCreateAction", () => WaitForId("Images_GeneratePrompt", 6) is not null);
        CheckPresent("Its options sit in the picker", "Images_GenerateOptions");
        Check("Nothing to download: the model is here", () => FindInAppWindows("Images_GenerateDownload") is null);
        Check("Z-Image is offered with no other quality, size, detail, keep-out or transparency", () =>
            FindInAppWindows("Images_GenerateQuality") is null && FindInAppWindows("Images_GenerateSize") is null
            && FindInAppWindows("Images_GenerateDetail") is null && FindInAppWindows("Images_GenerateKeepOut") is null
            && FindInAppWindows("Images_GenerateTransparent") is null);
        Check("Create waits for what to draw", () => WaitForId("FileSystem_CreateConfirm") is { } create && !create.IsEnabled);

        Check("What to draw is typed", () => TypeInto("Images_GeneratePrompt", "A lighthouse on a cliff at dusk, oil painting"));
        Check("Its three shapes are offered, and landscape picked", () => PickShape(1));
        Check("A seed is typed", () => TypeInto("Images_GenerateSeed", "42"));
        Check("The name is given", () => TypeInto("CreateFileNameBox", "lighthouse"));
        Check("Create is offered now", () => WaitForFs(() => WaitForId("FileSystem_CreateConfirm")?.IsEnabled == true, 2));

        CheckDoes("Create closes the picker and sets the picture drawing", "FileSystem_CreateConfirm", () => WaitForGone("CreateFileNameBox", 4));
        Check("The picture lands in the folder under its name", () =>
            WaitForFs(() => File.Exists(Path.Combine(_folder, "lighthouse.png")), Affordable(180)));

        AssertJourney();
    }

    /// <summary>Picks the shape at <paramref name="index"/> of the three Z-Image draws.</summary>
    private bool PickShape(int index)
    {
        if (WaitForId("Images_GenerateShape")?.AsComboBox() is not { } shapes || shapes.Items.Length != 3) return false;
        var picked = shapes.Select(index);
        return WaitForFs(() => shapes.SelectedItem?.Name == picked.Name, 2);
    }
}
