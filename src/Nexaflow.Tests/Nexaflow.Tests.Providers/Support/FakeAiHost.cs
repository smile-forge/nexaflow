using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.Tests.Providers.Support;

/// <summary>
/// The AI host as a provider sees it, in memory. Each request is recorded and answered by <see cref="Reply"/>; a
/// stream then answers each input sent into it with <see cref="Answer"/>, and ends when the app sends
/// <see cref="InputEnd"/>.
/// </summary>
internal sealed class FakeAiHost : IAiHost
{
    public readonly List<HostRequest> Requests = [];
    public readonly List<RequestInput> Inputs = [];

    /// <summary>The replies to a request, given how many of its kind came before it.</summary>
    public Func<HostRequest, int, IEnumerable<HostReply>> Reply = (r, _) => r is Generate g
        ? [new TextDelta(g.RequestId, "Hello"), new TextDelta(g.RequestId, " there."), new GenerationEnded(g.RequestId, StopReason.EndOfTurn, 10, 10, 3)]
        : [];

    /// <summary>What a stream replies to each input sent into it.</summary>
    public Func<RequestInput, IEnumerable<HostReply>> Answer = _ => [];

    public AiHostEnvironment Environment { get; set; } = AiHostEnvironment.Cpu;

    public async IAsyncEnumerable<HostReply> SendAsync(Func<long, HostRequest> create, TimeSpan stallLimit,
                                                       [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = Record(create);
        foreach (var reply in Reply(request, Attempt(request)))
        {
            await Task.Yield();
            yield return reply;
        }
    }

    public Task<IHostStream> OpenStreamAsync(Func<long, HostRequest> create, TimeSpan stallLimit, CancellationToken ct = default)
    {
        var request = Record(create);
        var stream  = new Stream(this, request.RequestId);
        foreach (var reply in Reply(request, Attempt(request))) stream.Push(reply);
        return Task.FromResult<IHostStream>(stream);
    }

    private HostRequest Record(Func<long, HostRequest> create)
    {
        var request = create(Requests.Count + 1);
        Requests.Add(request);
        return request;
    }

    private int Attempt(HostRequest request) => Requests.Count(r => r.GetType() == request.GetType()) - 1;

    private sealed class Stream(FakeAiHost host, long id) : IHostStream
    {
        private readonly Channel<HostReply> _replies = Channel.CreateUnbounded<HostReply>();

        public long RequestId => id;

        public bool Disposed { get; private set; }

        public void Push(HostReply reply) => _replies.Writer.TryWrite(reply);

        public Task WriteAsync(RequestInput input, CancellationToken ct = default)
        {
            lock (host.Inputs) host.Inputs.Add(input);
            foreach (var reply in host.Answer(input)) Push(reply);
            if (input is InputEnd) _replies.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<HostReply> ReadAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var reply in _replies.Reader.ReadAllAsync(ct))
            {
                if (reply is Failed failed) throw new AiHostException(AiHostFailure.Request, failed.Message, failed.Kind);
                if (reply is Completed) yield break;
                yield return reply;
            }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _replies.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
