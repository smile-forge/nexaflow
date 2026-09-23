using System;
using System.Collections.Generic;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using System.Diagnostics;
using System.Linq;

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
    protected AutomationElement? OpenFileVia(string folder, string fileName, string actionAutomationId,
                                             string viewerAutomationId, int seconds = 15)
    {
        NavigateFileBrowserTo(folder);

        var row = WaitForName(fileName, 8);
        Assert.IsNotNull(row, $"File '{fileName}' not found in the file list.");
        row!.Click();                       // select → ActionStrip lists this file's actions
        Wait.UntilInputIsProcessed();
        System.Threading.Thread.Sleep(200);

        var action = WaitForId(actionAutomationId, 6);
        Assert.IsNotNull(action, $"Action '{actionAutomationId}' not found in the ActionStrip for '{fileName}'.");
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
        if (BlockedByModalQuestion(automationId)) return null;
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

    /// <summary>
    /// True when the <c>ZoomChip</c> for <paramref name="pagePrefix"/> reads exactly
    /// <paramref name="expected"/> (e.g. "120%").
    /// <para>
    /// Exact, not a substring: "contains 120" also passes on 1200%, and — worse — a check that can pass
    /// without the zoom having moved is the one failure mode a zoom test exists to catch. The label is a
    /// TextBlock, so UIA reports its text as the element's Name.
    /// </para>
    /// </summary>
    protected bool ZoomLabelReads(string pagePrefix, string expected)
    {
        var label = WaitForId($"{pagePrefix}_ZoomLabel", 3);
        return label?.Name == expected;
    }

    /// <summary>
    /// A control by id in the shell window or, failing that, in any other top-level window this app owns — a
    /// Popup (a speed or save menu), a context menu, a fullscreen window. UIA reports each of those as its own
    /// window rather than under the shell. Only the app's windows are searched: a miss is usually being polled
    /// for, and a whole-desktop walk per poll costs seconds. Offscreen matches are returned too.
    /// </summary>
    protected AutomationElement? FindInAppWindows(string automationId) =>
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
        ?? Automation.GetDesktop()
                     .FindAllChildren(cf => cf.ByProcessId(App.ProcessId))
                     .Select(w => w.FindFirstDescendant(cf => cf.ByAutomationId(automationId)))
                     .FirstOrDefault(e => e is not null);

    /// <summary>
    /// Sets a text box's text, soft: false rather than an assertion when the box never appears or will not take
    /// it. Scrolls it into view first, because a box below the fold of a ScrollViewer reads as offscreen and
    /// <see cref="FileSystemUiTestBase.WaitForId"/> skips offscreen elements.
    /// </summary>
    protected bool TypeInto(string automationId, string text)
    {
        if (BlockedByModalQuestion(automationId)) return false;
        var box = WaitFor(() =>
        {
            var el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            try { el?.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView(); } catch { /* not scrollable — fine */ }
            return el;
        }, 8);
        if (box is null) return false;

        box.AsTextBox().Text = text;
        return WaitForFs(() => box.AsTextBox().Text == text, 2);
    }

    // ── Modal questions ───────────────────────────────────────────────────────────
    // The shell's confirmation and prompt are window-modal: while one is open the user can reach nothing
    // else in the window. Every helper below drives a control through its UIA pattern, which needs no
    // mouse and so passes straight through the scrim covering the page. Left unchecked that lets a journey
    // "press" a control nobody could get to, and every check after it reports on a window the user would
    // have been locked out of — which is not a pass, it is the test having stopped describing the app.

    /// <summary>The modal question's own controls: the only ones reachable while it is open.</summary>
    private static readonly string[] ModalOwnIds =
        ["Chrome_ConfirmOk", "Chrome_ConfirmCancel", "ShellPromptOk", "ShellPromptCancel", "ShellPromptBox"];

    /// <summary>
    /// True when a shell confirmation or prompt is open and <paramref name="automationId"/> is no part of
    /// it. Records the failure here, naming the question that was left open, because the enclosing check
    /// can only report that something returned false — and "returned false" is exactly what this looked
    /// like for as long as it went unnoticed.
    /// </summary>
    private bool BlockedByModalQuestion(string automationId)
    {
        if (ModalOwnIds.Contains(automationId, StringComparer.Ordinal)) return false;

        if ((MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Chrome_ConfirmOk"))
          ?? MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ShellPromptOk"))) is null) return false;

        _failures.Add($"'{automationId}' was driven while the shell's modal question was still open. "
                    + "Answer it in the journey first: a user could not have reached this control, so "
                    + "neither should the pass.");
        return true;
    }

    /// <summary>How many controls carry <paramref name="automationId"/> — for an id stamped on every row of a list.</summary>
    protected int CountOf(string automationId) =>
        MainWindow.FindAllDescendants(cf => cf.ByAutomationId(automationId)).Length;

    /// <summary>
    /// Invokes the <paramref name="index"/>th control carrying an id stamped on every row of a list (-1 = the last).
    /// Offscreen and transparent rows included: the pattern needs no mouse.
    /// </summary>
    protected bool PressNth(string automationId, int index)
    {
        if (BlockedByModalQuestion(automationId)) return false;
        var rows = MainWindow.FindAllDescendants(cf => cf.ByAutomationId(automationId));
        var i = index < 0 ? rows.Length - 1 : index;
        if (i < 0 || i >= rows.Length) return false;
        rows[i].AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        return true;
    }

    /// <summary>
    /// True when a control carrying <paramref name="automationId"/> is in the tree at all — on screen or not. For the
    /// editors inside a scrolling pane, where "offscreen" means "below the fold", not "absent".
    /// </summary>
    protected bool Exists(string automationId) =>
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is not null;

    /// <summary><see cref="CheckPresent"/> for a control that may sit below the fold of a scrolling pane.</summary>
    protected void CheckExists(string label, string automationId, int seconds = 5) =>
        Check($"{label} ('{automationId}' exists)", () => WaitForFs(() => Exists(automationId), Affordable(seconds)));

    /// <summary>The rows of the list carrying <paramref name="automationId"/>, or 0 when it is not there.</summary>
    protected int ListCount(string automationId) =>
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
                  ?.FindAllChildren(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem)).Length ?? 0;

    /// <summary>Sets a toggle (ToggleButton, CheckBox) to <paramref name="on"/> through its pattern. True once it reads so.</summary>
    protected bool SetToggle(string automationId, bool on)
    {
        if (BlockedByModalQuestion(automationId)) return false;
        var toggle = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.Patterns.Toggle.PatternOrDefault;
        if (toggle is null) return false;
        var want = on ? FlaUI.Core.Definitions.ToggleState.On : FlaUI.Core.Definitions.ToggleState.Off;
        if (toggle.ToggleState.Value != want) toggle.Toggle();
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => toggle.ToggleState.Value == want, 2);
    }

    /// <summary>
    /// Invokes the first open menu item whose label contains <paramref name="label"/>. A ContextMenu built in code
    /// carries no ids and is its own top-level window, so every window this app owns is searched.
    /// </summary>
    protected bool PickMenuItem(string label)
    {
        var item = WaitFor(() => Automation.GetDesktop()
            .FindAllChildren(cf => cf.ByProcessId(App.ProcessId))
            .SelectMany(w => w.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem)))
            .FirstOrDefault(m => m.Name?.Contains(label, StringComparison.Ordinal) == true), 5);
        if (item is null) return false;
        item.AsMenuItem().Invoke();
        Wait.UntilInputIsProcessed();
        return true;
    }

    /// <summary><see cref="PickMenuItem"/> by AutomationId — for a menu item whose label is translated.</summary>
    protected bool PickMenuItemById(string automationId)
    {
        var item = WaitFor(() => Automation.GetDesktop()
            .FindAllChildren(cf => cf.ByProcessId(App.ProcessId))
            .SelectMany(w => w.FindAllDescendants(cf => cf.ByAutomationId(automationId)))
            .FirstOrDefault(), 5);
        if (item is null) return false;
        item.AsMenuItem().Invoke();
        Wait.UntilInputIsProcessed();
        return true;
    }

    /// <summary>
    /// Presses a button in one of the app's own dialog windows (the file and folder pickers) and waits for the
    /// window to go. The dialog is modal and top-level, so it is searched for outside the shell window.
    /// </summary>
    protected bool PressInDialog(string automationId)
    {
        var button = WaitFor(() => FindInAppWindows(automationId), 5);
        if (button is null) return false;
        button.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => FindInAppWindows(automationId) is null, 5);
    }

    /// <summary>Selects a radio button, in the main window or any of the app's popups.</summary>
    protected bool SelectRadio(string automationId)
    {
        if (FindInAppWindows(automationId)?.Patterns.SelectionItem.PatternOrDefault is not { } radio) return false;
        radio.Select();
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => radio.IsSelected.Value, 3);
    }
}
