using System.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Localization;
using Nexaflow.Providers.Common;
using Nexaflow.Session.AI;

namespace Nexaflow.Assistant.Requests;

/// <summary>
/// The requests one window's AI bar sends, and how each is answered. A query handler that clearly owns the words answers them;
/// otherwise the workspace's model picks a handler, and failing that the agent answers. A turn about a page that shows the assistant
/// itself is answered on that page; any other in the bar. Words sent while a run is under way join it, to be taken up at its next
/// turn, and those it had not taken up when it ended are run after it.
/// <para>Called on the UI thread, where the pages, their handlers and the surfaces that show a reply live; its events are raised there.</para>
/// </summary>
/// <param name="assistant">The window's workspace's assistant, read as each turn runs: the window may move to another workspace.</param>
/// <param name="bar">The window's AI bar.</param>
public sealed class AssistantRequests(Func<IAnswersRequests> assistant, IAssistantBar bar)
{
    private enum TurnOutcome { Completed, Cancelled, Prefill, Error }

    /// <summary>The surface the run under way answers on, or null when none is: while set, what is sent joins that run.</summary>
    private IAIResponseHandler? _running;

    /// <summary>The latest request's cancellation: <see cref="Cancel"/> trips it, and a newer request replaces it.</summary>
    private CancellationTokenSource? _latest;

    /// <summary>A run is under way.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Raised when <see cref="IsBusy"/> changes.</summary>
    public event EventHandler? BusyChanged;

    /// <summary>Something that answers a request failed: what went wrong, for the window to say. The run it was part of ends.</summary>
    public event Action<Exception>? Failed;

    /// <summary>The user cancelled the request they sent, and sent nothing since: the words they sent, for the bar to offer again.</summary>
    public event Action<string>? Cancelled;

    /// <summary>
    /// Sends what the user typed about <paramref name="page"/>, the page in front of them. With a run under way it joins that run and
    /// returns at once; otherwise it ends when the run it starts does.
    /// </summary>
    public async Task SendAsync(string text, IPageViewModel? page)
    {
        if (_running is not null) { _running.Enqueue(text); return; }

        // A new request cancels the one before it; an approval that one waits on is denied through its token.
        _latest?.Cancel();
        _latest = new CancellationTokenSource();
        var token = _latest.Token;

        var surface = SurfaceFor(page);
        await RunTurnsAsync(surface,
                            () => RunTypedTurnAsync(text, page, surface, token),
                            leftover => RunTypedTurnAsync(leftover, page, surface, token),
                            onCancelled: () =>
                            {
                                // Only the user's own cancel offers the words again: a newer request has its own words in the bar.
                                if (token.IsCancellationRequested && _latest?.Token == token) Cancelled?.Invoke(text);
                            });
    }

    /// <summary>
    /// Picks up work the assistant set going in the background that has ended, while the user is still on <paramref name="context"/>:
    /// a turn of the assistant's own, answered where any turn about that page is, the outcome its request. With a run under way, the
    /// outcome joins it, as words sent meanwhile do.
    /// </summary>
    public async Task PickUpAsync(IPageViewModel context, string about, string outcome)
    {
        if (_running is { } running) { running.Enqueue(PickUp(outcome)); return; }

        var surface = SurfaceFor(context);
        _latest = new CancellationTokenSource();
        var token = _latest.Token;

        async Task<TurnOutcome> PickUpTurn()
        {
            surface.BeginFollowUp(about, outcome);
            await WaitForContextAsync(surface, context, token);
            if (token.IsCancellationRequested) { surface.Abort(); return TurnOutcome.Cancelled; }
            return await RunAgentTurnAsync(context, PickUp(outcome), surface, includeContext: true, token);
        }

        await RunTurnsAsync(surface, PickUpTurn, leftover => RunTypedTurnAsync(leftover, context, surface, token), onCancelled: null);
    }

    /// <summary>Cancels the latest request: the agent's run stops at its next step, and an approval it waits on is denied.</summary>
    public void Cancel() => _latest?.Cancel();

    /// <summary>What the assistant is told when work it set going has ended and it picks it up.</summary>
    private static string PickUp(string outcome) =>
        $"Work you set going in the background has ended: {outcome}\n\n" +
        "Tell the user briefly what came of it, or carry on with it where it waits on something you can do.";

    /// <summary>Where a turn about <paramref name="page"/> is answered: on a page that shows the assistant itself (the conversation), there;
    /// any other in the bar. Either way the agent loop talks only to that surface.</summary>
    private IAIResponseHandler SurfaceFor(IPageViewModel? page) => page as IAIResponseHandler ?? bar.Surface;

