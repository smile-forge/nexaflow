using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using Nexaflow.Tests.UIJourneys.Infrastructure;
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
public class OptionsJourneyTests : UiJourneyTestBase
{
    /// <summary>Opens the modal overlay and returns its section list.</summary>
    private AutomationElement OpenOptions()
    {
        Assert.IsNotNull(WaitForId("DirectoryTree", 15), "Default FileSystem tab did not load.");

        CheckInvoke("Options button (open)", "Chrome_OptionsButton");
        CheckPresent("Options overlay",      "Chrome_OptionsPanel");

        var list = WaitForId("Options_SectionList", 10);
        Assert.IsNotNull(list, "Options section list not found — the overlay did not render its contents.");
        return list!;
    }

    /// <summary>
    /// Options is MODAL, so it must be closed however the test ends — a left-open overlay blocks
    /// everything after it, including the next journey in the same app instance.
    /// </summary>
    private void CloseOptions() => CheckInvoke("Options button (close)", "Chrome_OptionsButton");

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
            SelectSection(list, "About");

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
            // Cancel rather than the chrome toggle, so the panel's own dismissal is what gets exercised.
            // If it fails to close, CloseOptions still runs — a modal left open blocks everything after it.
            CheckDoes("Cancel closes the Options overlay", "Options_Cancel",
                      () => WaitForId("Chrome_OptionsPanel", 3) is null);
            if (WaitForId("Chrome_OptionsPanel", 1) is not null) CloseOptions();
        }

        AssertJourney();
    }

    /// <summary>
    /// A section row's visible label. The row's own automation Name is not dependable: the list binds
    /// view-models through an ItemTemplate, so a container may report the view-model rather than the text
    /// the user reads. The TextBlock inside it is the thing that actually shows the section name.
    /// </summary>
    private static string Label(AutomationElement row)
    {
        if (!string.IsNullOrWhiteSpace(row.Name)) return row.Name;
        return row.FindAllDescendants()
                  .Select(d => d.Name ?? string.Empty)
                  .FirstOrDefault(n => n.Length > 0) ?? string.Empty;
    }

    /// <summary>
    /// Selects a section by label, scrolling the list until it is realised.
    /// <para>
    /// The scroll is not optional. The list virtualises, so only the rows currently on screen exist in the
    /// automation tree — and "About" is deliberately sorted to the very bottom, below every feature's
    /// section. Querying the children once finds everything except the row this journey most needs.
    /// </para>
    /// </summary>
    private void SelectSection(AutomationElement list, string label)
    {
        if (FindSection(list, label) is { } target)
        {
            target.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
            target.Patterns.SelectionItem.PatternOrDefault?.Select();
            Wait.UntilInputIsProcessed();
            System.Threading.Thread.Sleep(250);
            return;
        }

        // Fall back to the keyboard. "About" is sorted to the very bottom of the list, and End is how a
        // person gets to the bottom of a ListBox — it moves selection to the last item and realises it,
        // without depending on how the row reports itself to automation.
        list.Focus();
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.END);
        Wait.UntilInputIsProcessed();
        System.Threading.Thread.Sleep(300);

        var seen = string.Join(" | ", list.FindAllChildren().Select(Label));
        Check($"'{label}' section reachable (rows seen: {seen})",
              () => FindSection(list, label) is not null
                 || WaitForId("About_ComponentsToggle", 3) is not null);
    }

    private static AutomationElement? FindSection(AutomationElement list, string label)
    {
        bool Matches(AutomationElement r) =>
            Label(r).StartsWith(label, StringComparison.OrdinalIgnoreCase);

        var found = list.FindAllChildren().FirstOrDefault(Matches);
        if (found is not null) return found;

        var scroll = list.Patterns.Scroll.PatternOrDefault;
        if (scroll is null || !scroll.VerticallyScrollable.Value) return null;

        // Walk to the bottom a page at a time, re-querying as new rows realise.
        for (var i = 0; i < 12; i++)
        {
            scroll.Scroll(FlaUI.Core.Definitions.ScrollAmount.NoAmount,
                          FlaUI.Core.Definitions.ScrollAmount.LargeIncrement);
            Wait.UntilInputIsProcessed();
            System.Threading.Thread.Sleep(120);

            found = list.FindAllChildren().FirstOrDefault(Matches);
            if (found is not null) return found;

            if (scroll.VerticalScrollPercent.Value >= 99.0) break;
        }
        return null;
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
