using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.UI;

/// <summary>
/// Opening the modal Options overlay and reaching a section inside it, for any journey that needs one.
/// </summary>
/// <remarks>
/// Lifted out of <see cref="OptionsJourneyTests"/> so a feature's own Options section is driven by the journey
/// that owns the feature, without a second copy of the scrolling a virtualised section list needs.
/// </remarks>
public abstract class OptionsOverlayJourney : UiJourneyTestBase
{
    /// <summary>Opens the modal overlay and returns its section list.</summary>
    protected AutomationElement OpenOptions()
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
    protected void CloseOptions() => CheckInvoke("Options button (close)", "Chrome_OptionsButton");

    /// <summary>Cancel, then the chrome toggle if Cancel did not close it — the panel's own dismissal is what
    /// gets exercised, and a modal left open still cannot survive the test.</summary>
    protected void CancelOptions()
    {
        CheckDoes("Cancel closes the Options overlay", "Options_Cancel",
                  () => WaitForId("Chrome_OptionsPanel", 3) is null);
        if (WaitForId("Chrome_OptionsPanel", 1) is not null) CloseOptions();
    }

    /// <summary>
    /// A section row's visible label. The row's own automation Name is not dependable: the list binds
    /// view-models through an ItemTemplate, so a container may report the view-model rather than the text
    /// the user reads. The TextBlock inside it is the thing that actually shows the section name.
    /// </summary>
    protected static string Label(AutomationElement row)
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
    /// section. Querying the children once finds everything except the row a journey most needs.
    /// </para>
    /// </summary>
    /// <param name="contentId">An id the section shows once selected, accepted as proof it was reached
    /// when the row itself does not report its label.</param>
    protected void SelectSection(AutomationElement list, string label, string? contentId = null)
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
                 || (contentId is not null && WaitForId(contentId, 3) is not null));
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
}
