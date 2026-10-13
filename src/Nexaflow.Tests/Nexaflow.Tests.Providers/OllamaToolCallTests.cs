using System.Text.Json.Nodes;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Ollama;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// Native tool calling against a real Ollama server on this machine: a model whose capabilities name tools is offered
/// them in Ollama's protocol, calls one, and hears what came of it. Runs only when <c>NEXAFLOW_TEST_OLLAMA_MODEL</c>
/// names a tool-capable model the server has pulled; <c>NEXAFLOW_TEST_OLLAMA_PLAIN_MODEL</c>, one without tools.
/// </summary>
[TestClass]
public class OllamaToolCallTests
{
    private static readonly LlmTool DeleteFile = new("delete_file", "Delete a file or folder in the folder the user is looking at.", new JsonObject
    {
        ["type"]       = "object",
        ["properties"] = new JsonObject { ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Its name." } },
        ["required"]   = new JsonArray("name"),
    });

    private static async Task<OllamaLlmProvider> WarmedAsync(string variable)
    {
        var model = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(model)) Assert.Inconclusive($"{variable} names no Ollama model on this machine.");
        var provider = new OllamaLlmProvider(new FakeActivityManager(), new OllamaConfig(), new ProviderModel(model));
        await provider.WarmupAsync();
        return provider;
    }

    private static async Task<(string Text, List<LlmToolCall> Calls)> ReadAsync(IAsyncEnumerable<LlmStreamEvent> stream)
    {
        var text  = new System.Text.StringBuilder();
        var calls = new List<LlmToolCall>();
        await foreach (var e in stream)
            if (e is LlmTextDelta d) text.Append(d.Text);
            else if (e is LlmToolCallRequested c) calls.Add(c.Call);
        return (text.ToString(), calls);
    }

    [TestMethod]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task AModelThatCallsTools_CallsOne_AndAnswersWhatCameOfIt()
    {
        var provider = await WarmedAsync("NEXAFLOW_TEST_OLLAMA_MODEL");
        Assert.IsTrue(provider.SupportsToolCalls, "its capabilities name tools");

        List<LlmMessage> messages =
        [
            new(LlmRole.System, "You are an assistant in a file browser. Act with the tools you are given."),
            new(LlmRole.User, "Delete the folder called old."),
        ];
        var (said, calls) = await ReadAsync(provider.StreamAsync(messages, [DeleteFile], LlmUrgency.Interactive));

        var call = calls.Single();
        Assert.AreEqual(("delete_file", "old"), (call.Name, (string?)call.Arguments["name"]), said);

        messages.Add(new(LlmRole.Assistant, said) { ToolCalls = [call] });
        messages.Add(new(LlmRole.Tool, "Deleted the folder old.") { Answers = call });
        var (reply, again) = await ReadAsync(provider.StreamAsync(messages, [DeleteFile], LlmUrgency.Interactive));

        Assert.AreEqual(0, again.Count, $"the deletion is done: {reply}");
        Assert.IsFalse(string.IsNullOrWhiteSpace(reply), "it says what it did");
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AModelWithoutTools_IsNotOfferedThemNatively()
    {
        var provider = await WarmedAsync("NEXAFLOW_TEST_OLLAMA_PLAIN_MODEL");

        Assert.IsFalse(provider.SupportsToolCalls);
    }
}
