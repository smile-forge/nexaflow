using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using System.IO.Compression;

namespace Nexaflow.Tests.Features.Markdown.UI;

/// <summary>
/// A whole Markdown document worked through with the workspace's Documents model, Nexaflow's own. Summarise opens the
/// summary beside the document, a heading per section, each leading back to its section — followed, the document shows that
/// section. Translate asks for the language, then opens the translation beside it, its code, its address and its block as
/// they were, and its link to a heading re-pointed at that heading translated.
/// <para>
/// What a model writes is never asserted, only its shape: the sections, the links, what is held back from the model.
/// <c>NEXAFLOW_TEST_AI_DIR</c> names an AI folder holding Gemma 4 E4B as the app keeps it, each file verified, which the
/// app is started on as its <c>NEXAFLOW_AI_DIR</c>; without it the journey is inconclusive.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("OwnedModel")]
[CoversNode("markdown-summarize")]
[CoversNode("markdown-translate")]
[CoversNode("markdown-place-links")]
public class MarkdownDocumentWorkJourneyTests : UiJourneyTestBase
{
    private const string AiDirVariable = "NEXAFLOW_TEST_AI_DIR";

    private const string FileName = "guide.md";

    /// <summary>The same guide kept as a Markdown Package, its picture inside it.</summary>
    private const string PackageName = "guide.mdz";

    /// <summary>The garden's guide: a title over three sections, the first long enough that the last starts out of view.</summary>
    private static readonly string Guide =
        "# Field guide\n\nA short guide to the garden, written for a first visit.\n\n" +
        "## Birds\n\n" + string.Concat(Enumerable.Range(1, 30).Select(n => $"Robins and blackbirds come to the hedge in the morning, and wrens in the evening; note {n}.\n\n")) +
        "## Trees\n\nThe oak by the gate is two hundred years old. Run `measure --girth` to see how wide it has grown.\n\n" +
        "```text\ngirth: 4.1 m\n```\n\nSee [the pond](#the-pond) for where the water comes from.\n\n" +
        "## The pond\n\nFrogs lay their eggs here in March. The season's notes are in the [pond notes](https://example.com/pond).\n";

    /// <summary>A picture of one pixel, for the package's document to name.</summary>
    private static readonly byte[] OnePixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private string _aiDir = "";
    private string _folder = "";

    /// <summary>The first question loads the model; a document of four sections is a few dozen questions, twice over.</summary>
    protected override TimeSpan JourneyBudget => TimeSpan.FromMinutes(20);

    protected override IReadOnlyDictionary<string, string> LaunchEnvironment =>
        new Dictionary<string, string> { ["NEXAFLOW_AI_DIR"] = _aiDir };

