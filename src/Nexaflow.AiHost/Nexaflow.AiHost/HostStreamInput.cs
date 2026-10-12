using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost;

/// <summary>
/// The input of a streaming request, read so that the request keeps proving it is alive: <c>alive</c> is called
/// whenever a second has passed since it last was — while waiting for input, and between items however long they take
/// to handle — from the handler's own loop, so a handler stuck in native code stops proving it.
/// </summary>
internal static class HostStreamInput
{
    internal static readonly TimeSpan AliveInterval = TimeSpan.FromSeconds(1);

    public static async IAsyncEnumerable<RequestInput> ReadAsync(ChannelReader<RequestInput> input, Func<Task> alive,
                                                                 [EnumeratorCancellation] CancellationToken ct)
    {
        var last = Stopwatch.GetTimestamp();
        while (true)
        {
            if (Stopwatch.GetElapsedTime(last) >= AliveInterval)
            {
                await alive();
                last = Stopwatch.GetTimestamp();
            }
            if (input.TryRead(out var item))
            {
                yield return item;
                continue;
            }

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(AliveInterval);
            bool more;
            try
            {
                more = await input.WaitToReadAsync(wait.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                continue;
            }
            if (!more) yield break;
        }
    }
}
