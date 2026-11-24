using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// An app that asks and then stops reading: a bare connection to a service, below the SDK — whose client reads every reply off the
/// pipe as it comes, however slowly its caller takes them — so its replies fill the pipe and then the host's queue for it, as an app
/// that hangs, or reads slowly, leaves them. It can read again (<see cref="ReadUntilEndedAsync"/>).
/// </summary>
internal sealed class UnreadClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly HostChannel _channel;
    private long _next;

    private UnreadClient(NamedPipeClientStream pipe, HostChannel channel)
    {
        _pipe = pipe;
        _channel = channel;
    }

    /// <summary>Joins the service on <paramref name="pipeName"/> as <paramref name="app"/>.</summary>
    public static async Task<UnreadClient> JoinAsync(string pipeName, string app)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeAccessRights.ReadWrite, PipeOptions.Asynchronous,
                                             TokenImpersonationLevel.Identification, HandleInheritability.None);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        var channel = new HostChannel(pipe);
        await channel.SendAsync(new Hello(null, AiHostProtocol.Version, app));
        if (await channel.ReceiveAsync() is not Welcome) throw new AssertFailedException("the service did not welcome the unread client");
        return new UnreadClient(pipe, channel);
    }

    /// <summary>Sends the request <paramref name="create"/> makes, and reads nothing of its replies; its id.</summary>
    public async Task<long> AskAsync(Func<long, HostRequest> create)
    {
        long id = Interlocked.Increment(ref _next);
        await _channel.SendAsync(create(id));
        return id;
    }

    /// <summary>What waits in the pipe, unread: how full the host has filled it.</summary>
    public long Waiting
        => PeekNamedPipe(_pipe.SafePipeHandle, IntPtr.Zero, 0, IntPtr.Zero, out uint waiting, IntPtr.Zero) ? waiting : 0;

    /// <summary>Reads again, until every one of <paramref name="requests"/> has ended: how each ended, by its id, and how many tokens they
    /// generated in all.</summary>
    public async Task<(IReadOnlyDictionary<long, HostReply> Ends, int Tokens)> ReadUntilEndedAsync(IReadOnlyCollection<long> requests, CancellationToken ct)
    {
        var ended = new Dictionary<long, HostReply>();
        int tokens = 0;
        while (ended.Count < requests.Count && await _channel.ReceiveAsync(ct) is { } message)
        {
            switch (message)
            {
                case Ping ping:
                    await _channel.SendAsync(new Pong(ping.Sequence), ct);
                    break;
    case GenerationEnded generated when requests.Contains(generated.RequestId):
                    tokens += generated.GeneratedTokens;
                    break;
                case Completed or Failed when requests.Contains(((HostReply)message).RequestId):
                    ended[((HostReply)message).RequestId] = (HostReply)message;
                    break;
            }
        }
        return (ended, tokens);
    }

    public async ValueTask DisposeAsync() => await _channel.DisposeAsync();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekNamedPipe(SafePipeHandle pipe, IntPtr buffer, uint size, IntPtr read, out uint available, IntPtr leftOfMessage);
}