    /// <summary>A workspace whose Documents row, and whose conversation, are Gemma's, opened on the guide's folder.</summary>
    protected override void SeedConfig(string configDir)
    {
        var aiDir = Environment.GetEnvironmentVariable(AiDirVariable);
        if (string.IsNullOrEmpty(aiDir) || !Directory.Exists(aiDir))
        {
            Assert.Inconclusive($"{AiDirVariable} names no folder of Nexaflow's own models on this machine.");
            return;
        }
        _aiDir  = aiDir;
        _folder = TestTemp.CreateSubdirectory("nexaflow-documents-").FullName;
        File.WriteAllText(Path.Combine(_folder, FileName), Guide);
        using (var package = ZipFile.Open(Path.Combine(_folder, PackageName), ZipArchiveMode.Create))
        {
            using (var document = new StreamWriter(package.CreateEntry("document.md").Open()))
                document.Write(Guide.Replace("## Birds\n\n", "## Birds\n\n![A robin](attachments/robin.png)\n\n"));
            using (var picture = package.CreateEntry("attachments/robin.png").Open()) picture.Write(OnePixel);
        }

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
                    { "PageKind": "FileSystem", "PageParams": { "mode": "path", "path": {{JsonSerializer.Serialize(_folder)}} }, "Pane": 0, "Title": "garden", "IsActive": true }
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
                { "Id": "chat", "ProviderName": "Nexaflow", "Model": "gemma-4-e4b-it", "Kind": "Language", "AssemblyFileName": "Nexaflow.Providers.AiHost.dll" }
              ],
              "Assignments": {
                "GeneralIntelligence": [ "chat" ],
                "Disambiguation":      [ "chat" ],
                "Documents":           [ "chat" ]
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
    public void AWholeDocument_IsSummarisedAndTranslatedBesideIt_TheSummaryLeadingBackToEachSection()
    {
        var view = OpenFileVia(_folder, FileName, "ShowMarkdownAction", "MarkdownView");
        Assert.IsNotNull(view, "MarkdownView did not open via the 'Markdown' action.");
        Check("the pond starts out of view, below the birds", () => Link("pond notes") is null or { IsOffscreen: true });

        // Summarise: the model loads, then the document is read a section at a time in the background.
        CheckDoes("Summarise sets the summary going, and it opens beside the document", "Markdown_Summarize",
                  () => Made(() => Back("field-guide")), seconds: 10);
        Check("each section's heading leads back to it", () =>
            new[] { "field-guide", "birds", "trees", "the-pond" }.All(anchor => Back(anchor) is not null));
        Check("following the pond's heading shows the pond in the document", () =>
            Back("the-pond")?.Patterns.Invoke.PatternOrDefault is { } follow && Invoked(follow)
            && WaitFor(() => Link("pond notes") is { IsOffscreen: false } shown ? shown : null, 15) is not null);

        // Translate, from the document itself: the language is asked for, then the translation opens beside it.
        Check("Translate asks for the language", () => Press(Leftmost("Markdown_Translate")) && WaitForId("Markdown_TranslateTo", 5) is not null);
        Check("French is chosen", () => TypeInPopup("Markdown_TranslateTo", "French"));
        CheckDoes("Translate it sets the translation going, and it opens beside the document", "Markdown_TranslateGo",
                  () => Made(() => Rightmost("MarkdownView") is { } draft && Source(draft) is { } said && IsTranslation(said) ? draft : null), seconds: 10);

        var translated = Rightmost("MarkdownView") is { } shown ? Source(shown) ?? "" : "";
        Check("its code is as it was", () => translated.Contains("`measure --girth`") && translated.Contains("```text\ngirth: 4.1 m\n```"));
        Check("its address is as it was", () => translated.Contains("(https://example.com/pond)"));
        Check("its link to a heading is re-pointed at the heading translated", () =>
            !translated.Contains("(#the-pond)") && translated.Contains("](#"));
        Check("it keeps the document's sections", () =>
            translated.Split('\n').Count(line => line.StartsWith("## ", StringComparison.Ordinal)) == 3
            && translated.Split('\n').Count(line => line.StartsWith("# ", StringComparison.Ordinal)) == 1);

        AssertJourney();
    }

    /// <summary>
    /// A Markdown Package is worked through as its document, as a Word document is: Summarise is offered for it, says while it
    /// works that it is summarising, and the summary's headings lead back into the package, at their sections.
    /// </summary>
    [TestMethod]
    public void APackage_IsSummarisedAsItsDocument_ItsButtonSayingWhatItDoes_AndTheSummaryLeadsBackIntoIt()
    {
        var view = OpenFileVia(_folder, PackageName, "ShowMarkdownAction", "MarkdownView");
        Assert.IsNotNull(view, "MarkdownView did not open the package via the 'Markdown' action.");

        var summarize = CheckPresent("Summarise", "Markdown_Summarize");
        Check("Summarise is offered for a package", () => summarize is not null && summarize.IsEnabled);
        Check("while it summarises, Summarise says so", () =>
            Press(summarize) && WaitFor(() => Leftmost("Markdown_Summarize") is { } button && button.HelpText.Contains("Summarising", StringComparison.OrdinalIgnoreCase) ? button : null, 60) is not null);

    Check("the summary opens beside the package, its title leading back into it", () => Made(() => Back("field-guide", PackageName)));
        Check("each section's heading leads back into the package", () =>
            new[] { "birds", "trees", "the-pond" }.All(anchor => Back(anchor, PackageName) is not null));
        Check("following the pond's heading shows the pond in the package", () =>
            Back("the-pond", PackageName)?.Patterns.Invoke.PatternOrDefault is { } follow && Invoked(follow)
            && WaitFor(() => Link("pond notes") is { IsOffscreen: false } shown ? shown : null, 15) is not null);
        Check("and once it is made, Summarise is offered again", () =>
            WaitFor(() => Leftmost("Markdown_Summarize") is { IsEnabled: true } button ? button : null, 30) is not null);

        AssertJourney();
    }

    /// <summary>
    /// Whether what a draft says is the guide translated: its block of code, which only the document itself and its
    /// translation hold — the summary says what the document says — and not the birds' English, which only the document does.
    /// </summary>
    private static bool IsTranslation(string said) => said.Contains("girth: 4.1 m") && !said.Contains("Robins and blackbirds");

    /// <summary>
    /// Waits for what a background job makes to show — <paramref name="shown"/> — and stops waiting as soon as a message says the
    /// job could not finish, writing what it says to the run's output, so a failed job is read rather than waited out.
    /// </summary>
    private bool Made(Func<AutomationElement?> shown)
    {
        string said = "";
        var found = WaitFor(() => shown() is { } made ? made : (said = Messages()).Length > 0 ? MainWindow : null, Affordable(600));
        if (said.Length > 0) System.Console.WriteLine($"[messages] {said}");
        return found is not null && said.Length == 0;
    }

    /// <summary>What the shell's messages say, each one's lines joined; empty where there are none.</summary>
    private string Messages() => string.Join(" | ",
        MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem))
                  .Where(item => item.Name.EndsWith("NotificationItem", StringComparison.Ordinal))
                  .Select(item => string.Join(" — ", item.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
                                                         .Select(edit => edit.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault ?? edit.Name))));

    /// <summary>A link of a document in view, by its words.</summary>
    private AutomationElement? Link(string words) => Links().FirstOrDefault(link => link.Name.Contains(words, StringComparison.OrdinalIgnoreCase));

    /// <summary>A summary's heading link back to the section <paramref name="anchor"/> names, in the document summarised.</summary>
    private AutomationElement? Back(string anchor, string document = FileName) => Links().FirstOrDefault(link =>
        link.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault is { } target
        && target.EndsWith(document + "#heading%3A" + anchor, StringComparison.OrdinalIgnoreCase));

    private AutomationElement[] Links() => MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Hyperlink));

    /// <summary>What <paramref name="view"/> holds, written out — its view slid to Source to read it.</summary>
    private string? Source(AutomationElement view)
    {
        var slider = view.FindFirstDescendant(cf => cf.ByAutomationId("Markdown_ViewMode"));
        try { slider?.Patterns.RangeValue.Pattern.SetValue(2); }
        catch { return null; }

        return WaitFor(() => view.FindFirstDescendant(cf => cf.ByAutomationId("Markdown_SourceBox")), 5)?.AsTextBox().Text.Replace("\r\n", "\n");
    }

    /// <summary>The element of <paramref name="automationId"/> furthest left — the document's own, beside what it made.</summary>
    private AutomationElement? Leftmost(string automationId) => InView(automationId).OrderBy(element => element.BoundingRectangle.Left).FirstOrDefault();

    /// <summary>The element of <paramref name="automationId"/> furthest right — what was made last, beside the document.</summary>
    private AutomationElement? Rightmost(string automationId) => InView(automationId).OrderByDescending(element => element.BoundingRectangle.Left).FirstOrDefault();

    private IEnumerable<AutomationElement> InView(string automationId) =>
        MainWindow.FindAllDescendants(cf => cf.ByAutomationId(automationId)).Where(element => !element.IsOffscreen);

    /// <summary>Presses <paramref name="button"/> as a user would: invoked, toggled where it opens a popup, clicked failing both.</summary>
    private static bool Press(AutomationElement? button)
    {
        if (button is null) return false;
        if (button.Patterns.Invoke.PatternOrDefault is { } invoke) return Invoked(invoke);

        try
        {
            if (button.Patterns.Toggle.PatternOrDefault is { } toggle) toggle.Toggle();
            else button.Click();
            return true;
        }
        catch { return false; }
    }

    private static bool Invoked(FlaUI.Core.Patterns.IInvokePattern invoke)
    {
        try { invoke.Invoke(); return true; }
        catch { return false; }
    }
}
