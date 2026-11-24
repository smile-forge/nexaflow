using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.ClientTools;

/// <summary>
/// A run that is a turn of a conversation is named by it to every model it reaches, the larger one it is handed over to
/// included, so a model that keeps conversations between turns keeps it; work that is no conversation goes unnamed. A
/// conversation deleted or closed is forgotten by every language model of the workspace.
/// </summary>
[TestClass]
[CoversNode("chat-interface")]
public class ConversationNamingTests
{
    /// <summary>A model that answers every step at once — or fails, as a provider's error does — and keeps the conversation
    /// each step was a turn of, and every conversation it was told to forget.</summary>
    private sealed class Model(string name, DataResidency residency, bool fails = false) : ILlmProvider
    {
        public List<string?> Turns { get; } = [];
        public List<string> Forgotten { get; } = [];

        public string Name => name;
        public DataResidency Residency => residency;
        public bool IsAvailable => true;

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["m"]);

        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
            => throw new AssertFailedException("every step of a run is asked as a turn of its conversation, or of none");

        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmTool> tools, LlmUrgency urgency,
                                                            LlmThinking thinking, string? conversation, CancellationToken ct = default)
        {
            Turns.Add(conversation);
            if (fails) throw new LlmProviderException($"{name} could not answer.");
            return Said("Done.");
        }

        public Task ForgetConversationAsync(string conversation, CancellationToken ct = default)
        {
            Forgotten.Add(conversation);
            return Task.CompletedTask;
        }

        private static async IAsyncEnumerable<LlmStreamEvent> Said(string text)
        {
            await Task.Yield();
            yield return new LlmTextDelta(text);
            yield return new LlmReplyEnded(LlmReplyEnd.Finished, null);
        }
    }

    /// <summary>A handler holding the conversation <paramref name="conversation"/>, or none.</summary>
    private sealed class Holder(string? conversation) : IAIResponseHandler
    {
        public string? ConversationName => conversation;
        public void BeginResponse(string userText) { }
        public void ReportProgress(string message) { }
        public Task<bool> RequestToolBatchApprovalAsync(string explanation, IReadOnlyList<ToolCall> batch, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct) => Task.FromResult(true);
        public void ShowFinal(string finalMarkdown) { }
        public void Abort() { }
    }

    /// <summary>A service whose General Intelligence row is <paramref name="row"/>, in order, each model in a column of its own
    /// — and <paramref name="again"/> in another column besides.</summary>
    private static AIService Service(Model[] row, Model? again = null)
    {
        var svc = TestRuntimes.Unstarted().AiService;

        var columns = row.Select((m, i) => (Id: "c" + i, Model: m)).ToList();
        if (again is not null) columns.Add(("again", again));
        foreach (var (id, model) in columns) svc.Register(id, model);
        var config = new AiConfig { Columns = [.. columns.Select(c => new ProviderModelPair { Id = c.Id, ProviderName = c.Model.Name, Model = "m" })] };
        config.Assignments["GeneralIntelligence"] = AbilityChain.Of([.. row.Select((_, i) => "c" + i)]);
        svc.LoadAbilityConfig(config);
        return svc;
    }

    [TestMethod]
    public async Task ARunOnAPageHoldingAConversation_IsATurnOfIt()
    {
        var model = new Model("local", DataResidency.StaysOnMachine);

        await Service([model]).RunAgentAsync(null, "What did we decide?", false, new Holder("chat-7"));

        CollectionAssert.AreEqual(new[] { "chat-7" }, model.Turns);
    }

    [TestMethod]
    public async Task WorkThatIsNoConversation_GoesUnnamed()
    {
        var model = new Model("local", DataResidency.StaysOnMachine);

        await Service([model]).RunAgentAsync(null, "What time is it?", false, new Holder(null));
        await Service([model]).RunAgentAsync(null, "And now?", false, new Holder("chat-7"), AgentRunOptions.Typed);

        CollectionAssert.AreEqual(new string?[] { null, null }, model.Turns, "a run's options name its conversation, and the typed run's name none");
    }

    [TestMethod]
    public async Task ARunHandedOverToTheLargerModel_StaysATurnOfTheSameConversation()
    {
        var local  = new Model("local", DataResidency.StaysOnMachine, fails: true);
        var larger = new Model("large", DataResidency.LeavesMachine);

        await Service([local, larger]).RunAgentAsync(null, "Summarise the thread.", false, new Holder(null),
                                                      new AgentRunOptions { Conversation = "chat-7" });

        CollectionAssert.AreEqual(new[] { "chat-7" }, local.Turns);
        CollectionAssert.AreEqual(new[] { "chat-7" }, larger.Turns);
    }

    [TestMethod]
    public async Task AConversationForgotten_IsForgottenByEveryLanguageModelOfTheWorkspace_EachOnce()
    {
        var local  = new Model("local", DataResidency.StaysOnMachine);
        var larger = new Model("large", DataResidency.LeavesMachine);

        await Service([local, larger], again: local).ForgetConversationAsync("chat-7");

        CollectionAssert.AreEqual(new[] { "chat-7" }, local.Forgotten, "a model in two columns is one model");
        CollectionAssert.AreEqual(new[] { "chat-7" }, larger.Forgotten);
    }

    [TestMethod]
    public async Task AConversationDeleted_IsForgotten()
    {
        var model = new Model("local", DataResidency.StaysOnMachine);

        await Service([model]).DeleteConversationAsync("chat-7");

        CollectionAssert.AreEqual(new[] { "chat-7" }, model.Forgotten);
    }
}
