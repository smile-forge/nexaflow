using System.Globalization;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Voice;

/// <summary>What a voice conversation shows while it says an action and waits to be stopped: the sentence, and what
/// the two buttons say. Held — announced before instructions the user is still giving — it is shown in red with only
/// its stop, and not said.</summary>
public sealed record VoiceConfirmation(string Text, string ProceedLabel, string StopLabel, bool IsHeld = false);

/// <summary>Where a voice conversation shows what it is about to do, with a button to go ahead and one to stop.</summary>
public interface IVoiceConfirmations
{
    /// <summary>Shows it until a button answers — true to go ahead, false to stop — or <paramref name="ct"/> takes it
    /// away because it was answered some other way.</summary>
    Task<bool> ShowAsync(VoiceConfirmation confirmation, CancellationToken ct);
}

/// <summary>
/// What a voice conversation says about a batch of actions that would otherwise be put up for approval, in fixed words
/// rather than the model's: "Ok. I will delete file test.zip." — then "File deleted.", or "Aborting delete. The file is
/// unchanged." While it runs it is "Deleting file test.zip". A tool that does not say how it is announced is named by
/// its name.
/// </summary>
internal sealed record AnnouncedActions(string Announcement, string Action, string Doing, string Done, string Unchanged,
                                        IReadOnlyList<ToolCallAnnouncement> Calls)
{
    public static AnnouncedActions Of(IReadOnlyList<ToolCall> batch)
    {
        var calls = batch.Select(c => c.Announcement ?? Unannounced(c)).ToList();
        string and = Str.Get("Shell.Voice.And");
        var actions = calls.Select(a => a.Action).Distinct().ToList();
        return new AnnouncedActions(
            Str.Format("Shell.Voice.AnnounceFormat", string.Join(and, calls.Select(a => $"{a.Action} {a.Target}"))),
            actions.Count == 1 ? actions[0] : Str.Get("Shell.Voice.Proceed"),
            string.Join(and, calls.Select(a => a.Doing)),
            string.Join(" ", calls.Select(a => a.Done).Distinct()),
            string.Join(" ", calls.Select(a => a.Unchanged).Distinct()),
            calls);
    }

    /// <summary>A plan: announced by its title, and carried on by the model once it goes ahead.</summary>
    public static AnnouncedActions OfPlan(ClientPlan plan)
    {
        var call = new ToolCallAnnouncement(Str.Get("Shell.Voice.Carry"), plan.Title, plan.Title, Str.Get("Shell.Voice.Done"),
                                            Str.Get("Shell.Voice.NothingChanged"));
        return new AnnouncedActions(Str.Format("Shell.Voice.AnnounceFormat", $"{call.Action} {call.Target}"), call.Action,
                                    call.Doing, call.Done, call.Unchanged, [call]);
    }

    private static ToolCallAnnouncement Unannounced(ToolCall call)
    {
        string name = call.Tool.Replace('_', ' ');
        return new(Str.Get("Shell.Voice.Run"), name, Str.Format("Shell.Voice.RunningFormat", name), Str.Get("Shell.Voice.Done"),
                   Str.Get("Shell.Voice.NothingChanged"));
    }

    /// <summary>The go-ahead button: the verb, as a label.</summary>
    public string ProceedLabel => Action.Length == 0 ? Action : char.ToUpper(Action[0], CultureInfo.CurrentCulture) + Action[1..];

    public string Stopped => Str.Format("Shell.Voice.AbortStoppedFormat", Action, Unchanged);

    public string NotUnderstood => Str.Format("Shell.Voice.AbortUnclearFormat", Action);

    /// <summary>Shown in place of the announcement while the user adds instructions: "First action - Deleting file
    /// test.zip" for one announced before they began, "Next action - …" for one that came of them.</summary>
    public string Held(bool first) => Str.Format(first ? "Shell.Voice.HeldFirstFormat" : "Shell.Voice.HeldNextFormat", Doing);

    /// <summary>What is said once it has run: the done sentence, or what failed and why.</summary>
    public string After(IReadOnlyList<ToolRunResult> results)
    {
        // The batch's results come back in the order its calls were made.
        var failed = results.Select((r, i) => (Result: r, Call: i < Calls.Count ? Calls[i] : Unannounced(new ToolCall(r.Tool, []))))
                            .Where(x => x.Result.Result.IsError || !x.Result.Result.Success)
                            .ToList();
        if (failed.Count == 0) return Done;
        return string.Join(" ", failed.Select(f => Str.Format("Shell.Voice.FailedFormat", f.Call.Action, f.Call.Target, f.Result.Result.Summary)));
    }
}
