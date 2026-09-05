using System;
using System.Collections.Generic;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using System.Diagnostics;

namespace Nexaflow.Tests.UIJourneys.Infrastructure;

/// <summary>
/// Base for per-section UI "journey" tests: launch the app once (via <see cref="UITestBase"/>), open a
/// feature, then exercise every control of that section in a single pass — amortising the ~20s app launch.
/// <para>
/// Control checks are <b>soft</b>: each is recorded via <see cref="CheckPresent"/>/<see cref="CheckInvoke"/>/
/// <see cref="Check"/>, and <see cref="AssertJourney"/> fails once at the end listing every miss. So a single
/// un-tagged or broken control doesn't abort the rest of the section's coverage.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
public abstract class UiJourneyTestBase : FileSystemUiTestBase
{
    private readonly List<string> _failures = [];
    private int _checks;

    /// <summary>
    /// Opens <paramref name="fileName"/> in a specific viewer by selecting it in the file browser and
    /// clicking its ActionStrip action (by <paramref name="actionDisplayName"/>) — deterministic, unlike a
    /// double-click which only fires the <i>default</i> file-type mapping. Returns the viewer root (or null).
    /// </summary>
    protected AutomationElement? OpenFileVia(string folder, string fileName, string actionDisplayName,
                                             string viewerAutomationId, int seconds = 15)
    {
        NavigateFileBrowserTo(folder);

        var row = WaitForName(fileName, 8);
        Assert.IsNotNull(row, $"File '{fileName}' not found in the file list.");
        row!.Click();                       // select → ActionStrip lists this file's actions
        Wait.UntilInputIsProcessed();
        System.Threading.Thread.Sleep(200);

        var action = WaitForId(actionDisplayName, 6);
        Assert.IsNotNull(action, $"Action '{actionDisplayName}' not found in the ActionStrip for '{fileName}'.");
        action!.AsButton().Invoke();
        Wait.UntilInputIsProcessed();

        return WaitForId(viewerAutomationId, seconds);
    }

    // ── Soft checks ──────────────────────────────────────────────────────────

    /// <summary>
    /// How long a journey may spend <b>waiting</b> on controls before it stops paying for waits.
    /// <para>
    /// Per-check timeouts are additive, and a journey walking a broken page pays every one of them. The
    /// Search journey's fourteen 60-second waits stack into roughly a quarter of an hour of a machine
    /// sitting apparently idle before it says anything at all — so the soft-check report, which is the
    /// entire point of checking softly, arrives long after anyone has stopped watching and hit Ctrl+C.
    /// Past the budget each remaining check gets one look instead of a wait: still truthful about a
    /// control that is there, no longer paying a minute to confirm one that is not.
    /// </para>
    /// The longest healthy journey is comfortably under a minute, so this only bites once something is
    /// already wrong. Override it for a journey that genuinely waits on real work.
    /// </summary>
    protected virtual TimeSpan JourneyBudget => TimeSpan.FromSeconds(120);

    /// <summary>Started at the first check, so the app launch is not charged to the journey's budget.</summary>
    private Stopwatch? _clock;
    private bool _budgetReported;

    /// <summary>
    /// <paramref name="wanted"/> seconds, or what is left of <see cref="JourneyBudget"/> — whichever is
    /// less, and never less than one. A single look still distinguishes a control that is present from
    /// one that is missing; a zero would report every remaining control missing without looking at all.
    /// <para>Wrap any wait a journey performs for itself, so its own loops are budgeted like the checks.</para>
    /// </summary>
    protected int Affordable(int wanted)
    {
        _clock ??= Stopwatch.StartNew();
        var left = (int)(JourneyBudget - _clock.Elapsed).TotalSeconds;
        if (left >= wanted) return wanted;

        if (left <= 0 && !_budgetReported)
        {
            _budgetReported = true;
            _failures.Add($"Journey budget of {JourneyBudget.TotalSeconds:0}s spent — every check below got " +
                          "one look rather than its full wait, so read them as consequences of the first miss.");
        }
        return Math.Max(1, left);
    }

    /// <summary>Records whether a control with <paramref name="automationId"/> is present + on-screen.</summary>
    protected AutomationElement? CheckPresent(string label, string automationId, int seconds = 5)
    {
        _checks++;
        var el = WaitForId(automationId, Affordable(seconds));
        if (el is null) _failures.Add($"{label}: control '{automationId}' not found.");
        return el;
    }

    /// <summary>
    /// Finds a control, invokes it, and requires it to have an observable <paramref name="effect"/> —
    /// the form every interactive check should take.
    /// <para>
    /// <see cref="CheckInvoke"/> only proves a control can be clicked without throwing, which a control that
    /// does nothing at all also satisfies. Prefer this: state what pressing it should change (the document
    /// re-rendered, the panel opened, the row disappeared) and let the journey fail when it doesn't.
    /// </para>
    /// </summary>
    protected void CheckDoes(string label, string automationId, Func<bool> effect, int seconds = 5)
    {
        if (CheckInvoke(label, automationId, seconds) is null) return;   // absence already recorded
        _checks++;
        try
        {
            if (!effect()) _failures.Add($"{label}: '{automationId}' was invoked but had no observable effect.");
        }
        catch (Exception ex)
        {
            _failures.Add($"{label}: checking the effect of '{automationId}' threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Finds a control and invokes (or clicks) it, recording any failure. Returns the element if found.
    /// <para>
    /// A <b>disabled</b> control is a failure, not a skip: pressing it could not do anything, so invoking it
    /// proves nothing. If the journey means to assert a control is unavailable in this state, assert that
    /// directly with <see cref="Check"/> instead of invoking it.
    /// </para>
    /// Prefer <see cref="CheckDoes"/> — this on its own does not assert the click achieved anything.
    /// </summary>
    protected AutomationElement? CheckInvoke(string label, string automationId, int seconds = 5)
    {
        var el = CheckPresent(label, automationId, seconds);
        if (el is null) return null;
        if (!el.IsEnabled)
        {
            _failures.Add($"{label}: '{automationId}' is disabled — invoking it cannot do anything.");
            return el;
        }
        try
        {
            if (el.Patterns.Invoke.IsSupported) el.AsButton().Invoke();
            else el.Click();
            Wait.UntilInputIsProcessed();
            System.Threading.Thread.Sleep(150);
        }
        catch (Exception ex)
        {
            _failures.Add($"{label}: invoking '{automationId}' threw {ex.GetType().Name}: {ex.Message}");
        }
        if (App.HasExited) _failures.Add($"{label}: app exited after invoking '{automationId}'.");
        return el;
    }

    /// <summary>Runs an arbitrary check, recording a failure if it throws or returns false.</summary>
    protected void Check(string label, Func<bool> assertion)
    {
        _checks++;
        try { if (!assertion()) _failures.Add($"{label}: assertion returned false."); }
        catch (Exception ex) { _failures.Add($"{label}: threw {ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>Asserts the whole journey passed — call once at the end of each journey test method.</summary>
    protected void AssertJourney()
    {
        Assert.IsFalse(App.HasExited, "App exited during the journey.");
        if (_failures.Count > 0)
            Assert.Fail($"{_failures.Count}/{_checks} control checks failed:\n  - " + string.Join("\n  - ", _failures));
    }
}
