using System.Runtime.CompilerServices;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Client;

/// <summary>
/// A request the app keeps feeding while it runs: input goes in through <see cref="WriteAsync"/>, replies come out of
/// <see cref="ReadAsync"/> until its end. Disposing it before then cancels it in the host.
/// </summary>
public interface IHostStream : IAsyncDisposable
{
    long RequestId { get; }

    /// <summary>Sends input into the request — made with <see cref="RequestId"/>.</summary>
    /// <exception cref="AiHostException">The host was lost.</exception>
    Task WriteAsync(RequestInput input, CancellationToken ct = default);

    /// <summary>The request's replies until it completes; a failure arrives as an <see cref="AiHostException"/>. One
    /// reader at a time.</summary>
    IAsyncEnumerable<HostReply> ReadAsync(CancellationToken ct = default);
}

internal sealed class HostStream(HostConnection.OpenRequest request, TimeSpan stallLimit) : IHostStream
{
    public long RequestId => request.Id;

    public Task WriteAsync(RequestInput input, CancellationToken ct = default)
    {
        if (input.RequestId != RequestId)
            throw new ArgumentException($"Input for request {input.RequestId} cannot go into request {RequestId}.", nameof(input));
        return request.SendInputAsync(input, ct);
    }

    public async IAsyncEnumerable<HostReply> ReadAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        while (true)
        {
            var reply = await request.NextAsync(stallLimit, ct).ConfigureAwait(false);
            switch (reply)
            {
                case Completed:
                    yield break;
                case Failed failed:
                    throw new AiHostException(AiHostFailure.Request, failed.Message, failed.Kind);
                default:
                    yield return reply;
                    break;
            }
        }
    }

    public ValueTask DisposeAsync() => request.DisposeAsync();
}
