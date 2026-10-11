using System.Runtime.CompilerServices;

namespace Nexaflow.Providers.Common;

/// <summary>
/// The shared execution skeleton for a streamed completion — what every SDK-backed provider would otherwise
/// repeat. Owns: the activity lifecycle, clean cancellation (<see cref="OperationCanceledException"/>
/// propagates unwrapped so the agent loop's cancel path sees it), a hard overall timeout so a hung stream
/// can't outlive its caller, bounded retry with backoff on transient failures (429 / 5xx / overloaded) that
/// occur BEFORE the first event arrives — a mid-stream failure is never retried, the reader has already seen
/// part of the reply — and uniform <see cref="LlmProviderException"/> wrapping.
/// </summary>
public static class LlmStreamRunner
{
    /// <summary>Hard ceiling on one completion call, retries included. Healthy generations stream
    /// continuously and never approach this; only a hung connection does.</summary>
    public static readonly TimeSpan OverallTimeout = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2)];

    /// <param name="events">Opens the vendor SDK stream and yields its events. Invoked again on a transient
    /// retry, so it must build a fresh request per call.</param>
    public static async IAsyncEnumerable<LlmStreamEvent> RunAsync(
        IBackgroundActivityManager activityManager,
        string activityLabel,
        string providerName,
        Func<CancellationToken, IAsyncEnumerable<LlmStreamEvent>> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(OverallTimeout);
        var run = new Run(activityManager.StartActivity(activityLabel), providerName, ct, timeoutCts.Token);

        try
        {
            for (var attempt = 0; ; attempt++)
            {
                await using var stream = events(run.Token).GetAsyncEnumerator(run.Token);

                Advance step;
                while ((step = await AdvanceAsync(stream, run, attempt)) == Advance.Next)
                {
                    run.Emitted = true;
                    yield return stream.Current;
                }
                if (step == Advance.End) yield break;
            }
        }
        finally
        {
            // A reader that stops early (it found what it needed, or broke out on its own cancellation) ends
            // the call as surely as the stream ending does.
            if (!run.Settled) run.Complete();
        }
    }

    private enum Advance { Next, End, Retry }

    private sealed class Run(IActivityHandle activity, string providerName, CancellationToken caller, CancellationToken token)
    {
        public string            ProviderName => providerName;
        public CancellationToken Caller       => caller;
        public CancellationToken Token        => token;
        public bool              Emitted;
        public bool              Settled;

        public void Complete() { Settled = true; activity.Complete(); }
        public void Fail(string reason) { Settled = true; activity.Fail(reason); }
    }

    /// <summary>Moves the stream on one event, turning every failure into what the reader should see: its own
    /// cancellation unwrapped, anything else an <see cref="LlmProviderException"/> — or, for a transient
    /// failure before anything was yielded, a wait and <see cref="Advance.Retry"/>.</summary>
    private static async ValueTask<Advance> AdvanceAsync(IAsyncEnumerator<LlmStreamEvent> stream, Run run, int attempt)
    {
        try
        {
            if (await stream.MoveNextAsync()) return Advance.Next;
            run.Complete();
            return Advance.End;
        }
        catch (Exception ex) when (ex is not OperationCanceledException &&
                                   !run.Emitted && attempt < Backoff.Length && IsTransient(ex))
        {
            try { await Task.Delay(Backoff[attempt], run.Token); }
            catch (OperationCanceledException oce) { throw Settle(oce, run); }
            return Advance.Retry;
        }
        catch (Exception ex)
        {
            throw Settle(ex, run);
        }
    }

    private static Exception Settle(Exception ex, Run run)
    {
        if (ex is OperationCanceledException && run.Caller.IsCancellationRequested)
        {
            // Cooperative cancellation is a clean stop, not a provider failure — propagate unwrapped.
            run.Fail("canceled");
            return ex;
        }
        if (ex is OperationCanceledException)
        {
            run.Fail("timed out");
            return new LlmProviderException(
                $"{run.ProviderName} request produced no result within {OverallTimeout.TotalMinutes:0} minutes.");
        }
        run.Fail(ex.Message);
        return new LlmProviderException($"{run.ProviderName} request failed: {ex.Message}", ex);
    }

    /// <summary>Heuristic transient classification across four vendor SDKs' exception shapes —
    /// rate limits and server-side hiccups retry; auth/validation failures never match.</summary>
    private static bool IsTransient(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message;
            if (m.Contains("429") ||
                m.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("overloaded", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("500") || m.Contains("502") || m.Contains("503") || m.Contains("504") ||
                e is System.Net.Http.HttpRequestException { StatusCode: null })
                return true;
        }
        return false;
    }
}
