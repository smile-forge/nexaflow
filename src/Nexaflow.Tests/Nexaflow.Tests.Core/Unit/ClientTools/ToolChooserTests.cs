using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Nexaflow.Core.Services.Agent;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Which tools each step of a run is offered: all of a page's few; of its many, the ones a picking model names for what
/// is left to do — asked again whenever the work moves on, and only then — with the caller's own and the lister always.
/// </summary>
[TestClass]
[CoversNode("chat-interface")]
public class ToolChooserTests
{
    /// <summary>A picking model: answers each request as told, and keeps what it was asked.</summary>
    private sealed class Picker(Func<string, string> answer) : ILlmProvider
    {
        public List<string> Asked { get; } = [];

        public string Name => "picker";
        public DataResidency Residency => DataResidency.StaysOnMachine;

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["m"]);

        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
        {
            var asked = messages[^1].Text;
            Asked.Add(asked);
            return Said(answer(asked));
        }

        private static async IAsyncEnumerable<LlmStreamEvent> Said(string text, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new LlmTextDelta(text);
        }
    }

    private static IClientTool Tool(string name) => new DelegateClientTool(name, $"The {name} tool.", [], ToolSafety.SafeOperation,
                                                                           (_, _) => Task.FromResult(ToolResult.Ok("ran")));

    private static readonly IClientTool Lister = Tool("get_client_commands");
    private static readonly IClientTool Own    = Tool("end_voice_session");

    private static IClientTool[] Page(int count) => [.. Enumerable.Range(1, count).Select(i => Tool($"tool_{i}"))];

    private static string[] Names(IReadOnlyList<IClientTool> tools) => [.. tools.Select(t => t.Name)];

    [TestMethod]
    public async Task AFewTools_AreAllOffered_WithoutAskingAnyone()
    {
        var picker  = new Picker(_ => "1");
        var chooser = new ToolChooser(Page(3), Lister, [Own], picker, "A page.", "hi");

        CollectionAssert.AreEqual(new[] { "tool_1", "tool_2", "tool_3", "get_client_commands", "end_voice_session" }, Names(await chooser.ChooseAsync(default)));
        Assert.AreEqual(0, picker.Asked.Count);
    }

    [TestMethod]
    public async Task ManyTools_ArePickedAgain_ForWhatIsLeft_OnceAStepIsDone()
    {
        var picker  = new Picker(asked => asked.Contains("Done so far") ? "5" : "2,3");
        var chooser = new ToolChooser(Page(6), Lister, [Own], picker, "A page.", "Tidy up.");

        CollectionAssert.AreEqual(new[] { "tool_2", "tool_3", "get_client_commands", "end_voice_session" }, Names(await chooser.ChooseAsync(default)));

        chooser.Did("tool_2", new JsonObject { ["name"] = "old" }, "Deleted old.");
        CollectionAssert.AreEqual(new[] { "tool_5", "get_client_commands", "end_voice_session" }, Names(await chooser.ChooseAsync(default)));
        StringAssert.Contains(picker.Asked[1], "tool_2 {\"name\":\"old\"}: Deleted old.");
    }

    [TestMethod]
    public async Task NothingNew_IsNotAskedAgain_AnAddedInstructionIs()
    {
        var picker  = new Picker(_ => "1");
        var chooser = new ToolChooser(Page(6), Lister, [], picker, "A page.", "Delete old.");

        await chooser.ChooseAsync(default);
        await chooser.ChooseAsync(default);
        Assert.AreEqual(1, picker.Asked.Count);

        chooser.Asked(["and the zip"]);
        await chooser.ChooseAsync(default);
        Assert.AreEqual(2, picker.Asked.Count);
        StringAssert.Contains(picker.Asked[1], "Then: \"and the zip\"");
    }

    [TestMethod]
    public async Task OnceListed_EveryToolIsOffered()
    {
        var chooser = new ToolChooser(Page(6), Lister, [], new Picker(_ => "1"), "A page.", "hi");

        chooser.Listed();

        Assert.AreEqual(7, (await chooser.ChooseAsync(default)).Count);
    }

    [TestMethod]
    public async Task WithNoOneToPick_TheFirstFewAreOffered()
    {
        var chooser = new ToolChooser(Page(6), Lister, [], null, "A page.", "hi");

        CollectionAssert.AreEqual(new[] { "tool_1", "tool_2", "tool_3", "tool_4", "get_client_commands" }, Names(await chooser.ChooseAsync(default)));
    }
}
