using System.IO.Pipes;
using System.Security.Principal;
using Nexaflow.AiHost;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Service;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The host's pipe with several clients on it: each its own session, known by its account and the app its hello names, every one
/// turned away that gives the wrong token or protocol, a client that sends what cannot be read ending only its own session, and a
/// host that ends with its last client ending once every client it welcomed has gone.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class HostListenerTests
{
    private const string Token = "the token";

    /// <summary>Answers a description with who asked — the client the request was served as, and the priority it is granted.</summary>
    private sealed class WhoAsked : HostRequestHandler<DescribeHost>
    {
        protected override Task HandleAsync(DescribeHost request, Func<HostReply, Task> reply, CancellationToken ct)
            => reply(new HostDescription(request.RequestId, ClientIdentity.Current.Key, [ClientIdentity.Current.Granted(RequestPriority.Voice).ToString()], null, []));
    }

    private sealed record Listening(string Pipe, HostListener Listener, Task Running);

    private static Listening Listen(HostPolicy? policy = null)
    {
        string pipe = "nexaflow-test-" + Guid.NewGuid().ToString("N");
        var log = new HostLog(TestTemp.CreateSubdirectory("nexaflow-listener-").FullName);
        var listener = new HostListener(new OwnPipes(pipe), Token, [new WhoAsked()], policy ?? HostPolicy.Default, log, endsWithLastClient: true);
        return new Listening(pipe, listener, Task.Run(listener.RunAsync));
    }

    private static async Task<HostChannel> ConnectAsync(string pipe)
    {
        var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await stream.ConnectAsync(5000);
        return new HostChannel(stream);
    }

    private static async Task<HostMessage?> HelloAsync(HostChannel channel, string app, string? token = Token, int version = AiHostProtocol.Version)
    {
        await channel.SendAsync(new Hello(token, version, app));
        return await channel.ReceiveAsync();
    }

    private static async Task<HostDescription> AskAsync(HostChannel channel, long id)
    {
        await channel.SendAsync(new DescribeHost(id));
        HostDescription? said = null;
        while (await channel.ReceiveAsync() is { } message)
        {
            if (message is HostDescription description) said = description;
            if (message is Completed or Failed) break;
        }
        return said ?? throw new AssertFailedException("the host did not describe itself");
    }

    private static string Me => WindowsIdentity.GetCurrent().User!.Value;

    [TestMethod]
    public async Task TwoClients_EachHaveASessionOfTheirOwn_KnownByTheirAccountAndApp()
    {
        var host = Listen();
        await using var nexaflow = await ConnectAsync(host.Pipe);
        await using var aria = await ConnectAsync(host.Pipe);

        Assert.IsInstanceOfType<Welcome>(await HelloAsync(nexaflow, "Nexaflow"));
        Assert.AreEqual(Environment.ProcessId, ((Welcome)(await HelloAsync(aria, "Aria"))!).ProcessId, "the welcome names the host's process");

        Assert.AreEqual($"{Me}/Nexaflow", (await AskAsync(nexaflow, 1)).RuntimeVersion);
        Assert.AreEqual($"{Me}/Aria", (await AskAsync(aria, 1)).RuntimeVersion, "the same request id on another session is its own");
    }

    [TestMethod]
    public async Task AClientWithTheWrongToken_OrAnotherProtocol_IsTurnedAway_SayingWhy()
    {
        var host = Listen();
        await using var welcomed = await ConnectAsync(host.Pipe);
        await HelloAsync(welcomed, "Nexaflow");
        await using var stranger = await ConnectAsync(host.Pipe);
        await using var older = await ConnectAsync(host.Pipe);

        var token = await HelloAsync(stranger, "Stranger", token: "another token");
        var version = await HelloAsync(older, "Older", version: AiHostProtocol.Version - 1);

        StringAssert.Contains(((HelloRefused)token!).Reason, "token");
        Assert.IsNull(((HelloRefused)token).Protocol, "the protocol was not what it was turned away for");
        StringAssert.Contains(((HelloRefused)version!).Reason, "protocol");
        Assert.AreEqual(AiHostProtocol.Version, ((HelloRefused)version).Protocol, "the protocol the host speaks, for a client to start a host of its own");
        Assert.IsNull(await stranger.ReceiveAsync(), "and its pipe is closed");
        Assert.AreEqual($"{Me}/Nexaflow", (await AskAsync(welcomed, 1)).RuntimeVersion, "the welcomed client is served all the while");
    }

    [TestMethod]
    public async Task AClientThatSendsWhatCannotBeRead_EndsItsOwnSession_AndNoOther()
    {
        var host = Listen();
        await using var steady = await ConnectAsync(host.Pipe);
        await HelloAsync(steady, "Steady");
        var raw = new NamedPipeClientStream(".", host.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await raw.ConnectAsync(5000);
        var broken = new HostChannel(raw);
        await HelloAsync(broken, "Broken");

        await raw.WriteAsync(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F, 1, 2, 3 });   // a frame longer than any the host reads
        await raw.FlushAsync();

        Assert.IsNull(await broken.ReceiveAsync(), "its session ends");
        Assert.AreEqual($"{Me}/Steady", (await AskAsync(steady, 7)).RuntimeVersion, "the host serves the others");
        Assert.IsFalse(host.Running.IsCompleted);
        await broken.DisposeAsync();
    }

    [TestMethod]
    public async Task AHostThatEndsWithItsLastClient_EndsOnceEveryClientItWelcomedHasGone()
    {
        var host = Listen();
        var first = await ConnectAsync(host.Pipe);
        var second = await ConnectAsync(host.Pipe);
        await HelloAsync(first, "First");
        await HelloAsync(second, "Second");

        await first.DisposeAsync();
        await Task.Delay(200);
        Assert.IsFalse(host.Running.IsCompleted, "a client is still there");

        await second.DisposeAsync();
        await host.Running.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task AClient_IsServedAtNoHigherPriorityThanThePolicyGrantsIt()
    {
        var policy = HostPolicy.Default with
        {
            Grants = new Dictionary<string, RequestPriority> { [$"{Me}/Background app"] = RequestPriority.Background, [Me] = RequestPriority.Interactive },
        };
        var host = Listen(policy);
        await using var background = await ConnectAsync(host.Pipe);
        await using var other = await ConnectAsync(host.Pipe);
        await HelloAsync(background, "Background app");
        await HelloAsync(other, "Other app");

        Assert.AreEqual(nameof(RequestPriority.Background), (await AskAsync(background, 1)).ExecutionProviders[0], "its app's grant");
        Assert.AreEqual(nameof(RequestPriority.Interactive), (await AskAsync(other, 1)).ExecutionProviders[0], "its account's grant");
    }
}
