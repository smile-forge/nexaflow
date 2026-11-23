using System.Text;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// A conversation cut back to where a prompt parts from it, in the real host with a model whose layers hold recurrent state —
/// where a machine has one: Qwen 3.5 9B, <c>qwen-3.5-9b</c> from the test store (<see cref="TestStore"/>), its gated DeltaNet
/// layers beside attention, held where the host places it. What is checked is what was read, never wording.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("conversation-cut-back")]
public class CutBackModelTests
{
    /// <summary>The model's chat part, as a request names it.</summary>
    private static readonly ModelRef Chat = new("qwen-3.5-9b", "chat");

    private static AiHostClient? _client;

    /// <summary>The model held for the class's requests, as a conversation holds it.</summary>
    private static HostHold? _hold;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        _client = await TestStore.HostAsync(gpu: false);
        if (_client is null) return;
        // A model holding recurrent state is held with saves of it, which is what a conversation is cut back to.
        _hold = await TestStore.HoldPlacedAsync(_client, [Chat], RequestPriority.Interactive);
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_hold is not null) await _hold.DisposeAsync();
        if (_client is not null) await _client.DisposeAsync();
    }

    private static async Task<GenerationEnded> AskAsync(List<ChatTurn> turns)
    {
        if (_client is null || _hold is null) Assert.Inconclusive(TestStore.Missing(gpu: false));
        var text = new StringBuilder();
        GenerationEnded? ended = null;
        await foreach (var reply in _client!.SendAsync(id => new Generate(id, Chat, turns, 48, RequestPriority.Interactive, Thinks: false),
                                                       TimeSpan.FromMinutes(5)))
        {
            if (reply is TextDelta delta) text.Append(delta.Text);
            if (reply is GenerationEnded end) ended = end;
        }
        turns.Add(new ChatTurn(TurnRole.Assistant, text.ToString()));
        return ended!;
    }

    /// <summary>Instructions long enough that reading them again is worth sparing: what a cut back keeps.</summary>
    private static readonly string Instructions = "You are the assistant inside Nexaflow, a file explorer, terminal and editor in one window. "
        + string.Concat(Enumerable.Repeat("Answer in one short sentence, plainly, without lists, headings or code unless they are asked for. "
                                          + "Name files by their full path and commands exactly as they are typed. ", 12));

    [TestMethod]
    public async Task AnEditedLastTurn_IsReadFromTheEndOfTheTurnBeforeIt_NotFromTheStart()
    {
        var turns = new List<ChatTurn> { new(TurnRole.System, Instructions), new(TurnRole.User, "Name a primary colour.") };
        var first = await AskAsync(turns);
        Assert.AreEqual(first.PromptTokens, first.ReadTokens, $"the first turn reads the whole prompt ({first})");

        var edited = new List<ChatTurn> { new(TurnRole.System, Instructions), new(TurnRole.User, "Name a secondary colour instead.") };
        var second = await AskAsync(edited);

        Assert.IsTrue(second.ReadTokens < second.PromptTokens / 4,
            $"the edit read {second.ReadTokens} of {second.PromptTokens} tokens: cut back to the instructions' end, it should read only its own turn");
    }
}
