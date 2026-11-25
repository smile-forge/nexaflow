using System;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using Nexaflow.Tests.Fixtures;
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
    /// <summary>Opens the modal overlay and returns its section list, once the page the app launched into has loaded.</summary>
    /// <param name="launchedViewId">That page's view: the file browser's tree for the default tab.</param>
    protected AutomationElement OpenOptions(string launchedViewId = "DirectoryTree")
    {
        Assert.IsNotNull(WaitForId(launchedViewId, 15), $"The tab the app launched into ({launchedViewId}) did not load.");

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

    /// <summary>Expands a collapsible section, tolerating one that is already open.</summary>
    protected void Expand(string automationId)
    {
        var toggle = WaitForId(automationId, 5);
        if (toggle is null) return;
        if (toggle.Patterns.Toggle.PatternOrDefault?.ToggleState.Value == FlaUI.Core.Definitions.ToggleState.On)
            return;

        toggle.Click();
        Wait.UntilInputIsProcessed();
        System.Threading.Thread.Sleep(200);
    }

    /// <summary>What About → System components says of the component named <paramref name="name"/> — the line beside its name; null
    /// while the list is not shown or holds no such row.</summary>
    protected string? ComponentStatus(string name)
    {
        var row = WaitForId("About_ComponentsList", 5)?.FindAllChildren()
                      .Select(item => item.FindAllChildren().Select(text => text.Name ?? "").ToList())
                      .FirstOrDefault(texts => texts.Contains(name));
        int at = row?.IndexOf(name) ?? -1;
        return at >= 0 && at + 1 < row!.Count ? row[at + 1] : null;
    }
}
