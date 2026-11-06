using System.Text;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// A conversation cut back to where a prompt parts from it, in the real host with a model whose layers hold recurrent state —
/// where a machine has one. <c>NEXAFLOW_TEST_HYBRID</c> names the GGUF of a hybrid model (Qwen 3.5, its gated DeltaNet layers
/// beside attention); it runs on the CPU. What is checked is what was read, never wording.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("conversation-cut-back")]
public class CutBackModelTests
{
    private static AiHostClient? _client;
    private static ModelSpec? _model;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_HYBRID") is not { Length: > 0 } gguf || !File.Exists(gguf)) return;

        var aiDir = TestTemp.CreateSubdirectory("nexaflow-aihost-").FullName;
        _client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"), aiDir));
        _model = new ModelSpec("hybrid", "llama-chat", Path.GetDirectoryName(gguf)!, Path.GetFileName(gguf), null, 0, 8192,
                               ContextCache: ContextCache.Q8, Checkpoints: 8);
        await foreach (var _ in _client.SendAsync(id => new LoadModel(id, _model), TimeSpan.FromMinutes(3))) { }
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private static async Task<GenerationEnded> AskAsync(List<ChatTurn> turns)
    {
        if (_client is null || _model is null) Assert.Inconclusive("NEXAFLOW_TEST_HYBRID names no hybrid model's GGUF on this machine.");
        var text = new StringBuilder();
        GenerationEnded? ended = null;
        await foreach (var reply in _client!.SendAsync(id => new Generate(id, _model!, turns, 48, RequestPriority.Interactive, Thinks: false),
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
