using System.Text;

namespace Nexaflow.Providers.Common;

/// <summary>
/// The shared execution skeleton for a streaming completion call — the four SDK-backed providers
/// each repeated it (activity handle, try/catch, accumulate, wrap). Owns: the activity lifecycle,
/// clean cancellation (<see cref="OperationCanceledException"/> propagates unwrapped so the agent
/// loop's cancel path sees it), a hard overall timeout so a hung stream can't outlive its caller,
/// bounded retry with backoff on transient failures (429 / 5xx / overloaded) that occur BEFORE the
/// first delta arrives — a mid-stream failure is never retried, the model may already have acted —
/// delta accumulation, and uniform <see cref="LlmProviderException"/> wrapping.
/// </summary>
public static class LlmStreamRunner
{
    /// <summary>Hard ceiling on one completion call, retries included. Healthy generations stream
    /// deltas continuously and never approach this; only a hung connection does.</summary>
    public static readonly TimeSpan OverallTimeout = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2)];

    /// <param name="deltas">Opens the vendor SDK stream and yields text deltas (nulls/empties skipped).
    /// Invoked again on transient retry, so it must build a fresh request per call.</param>
    public static async Task<LlmResponse?> RunAsync(
        IBackgroundActivityManager activityManager,
        string activityLabel,
        string providerName,
        Func<CancellationToken, IAsyncEnumerable<string?>> deltas,
        CancellationToken ct)
    {
        var activity = activityManager.StartActivity(activityLabel);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(OverallTimeout);
        var runCt = timeoutCts.Token;

        var sb = new StringBuilder();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await foreach (var delta in deltas(runCt))
                    if (!string.IsNullOrEmpty(delta)) sb.Append(delta);

                activity.Complete();
                var text = sb.ToString();
                return string.IsNullOrEmpty(text) ? null : new LlmResponse(text);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cooperative cancellation is a clean stop, not a provider failure — propagate unwrapped.
                activity.Fail("canceled");
                throw;
            }
            catch (OperationCanceledException)
            {
                activity.Fail("timed out");
                throw new LlmProviderException(
                    $"{providerName} request produced no result within {OverallTimeout.TotalMinutes:0} minutes.");
            }
            catch (Exception ex) when (sb.Length == 0 && attempt < Backoff.Length && IsTransient(ex))
            {
                await Task.Delay(Backoff[attempt], runCt);
            }
            catch (Exception ex)
            {
                activity.Fail(ex.Message);
                throw new LlmProviderException($"{providerName} request failed: {ex.Message}", ex);
            }
        }
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