    /// <summary>
    /// Runs a run's turns on <paramref name="surface"/>: the first, then each follow-up sent after the run's last turn boundary, until
    /// one ends other than completed. While it runs, words sent meanwhile join it.
    /// </summary>
    private async Task RunTurnsAsync(IAIResponseHandler surface, Func<Task<TurnOutcome>> first, Func<string, Task<TurnOutcome>> next,
                                     Action? onCancelled)
    {
        _running = surface;
        SetBusy(true);
        try
        {
            var outcome = await first();
            while (true)
            {
                if (outcome == TurnOutcome.Cancelled) { onCancelled?.Invoke(); break; }
                if (outcome != TurnOutcome.Completed) break;   // Prefill / Error

                // Words arrived after the run's last turn boundary: run them as a fresh follow-up.
                var leftover = surface.DrainQueue();
                if (leftover.Count == 0) break;
                outcome = await next(string.Join("\n", leftover));
            }
        }
        finally
        {
            _running = null;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        if (IsBusy == busy) return;
        IsBusy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Runs one typed request to its answer: a query handler that owns it answers it; otherwise the model picks a handler, and failing
    /// that the agent answers.
    /// </summary>
    private async Task<TurnOutcome> RunTypedTurnAsync(string input, IPageViewModel? page, IAIResponseHandler surface, CancellationToken token)
    {
        var svc = assistant();

        // Scored before anything is shown. A query handler that owns the words never reaches the AI, so showing the AI's surface for
        // it is wrong even briefly — and not briefly at all when the handler is slow: the surface would sit over the results a folder
        // scan is busy producing. Scoring is synchronous, so there is nothing to show during it.
        var (scored, clearWinner, effective, prefixed) = svc.ScoreHandlers(input, page);

        if (clearWinner is not null)
        {
            string? won;
            try   { won = await clearWinner.ProcessAsync(effective, prefixed, page); }
            catch (Exception ex) { Failed?.Invoke(ex); return TurnOutcome.Error; }

            // Only a handler with something to say needs the surface. One that shows its own result — a search filling its list —
            // stays silent, and the surface never appears.
            if (won is not null)
            {
                surface.BeginResponse(input);
                surface.ShowFinal(won);
            }
            return TurnOutcome.Completed;
        }

        surface.BeginResponse(input);   // the user's words, and a "considering" placeholder

        // The page's context may still be gathering (SystemInfo's background scan, say): the turn waits until the page says it is
        // ready, so the model sees what it holds rather than a "still gathering" stub.
        await WaitForContextAsync(surface, page, token);
        if (token.IsCancellationRequested) { surface.Abort(); return TurnOutcome.Cancelled; }

        // No clear winner: asking the model which handler to use is itself an AI call, so the surface is up by here and stays up.
        IQueryHandler? selected = null;
        if (scored.Count > 0)
        {
            try   { selected = await svc.DisambiguateToolSelection(page, effective, scored); }
            catch (Exception ex) { surface.Abort(); Failed?.Invoke(ex); return TurnOutcome.Error; }

            // Cancelled while the model weighed it: what it chose — a command, perhaps — must not then run.
            if (token.IsCancellationRequested) { surface.Abort(); return TurnOutcome.Cancelled; }
        }

        if (selected is not null)
        {
            string? result;
            try   { result = await selected.ProcessAsync(effective, prefixed, page); }
            catch (Exception ex) { surface.Abort(); Failed?.Invoke(ex); return TurnOutcome.Error; }

            if (result is not null) surface.ShowFinal(result);
            else                    surface.Abort();
            return TurnOutcome.Completed;
        }

        return await RunAgentTurnAsync(page, effective, surface, bar.IncludeContext, token);
    }

    /// <summary>One request of the agent's: the agent loop, driving <paramref name="surface"/> (progress, approval, the reply).</summary>
    private async Task<TurnOutcome> RunAgentTurnAsync(IPageViewModel? page, string input, IAIResponseHandler surface, bool includeContext,
                                                      CancellationToken token)
    {
        var svc = assistant();
        AiResponse? response;
        try
        {
            // Answered in the bar, the request continues the bar's conversation, its reply leading with the answer, briefly; answered on
            // a page that holds a conversation, it is a turn of that conversation.
            var options = ReferenceEquals(surface, bar.Surface) ? bar.RunOptions() : AgentRunOptions.Typed with { Conversation = surface.ConversationName };
            response = await svc.RunAgentAsync(page, input, includeContext, surface, options, token);
        }
        catch (OperationCanceledException) { surface.Abort(); return TurnOutcome.Cancelled; }
        catch (Exception ex)               { surface.Abort(); Failed?.Invoke(ex); return TurnOutcome.Error; }

        if (response is null)
        {
            // Nothing to answer: said in the reply's place, rather than the request vanishing — why the workspace's own models cannot
            // run where that is why, else that nothing is assigned.
            if (!token.IsCancellationRequested && !svc.StatusOf(AiAbility.GeneralIntelligence).IsUsable)
            {
                surface.ShowFinal(svc.OwnModelsUnreachable is { } why
                    ? Str.Format("Shell.AiBar.LocalAiUnreachable", why)
                    : Str.Format("Shell.Ai.AbilityDisabledFor", AiAbilityNames.Friendly(AiAbility.GeneralIntelligence)));
                return TurnOutcome.Completed;
            }
            surface.Abort();
            return token.IsCancellationRequested ? TurnOutcome.Cancelled : TurnOutcome.Completed;
        }

        bar.Touched(response.Attachments);

        if (response.Kind == AiResponseKind.Prefill)
        {
            surface.Abort();
            await bar.PrefillAsync(response.Text!);
            return TurnOutcome.Prefill;
        }
        return TurnOutcome.Completed;   // a message: the loop has already shown it through ShowFinal
    }

    /// <summary>Waits until <paramref name="page"/>'s context is ready, or the request is cancelled, saying meanwhile that it waits for the
    /// page's data. A page that is ready, or cannot say when it becomes so, never holds the request up.</summary>
    private static async Task WaitForContextAsync(IAIResponseHandler surface, IPageViewModel? page, CancellationToken token)
    {
        if (page is null || page.IsContextReady) return;
        if (page is not INotifyPropertyChanged observable) return;

        surface.ReportProgress(Str.Get("Shell.AiResponse.WaitingForData"));

        var ready = new TaskCompletionSource();
        void OnChanged(object? _, PropertyChangedEventArgs __)
        {
            if (page.IsContextReady) ready.TrySetResult();
        }

        observable.PropertyChanged += OnChanged;
        using var registration = token.Register(() => ready.TrySetResult());
        try
        {
            if (!page.IsContextReady)   // read again once listening: it may have become ready in between
                await ready.Task;
        }
        finally { observable.PropertyChanged -= OnChanged; }
    }
}
