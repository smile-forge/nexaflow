using System;
using System.Threading;
using System.Windows;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.UIJourneys.Infrastructure;

/// <summary>
/// Asks once, before the first UI test launches anything, whether it may take the machine.
/// <para>
/// A FlaUI journey drives the real mouse and keyboard: it raises windows, clicks by screen position and
/// types. Started while someone is working, it both interrupts them and flakes itself — a click meant for
/// the app lands wherever focus actually went, which is the likeliest explanation for the intermittent
/// first-run failures on this suite. A single confirmation turns "the tests ambushed me" into a choice.
/// </para>
/// <para>
/// Asked at the first launch rather than at assembly load, so running only headless tests never
/// prompts. The answer is remembered: decline and every UI test in the run reports inconclusive
/// instead of re-asking.
/// </para>
/// <para>
/// Remembered <b>across test hosts</b>, not just within one — a whole-suite run starts a process per
/// project and three of them carry UI tests, so a per-process answer meant three prompts for one
/// decision. <see cref="UiTestGate.RecordedConsent"/> holds the shared answer.
/// </para>
/// </summary>
public static class UiTakeoverPrompt
{
    private enum Answer { Unasked, Allowed, Declined }

    private static Answer _answer;
    private static readonly Lock Gate = new();

    /// <summary>Set to 1/true to skip the prompt — for CI, or for a deliberately unattended local run.</summary>
    public const string SkipVariable = "NEXAFLOW_UITESTS_NOPROMPT";

    /// <summary>Throws an inconclusive result if the machine's owner declined. Safe to call on every launch.</summary>
    public static void EnsureAllowed()
    {
        lock (Gate)
        {
            if (_answer == Answer.Unasked)
            {
                // A sibling host may already have asked for this run — take its answer over asking again.
                _answer = UiTestGate.RecordedConsent switch
                {
                    true  => Answer.Allowed,
                    false => Answer.Declined,
                    null  => Ask(),
                };
            }
        }

        if (_answer == Answer.Declined)
            Assert.Inconclusive(
                "UI tests were declined for this run — they take over the mouse and keyboard. "
                + $"Re-run when the machine is free, or set {SkipVariable}=1 to skip this prompt.");
    }

    private static Answer Ask()
    {
        if (!ShouldPrompt()) return Answer.Allowed;

        // MessageBox needs an STA thread, and the MSTest runner's is not one. Its own thread also keeps the
        // dialog off whatever apartment the test happens to be on, so this cannot deadlock the runner.
        var result = MessageBoxResult.No;
        var ui = new Thread(() => result = MessageBox.Show(
            "The UI tests are about to take over this machine's mouse and keyboard.\n\n"
            + "They launch Nexaflow, raise its window and click by screen position, so anything you type "
            + "or click meanwhile can land in the wrong place — and can fail the run.\n\n"
            + "Start them now?",
            "Nexaflow UI tests",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes))
        {
            IsBackground = true,
        };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();

        // Never hang a run on an absent human: an unanswered prompt after two minutes means nobody is
        // there to be disturbed, so proceed rather than stall the suite forever.
        if (!ui.Join(TimeSpan.FromMinutes(2)))
        {
            // Nobody answered, so nobody is there to disturb. Don't record it: an absent human is a fact
            // about this moment, not a decision the rest of the run should inherit.
            return Answer.Allowed;
        }

        var allowed = result == MessageBoxResult.Yes;
        UiTestGate.RecordConsent(allowed);          // the other hosts read this instead of re-asking
        return allowed ? Answer.Allowed : Answer.Declined;
    }

    /// <summary>Prompt only where a person could actually be interrupted.</summary>
    private static bool ShouldPrompt()
    {
        if (IsSet(SkipVariable)) return false;
        if (!Environment.UserInteractive) return false;               // service / session 0
        // The usual build agents. They run interactive, so UserInteractive alone would not catch them.
        foreach (var ci in (string[])["CI", "TF_BUILD", "GITHUB_ACTIONS", "JENKINS_URL", "TEAMCITY_VERSION"])
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ci)))
                return false;
        return true;
    }

    private static bool IsSet(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v
        && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
}
