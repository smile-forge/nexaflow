using Nexaflow.AiHost;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Models;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The conversations a model keeps, each the client's whose request began it (<see cref="ClientIdentity"/>), in the host's own parts in
/// this test's process (<see cref="InProcessHost"/>) — where a machine has the model: Qwen 3.5 9B, <c>qwen-3.5-9b</c> from the test
/// store (<see cref="TestStore"/>), held on the CPU, whose conversations are cut back where a turn ended. Each request is run as a client
/// of its own, as the host's server runs it; another client's turns, however alike, are read in full, while a client's own continue its
/// conversations as ever. What is checked is what was read, never wording.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("conversation-cut-back")]
public class ClientConversationsTests
{
    /// <summary>The model's chat part, as a request names it.</summary>
    private static readonly ModelRef Chat = new("qwen-3.5-9b", "chat");

    private static InProcessHost? _host;

    /// <summary>The model held for the class's requests, as an app holds it while it expects to use it.</summary>
    private static IAsyncDisposable? _hold;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        if (TestStore.Root is null) return;
        _host = InProcessHost.OnTheCpu();
        _hold = await _host.Held.HoldAsync(new HoldModels(0, [Chat], RequestPriority.Interactive), _ => Task.CompletedTask, CancellationToken.None);
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_hold is not null) await _hold.DisposeAsync();
        _host?.Dispose();
    }

    /// <summary>A client of its own for each test, so what one test's requests leave behind is never another's.</summary>
    private static ClientIdentity Client(string app) => new("S-1-5-21-0-0-0-1001", app);

    /// <summary>Asks <paramref name="turns"/> as <paramref name="client"/>, as the host's server runs a request of that client's, in the
    /// conversation it calls <paramref name="conversation"/> — a one-off where null — and adds the reply to them. The client is this
    /// call's alone: its caller's flow serves whoever it served.</summary>
    private static async Task<GenerationEnded> AskAsync(ClientIdentity client, List<ChatTurn> turns, string? conversation = "the conversation")
    {
        if (_host is null) Assert.Inconclusive(TestStore.Missing(gpu: false));
        client.Enter();
        var replies = await InProcessHost.RunAsync(new GenerateHandler(_host!.Models, _host.Held, _host.Scheduler),
                                                   new Generate(1, Chat, [.. turns], 16, RequestPriority.Interactive, Thinks: false, Conversation: conversation));
        turns.Add(new ChatTurn(TurnRole.Assistant, string.Concat(replies.OfType<TextDelta>().Select(delta => delta.Text))));
        return replies.OfType<GenerationEnded>().Single();
    }

    /// <summary>Instructions long enough that reading them again is worth sparing: what a cut back keeps.</summary>
    private static readonly string Instructions = "You are the assistant inside Nexaflow, a file explorer, terminal and editor in one window. "
        + string.Concat(Enumerable.Repeat("Answer in one short sentence, plainly, without lists, headings or code unless they are asked for. "
                                          + "Name files by their full path and commands exactly as they are typed. ", 12));

    [TestMethod]
    public async Task AnotherClientsTurns_DoNotContinueAClientsConversation()
    {
        var alice = Client("continued-alice");
        var bob = Client("continued-bob");
        var turns = new List<ChatTurn> { new(TurnRole.System, "Answer in one short sentence."), new(TurnRole.User, "Name a primary colour.") };
        await AskAsync(alice, turns);
        turns.Add(new ChatTurn(TurnRole.User, "And a secondary one?"));

        var bobs = await AskAsync(bob, [.. turns]);
        Assert.AreEqual(bobs.PromptTokens, bobs.ReadTokens, $"another client's turns that continue her conversation are read in full ({bobs})");

        var hers = await AskAsync(alice, turns);
        Assert.IsTrue(hers.ReadTokens < hers.PromptTokens / 2,
            $"her own continue it: read {hers.ReadTokens} of {hers.PromptTokens} tokens, where only her new turn should be");
    }

    [TestMethod]
    public async Task AnotherClientsPrompt_IsNotCutBackFromAClientsConversation()
    {
        var alice = Client("cut-back-alice");
        var bob = Client("cut-back-bob");
        await AskAsync(alice, [new(TurnRole.System, Instructions), new(TurnRole.User, "Name a primary colour.")]);

        var bobs = await AskAsync(bob, [new(TurnRole.System, Instructions), new(TurnRole.User, "Name a secondary colour.")]);
        Assert.AreEqual(bobs.PromptTokens, bobs.ReadTokens, $"another client's prompt sharing her instructions is read in full ({bobs})");

        var hers = await AskAsync(alice, [new(TurnRole.System, Instructions), new(TurnRole.User, "Name a warm colour.")]);
        Assert.IsTrue(hers.ReadTokens < hers.PromptTokens / 4,
            $"her own is cut back to the instructions' end: read {hers.ReadTokens} of {hers.PromptTokens} tokens, where only its own turn should be");
    }

    [TestMethod]
    public async Task AOneOffTurn_KeepsNothing_AndTakesNothingAConversationKept()
    {
        var client = Client("one-off");
        var turns = new List<ChatTurn> { new(TurnRole.System, "Answer in one short sentence."), new(TurnRole.User, "Name a primary colour.") };
        await AskAsync(client, turns);
        turns.Add(new ChatTurn(TurnRole.User, "And a secondary one?"));

        var oneOff = await AskAsync(client, [.. turns], conversation: null);
        Assert.AreEqual(oneOff.PromptTokens, oneOff.ReadTokens, $"a one-off continues nothing, however alike its turns ({oneOff})");

        var named = await AskAsync(client, turns);
        Assert.IsTrue(named.ReadTokens < named.PromptTokens / 2,
            $"the conversation is still there for its own next turn: read {named.ReadTokens} of {named.PromptTokens} tokens");
    }

    [TestMethod]
    public async Task AForgottenConversation_IsReadAfresh()
    {
        var client = Client("forgotten");
        var turns = new List<ChatTurn> { new(TurnRole.System, "Answer in one short sentence."), new(TurnRole.User, "Name a primary colour.") };
        await AskAsync(client, turns);
        turns.Add(new ChatTurn(TurnRole.User, "And a secondary one?"));

        client.Enter();
        await InProcessHost.RunAsync(new ForgetConversationHandler(_host!.Models), new ForgetConversation(2, "the conversation"));
        var after = await AskAsync(client, turns);

        Assert.AreEqual(after.PromptTokens, after.ReadTokens, $"what was kept of it went with it ({after})");
    }

    [TestMethod]
    public async Task TwoConversationsOfAClient_SharingALongStart_ReadItOnce()
    {
        var client = Client("opening");
        await AskAsync(client, [new(TurnRole.System, Instructions), new(TurnRole.User, "Name a primary colour.")], conversation: "first");
        await AskAsync(client, [new(TurnRole.System, Instructions), new(TurnRole.User, "Name a secondary colour.")], conversation: "second");

        var third = await AskAsync(client, [new(TurnRole.System, Instructions), new(TurnRole.User, "Name a warm colour.")], conversation: "third");

        Assert.IsTrue(third.ReadTokens < third.PromptTokens / 4,
            $"a third conversation opens on the start the others shared, read once: read {third.ReadTokens} of {third.PromptTokens} tokens");
    }
}
