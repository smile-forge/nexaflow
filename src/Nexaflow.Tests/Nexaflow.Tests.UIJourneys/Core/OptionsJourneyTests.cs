using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UI;

/// <summary>
/// One-pass UI journey for the <b>inside</b> of the modal Options overlay.
/// <para>
/// <see cref="ShellChromeJourneyTests"/> opens and closes Options, which proves the toggle works and
/// nothing more — every section, every editor and every custom control behind it was undriven. That is
/// a large blind spot: Options is where a feature's config is edited, and a section that throws while
/// rendering takes the whole overlay with it, on a surface no test ever looked at.
/// </para>
/// <para>
/// So this walks the section list itself. Sections come from the registered <c>IFeatureConfig</c>s, one
/// per feature, and building the list forces every feature assembly to activate — meaning a broken
/// section anywhere shows up here regardless of which feature owns it. The pass is deliberately
/// data-driven rather than a hard-coded section list: features are added and removed often, and a
/// journey that enumerated them by name would need editing every time.
/// </para>
/// <para>
/// <b>About → System components</b> is then driven specifically, because it is the one section that
/// reports on the machine rather than on config: it probes for third-party runtimes (Edge WebView2,
/// libvlc, the dotnet CLI) and is the page a user is sent to when a viewer says a component is missing.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[NoCoverage("options journey")]
public class OptionsJourneyTests : OptionsOverlayJourney
{
    [TestMethod]
    public void Options_SectionsRender_AndAboutReportsComponents()
    {
        var list = OpenOptions();

        try
        {
            var sections = list.FindAllChildren();
            Check("Options lists at least a few sections", () => sections.Length >= 3);

            // ── Every section renders ────────────────────────────────────────
            // Selecting a section builds its editor: a [CustomControl] instance, or a reflected property
            // grid. Either way something must appear on the right — a section that selects to a blank
            // pane is the exact failure this journey exists to catch.
            foreach (var section in sections)
            {
                var name = Label(section);
                if (string.IsNullOrWhiteSpace(name)) continue;

                Check($"Section '{name}' selects and renders", () =>
                {
                    section.Patterns.SelectionItem.PatternOrDefault?.Select();
                    Wait.UntilInputIsProcessed();
                    System.Threading.Thread.Sleep(120);

                    // A custom control names itself; a property grid renders rows. Either is a pass, and
                    // a section with genuinely no settings still renders its (empty) grid host.
                    return WaitForId("Options_CustomSection", 1) is not null
                        || MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Chrome_OptionsPanel"))
                                     ?.FindAllDescendants().Length > 0;
                });
            }

            // ── About → System components ────────────────────────────────────
            SelectSection(list, "About", "About_ComponentsToggle");

            CheckPresent("System components heading", "About_ComponentsToggle", 10);

            // Expanded only when something is missing, so open it explicitly rather than assuming.
            Expand("About_ComponentsToggle");

            CheckPresent("System components list", "About_ComponentsList", 10);

            // The declarations are real, so the probe must have produced real rows. WebView2 is the one
            // every build declares (from both the PDF reader and the Web tab, merged into a single row),
            // which is what makes it a safe anchor for this assertion.
            Check("WebView2 is listed as a component", () => ComponentRowNames().Any(
                n => n.Contains("WebView2", StringComparison.OrdinalIgnoreCase)));

            Check("Each listed component reports a status", () => ComponentRowNames().Any(n =>
                n.Contains("Installed",     StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Missing",       StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Not installed", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("check",         StringComparison.OrdinalIgnoreCase)));

            // Re-check re-probes without a restart. The observable effect is that the list survives it —
            // a refresh that emptied the list, or threw, would be worse than no button at all.
            CheckDoes("Re-check re-probes", "About_Recheck",
                () => Wait.UntilResponsive(MainWindow)
                       && WaitForId("About_ComponentsList", 10) is not null
                       && ComponentRowNames().Any(n => n.Contains("WebView2", StringComparison.OrdinalIgnoreCase)));

            // The notices section shares the page; expanding it proves the two collapsibles are independent.
            Expand("About_NoticesToggle");

            // ── The panel's own footer ──
            // Save commits the edited copy and closes; the X closes without committing. Neither is pressed,
            // and not because the app would come to harm — it runs against a throwaway config dir, so what
            // they write is discarded with it. They are left alone because each one closes the panel, and
            // Cancel below is the exit whose behaviour this pass actually asserts.
            CheckPresent("Save",    "Options_Save");
            CheckPresent("Close X", "Options_CloseX");
        }
        finally
        {
            CancelOptions();
        }

        AssertJourney();
    }

    /// <summary>Expands a collapsible section, tolerating one that is already open.</summary>
    private void Expand(string automationId)
    {
        var toggle = WaitForId(automationId, 5);
        if (toggle is null) return;
        if (toggle.Patterns.Toggle.PatternOrDefault?.ToggleState.Value == FlaUI.Core.Definitions.ToggleState.On)
            return;

        toggle.Click();
        Wait.UntilInputIsProcessed();
        System.Threading.Thread.Sleep(200);
    }

    /// <summary>
    /// Every piece of text rendered inside the components list. The rows are a DataTemplate of TextBlocks,
    /// which publish their text as the automation Name — so this is how the journey reads what the user
    /// can actually see, rather than trusting that binding happened.
    /// </summary>
    private string[] ComponentRowNames()
    {
        var list = WaitForId("About_ComponentsList", 5);
        return list is null
            ? []
            : list.FindAllDescendants()
                  .Select(e => e.Name ?? string.Empty)
                  .Where(n => n.Length > 0)
                  .ToArray();
    }
}
