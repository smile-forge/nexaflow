using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Service;

namespace Nexaflow.AiHost;

/// <summary>
/// The host's clients: every one that connects (<paramref name="pipes"/>) gets a session of its own (<see cref="HostServer"/>) on the
/// host's one set of models, GPUs and handlers. Who a client is the OS says — its account, read by acting as the pipe's client,
/// never taken from what it sends — and its hello says which app it is; one that speaks another protocol, or — on a host started
/// with a token — gives another token, is turned away (<see cref="HelloRefused"/>). A host that ends with its last client serves
/// until every client it welcomed has gone, and gives up where none comes within <see cref="AiHostProtocol.ConnectTimeout"/>; one
/// that does not — the service's worker — serves until no more clients will come, and lets those it has go with it.
/// </summary>
/// <param name="pipes">Where its clients come from: a pipe of its own, or those the service's supervisor hands it.</param>
/// <param name="token">The token a client must give; null where the account alone admits one.</param>
/// <param name="policy">What each client may ask for (<see cref="HostPolicy.Grants"/>).</param>
/// <param name="endsWithLastClient">Stop once the last client welcomed has gone — a host an app started for itself.</param>
/// <param name="welcomeAs">The process its welcome names — the one a client checks it is speaking to: the service's own, for its
/// worker; this one otherwise.</param>
/// <param name="memory">The share of this machine's memory its models on the CPU may hold, which its welcome names.</param>
[SupportedOSPlatform("windows")]
internal sealed class HostListener(IClientPipes pipes, string? token, IReadOnlyList<IHostRequestHandler> handlers, HostPolicy policy, HostLog log,
                                   bool endsWithLastClient, int? welcomeAs = null, HostMemory? memory = null)
{
    /// <summary>How long a client that has connected has to say hello.</summary>
    private static readonly TimeSpan HelloLimit = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly List<HostServer> _sessions = [];
    private readonly CancellationTokenSource _stop = new();
    private int _welcomed;
    private int _present;

    /// <summary>How many clients are connected now — from the moment one's pipe arrives, so one still saying hello counts.</summary>
    public int Clients => Volatile.Read(ref _present);

    /// <summary>Whether any client has a request running.</summary>
    public bool Busy
    {
        get
        {
            lock (_gate) return _sessions.Exists(s => s.Busy);
        }
    }

    /// <summary>Tells every client connected now — a file of the store arriving or going.</summary>
    public void Tell(HostMessage message)
    {
        List<HostServer> sessions;
        lock (_gate) sessions = [.. _sessions];
        foreach (var session in sessions) session.Tell(message);
    }

    /// <summary>Takes clients until the host ends — with its last client, where it does, or once no more will come — then, where it
    /// ends with its last client, waits for every session to end.</summary>
    public async Task RunAsync()
    {
        var sessions = new List<Task>();
        using var noneCame = new CancellationTokenSource(AiHostProtocol.ConnectTimeout);
        while (!_stop.IsCancellationRequested)
        {
            ClientPipe? client;
            try
            {
                using var waiting = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token,
                    Volatile.Read(ref _welcomed) == 0 && endsWithLastClient ? noneCame.Token : CancellationToken.None);
                client = await pipes.NextAsync(waiting.Token);
            }
            catch (OperationCanceledException)
            {
                if (Volatile.Read(ref _welcomed) == 0) log.Write("no client came; stopping");
                break;
            }
            if (client is null)
            {
                log.Write("no more clients will come; stopping");
                break;
            }
    Interlocked.Increment(ref _present);
            sessions.RemoveAll(t => t.IsCompleted);
            sessions.Add(Task.Run(() => ServeAsync(client)));
        }
        if (endsWithLastClient) await Task.WhenAll(sessions);
    }

    /// <summary>One client, from its hello to its leaving.</summary>
    private async Task ServeAsync(ClientPipe arriving)
    {
        var pipe = arriving.Pipe;
        HostServer? server = null;
        HostSections? sections = null;
        try
        {
            var account = arriving.Account ?? AccountOf(pipe);
            sections = new HostSections(account, TimeProvider.System, message =>
            {
                server?.Tell(message);
                return Task.CompletedTask;
            });
            await using var channel = new HostChannel(pipe, sections);
            Hello said;
            using (var hello = new CancellationTokenSource(HelloLimit))
            {
                if (await channel.ReceiveAsync(hello.Token) is not Hello first)
                {
                    log.Write($"{account.Value} connected and said no hello; it was let go");
                    return;
                }
                said = first;
                if (Refusal(said) is { } why)
                {
                    await channel.SendAsync(new HelloRefused(why, said.ProtocolVersion == AiHostProtocol.Version ? null : AiHostProtocol.Version));
                    log.Write($"{account.Value}/{said.App} was turned away: {why}");
                    return;
                }
                await channel.SendAsync(new Welcome(welcomeAs ?? Environment.ProcessId, memory));
            }

            var client = new ClientIdentity(account.Value, said.App);
            server = new HostServer(channel, log, handlers, sections: sections, client: client with { Highest = policy.HighestFor(client) });
            lock (_gate) _sessions.Add(server);
            Interlocked.Increment(ref _welcomed);
            log.WriteActivity($"{server.Client} connected");
            await server.RunAsync();
            log.WriteActivity($"{server.Client} has gone");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException)
        {
            log.Write($"a client was let go before its session began: {ex.Message}");
        }
        finally
        {
            bool alone;
            lock (_gate)
            {
                if (server is not null) _sessions.Remove(server);
                alone = _sessions.Count == 0 && Volatile.Read(ref _welcomed) > 0;
            }
            sections?.Dispose();
            await pipe.DisposeAsync();
            Interlocked.Decrement(ref _present);
            if (alone && endsWithLastClient) _stop.Cancel();
        }
    }

    /// <summary>Why <paramref name="hello"/> is turned away; null where it is welcome.</summary>
    private string? Refusal(Hello hello)
    {
        if (hello.ProtocolVersion != AiHostProtocol.Version)
            return $"it speaks protocol {hello.ProtocolVersion}, and this host {AiHostProtocol.Version}";
        if (string.IsNullOrWhiteSpace(hello.App)) return "it names no app";
        if (token is not null && !TokensMatch(hello.Token, token)) return "it did not give the token this host was started with";
        return null;
    }

    private static bool TokensMatch(string? offered, string expected)
        => offered is not null && offered.Length == expected.Length
           && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(offered), System.Text.Encoding.ASCII.GetBytes(expected));

    /// <summary>The account that opened <paramref name="pipe"/>, as the OS says: read acting as the pipe's client, which a client lets
    /// the host do by connecting at identification level — enough to read who it is, never to act as it.</summary>
    /// <exception cref="UnauthorizedAccessException">The client did not let its account be read.</exception>
    private static SecurityIdentifier AccountOf(NamedPipeServerStream pipe)
    {
        SecurityIdentifier? account = null;
        try
        {
            pipe.RunAsClient(() => account = WindowsIdentity.GetCurrent(TokenAccessLevels.Query).User);
        }
        catch (IOException ex)
        {
            throw new UnauthorizedAccessException($"The client did not let its account be read: {ex.Message} (0x{ex.HResult:X8})", ex);
        }
        return account ?? throw new UnauthorizedAccessException("The client's account could not be read.");
    }
}
