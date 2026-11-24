using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace Nexaflow.Tests.UIJourneys.Infrastructure;

/// <summary>
/// A feature's journey is one test and one app launch made of <b>steps</b>: each a scenario of its own, run in the
/// app that is already up, so that one broken scenario costs the journey that step and nothing after it.
/// </summary>
public abstract partial class UiJourneyTestBase
{
    /// <summary>Every tab in the strip as <c>TabItem_&lt;PageKind&gt;</c> ids, one entry per tab, taken before the first step.</summary>
    private List<string>? _baselineTabs;

    private int _stepsRun;
    private readonly List<string> _stepsSkipped = [];

    private const string TabItemPrefix = "TabItem_";
    private const string CloseTabPrefix = "CloseTab_";

    /// <summary>
    /// Runs one scenario of the journey. A failed assertion, an exception or a stop through <c>Assert.Inconclusive</c>
    /// ends that scenario and nothing else: it is recorded under the step's name and the next step runs, where an
    /// assertion in a journey's own body would have ended the whole journey. The journey's budget starts over with each
    /// step, so a long scenario cannot starve the ones after it, and the app is brought back to where the journey began
    /// (<see cref="ReturnToBaseline"/>) before the next one starts.
    /// </summary>
    /// <param name="name">What the scenario shows — it prefixes every failure the step records and names its row in the timings.</param>
    /// <param name="body">The scenario: checks and, where one needs a thing to exist first, assertions.</param>
    protected void RunStep(string name, Action body)
    {
        _baselineTabs ??= TabIdsOnScreen();
        _clock = null;
        _budgetReported = false;

        var failuresBefore = _failures.Count;
        var checksBefore = _checks;
        var timer = Stopwatch.StartNew();
        try { body(); }
        catch (AssertInconclusiveException ex) { _stepsSkipped.Add($"{name}: {ex.Message}"); }
        catch (Exception ex) { _failures.Add($"{ex.GetType().Name}: {ex.Message}"); }
        timer.Stop();

        for (var i = failuresBefore; i < _failures.Count; i++) _failures[i] = $"[{name}] {_failures[i]}";
        _stepsRun++;
        JourneyTimings.Record(GetType().Name, $"{TestContext.TestName}/{name}", _checks - checksBefore, timer.Elapsed, _failures.Count - failuresBefore);

        try { ReturnToBaseline(); }
        catch (Exception ex) { _failures.Add($"[{name}] could not return to the journey's starting state: {ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>
    /// Puts the app back where the journey began, so one step's leftovers — an open question, the Options overlay, a menu, the
    /// tabs it opened — cannot reach the next: the shell's modal question is answered no, Options is closed, an open popup is
    /// dismissed, the tabs the step opened are closed (discarding what they hold unsaved), and the journey's own tab is brought forward.
    /// </summary>
    protected void ReturnToBaseline()
    {
        var no = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Chrome_ConfirmCancel").Or(cf.ByAutomationId("ShellPromptCancel")));
        if (no is not null) Press(no);

        if (MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Chrome_OptionsPanel")) is not null
            && MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Chrome_OptionsButton")) is { } options)
            Press(options);

        if (OpenPopups().Any()) Keyboard.Type(VirtualKeyShort.ESCAPE);

        var opened = TabIdsOnScreen();
        foreach (var id in _baselineTabs ?? [])
            opened.Remove(id);
        foreach (var id in opened)
        {
            var close = MainWindow.FindAllDescendants(cf => cf.ByAutomationId(CloseTabPrefix + id[TabItemPrefix.Length..])).LastOrDefault();
            if (close is null) continue;
            Press(close);

            // A tab with edits unsaved asks first: what a scenario left behind is discarded, never kept for the next.
            Thread.Sleep(250);
            if (MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Chrome_ConfirmAlternate")) is { } discard) Press(discard);
        }

        var home = LaunchTabKind is { } kind ? TabItemPrefix + kind : TabItemPrefix + "FileSystem";
        if (MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(home)) is { } tab && !IsSelected(tab)) Press(tab);
    }

    /// <summary>
    /// Opens another page in the running app through the slash palette — <c>/query</c>, Enter — the way a user reaches a page that is
    /// not on the ribbon, and waits for its root view. Null when it never appeared; the step's own check says what that means.
    /// </summary>
    /// <param name="slashQuery">What is typed after the slash: enough of the page's name for it to be the palette's first match.</param>
    /// <param name="viewId">The AutomationId of the page's root view.</param>
    protected AutomationElement? OpenPageViaSlash(string slashQuery, string viewId, int seconds = 10)
    {
        if (WaitForId(viewId, 1) is { } open) return open;

        var bar = WaitForId("AiInputBox", 10);
        if (bar is null) return null;
        bar.Click();
        Keyboard.Type("/" + slashQuery);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(500);                  // the palette's rebuild is debounced off the keystroke
        Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
        return WaitForId(viewId, Affordable(seconds));
    }

    /// <summary>Says what the steps skipped, and ends the journey inconclusive when every one of them did.</summary>
    private void ReportSkippedSteps()
    {
        if (_stepsSkipped.Count == 0) return;

        foreach (var skipped in _stepsSkipped) Console.WriteLine($"[skipped] {skipped}");
        if (_failures.Count == 0 && _stepsSkipped.Count == _stepsRun)
            Assert.Inconclusive("Every step was skipped:\n  - " + string.Join("\n  - ", _stepsSkipped));
    }

    /// <summary>The page-kind tab ids on screen, one per tab, so two tabs of one kind count twice.</summary>
    private List<string> TabIdsOnScreen() =>
        MainWindow.FindAllDescendants()
                  .Select(e => e.Properties.AutomationId.ValueOrDefault ?? string.Empty)
                  .Where(id => id.StartsWith(TabItemPrefix, StringComparison.Ordinal))
                  .ToList();

    private static bool IsSelected(AutomationElement tab) =>
        tab.Patterns.SelectionItem.PatternOrDefault is { } item && item.IsSelected.ValueOrDefault;

    /// <summary>Presses a control the way a person does: its invoke pattern where it has one, a click where it has not.</summary>
    private static void Press(AutomationElement element)
    {
        if (element.Patterns.Invoke.IsSupported) element.AsButton().Invoke();
        else element.Click();
        Wait.UntilInputIsProcessed();
    }
}
