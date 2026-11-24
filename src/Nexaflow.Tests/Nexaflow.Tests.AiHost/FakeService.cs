using System.Collections.Concurrent;
using System.IO.Pipes;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The machine's AI host service as a client finds it, run in the test process: its pipe, answered by this process — which the service
/// manager is said to run it in — for any number of clients, each speaking the protocol. It can be stopped, said to run in another
/// process, speak another protocol, turn clients away, or miss their heartbeats.
/// </summary>
internal sealed class FakeService : IAiHostService, IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accepting;
    private int _joins;
    private int _left;

    public FakeService()
    {
        PipeName   = $"nexaflow-test-service-{Guid.NewGuid():N}";
        _accepting = Task.Run(AcceptAsync);
    }

    public string PipeName { get; }

    /// <summary>Whether the service manager says the service runs.</summary>
    public bool Running { get; set; } = true;

    /// <summary>The process the service manager says runs the service, in place of the one answering on its pipe — as where something
    /// else took the pipe's name.</summary>
    public int? ReportsProcessId { get; set; }

    /// <summary>The protocol it speaks, where not the client's.</summary>
    public int? SpeaksProtocol { get; set; }

    /// <summary>Turns every client away, saying this.</summary>
    public string? RefusesWith { get; set; }

    public bool AnswerPings { get; set; } = true;

    /// <summary>The share of memory its welcome names; none unless a test sets one.</summary>
    public HostMemory? Memory { get; set; }

    /// <summary>What it does with a request; by default it describes itself and completes.</summary>
    public Func<HostRequest, HostChannel, Task> Serve { get; set; } = FakeHost.Describe;

    /// <summary>The clients it welcomed, counted once per connection.</summary>
    public int Joins => _joins;

    /// <summary>The clients it welcomed that have let go since.</summary>
    public int Left => Volatile.Read(ref _left);

    /// <summary>Every hello it was sent, welcomed or not.</summary>
    public ConcurrentQueue<Hello> Greetings { get; } = [];

    public ConcurrentQueue<long> CancelsReceived { get; } = [];

    public int? RunningProcessId() => Running ? ReportsProcessId ?? System.Environment.ProcessId : null;

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                                                 PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try { await pipe.WaitForConnectionAsync(_stop.Token); }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                return;
            }
            _ = Task.Run(() => ServeAsync(pipe));
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        var channel = new HostChannel(pipe);
        bool welcomed = false;
        await using (channel)
        {
            try
            {
                if (await channel.ReceiveAsync(_stop.Token) is not Hello hello) return;
                Greetings.Enqueue(hello);
                int protocol = SpeaksProtocol ?? AiHostProtocol.Version;
                if (hello.ProtocolVersion != protocol)
                {
                    await channel.SendAsync(new HelloRefused("another protocol", protocol));
                    return;
                }
                if (RefusesWith is { } why)
                {
                    await channel.SendAsync(new HelloRefused(why));
                    return;
                }
                Interlocked.Increment(ref _joins);
                welcomed = true;
                await channel.SendAsync(new Welcome(System.Environment.ProcessId, Memory));

                while (await channel.ReceiveAsync(_stop.Token) is { } message)
                {
                    switch (message)
                    {
                        case Ping ping when AnswerPings:
                            await channel.SendAsync(new Pong(ping.Sequence));
                            break;
                        case CancelRequest cancel:
                            CancelsReceived.Enqueue(cancel.RequestId);
                            await channel.SendAsync(new Failed(cancel.RequestId, HostErrorKind.Cancelled, "cancelled"));
                            break;
                        case HostRequest request:
                            _ = Task.Run(() => Serve(request, channel));
                            break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { }   // the client let go
            finally
            {
                if (welcomed) Interlocked.Increment(ref _left);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await _accepting;
        _stop.Dispose();
    }
}
