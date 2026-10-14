using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Jinja;
using Nexaflow.Tests.AiHost.Jinja;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>What a chat template is given — the conversation as OpenAI-style messages, the tools, reasoning off — and
/// what Gemma 4's and Qwen's own templates make of it.</summary>
[TestClass]
[CoversNode("ai-host")]
public class ChatPromptTests
{
    private const string Marker = "<__media__>";

    private static readonly ChatToolCall Delete = new("call_0", "delete_file", "{\"name\":\"test.zip\",\"to_bin\":true}");

    private static readonly ChatTool DeleteFile = new("delete_file", "Delete a file.",
        "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"description\":\"The file.\"},"
      + "\"to_bin\":{\"type\":\"boolean\",\"description\":\"To the Recycle Bin.\"}},\"required\":[\"name\"]}");

    private static readonly ChatTurn[] WithACall =
    [
        new(TurnRole.System, "Be brief."),
        new(TurnRole.User, "Delete it."),
        new(TurnRole.Assistant, "") { ToolCalls = [Delete] },
        new(TurnRole.Tool, "Deleted 'test.zip'.") { Answers = Delete },
    ];

    /// <summary>A model that sees but does not hear.</summary>
    private static string? SeesOnly(MediaKind kind) => kind == MediaKind.Image ? Marker : null;

    private static Dictionary<string, object?> Variables(IReadOnlyList<ChatTurn> turns, IReadOnlyList<ChatTool>? tools = null)
        => ChatPrompt.Variables(turns, generationPrompt: true, tools, "<bos>", "<eos>", SeesOnly);

    private static IDictionary<string, object?> Mapping(object? value) => (IDictionary<string, object?>)value!;

    private static IList<object?> List(object? value) => (IList<object?>)value!;

    [TestMethod]
    public void TheTemplate_IsGivenTheTurnsAsMessages_TheCallsArgumentsAsAMapping_AndWhatAnswersThem()
    {
        var messages = List(Variables(WithACall, [DeleteFile])["messages"]);

        Assert.AreEqual(("system", "Be brief."), ((string)Mapping(messages[0])["role"]!, (string)Mapping(messages[0])["content"]!));
        var call = Mapping(List(Mapping(messages[2])["tool_calls"])[0]);
        var function = Mapping(call["function"]);
        Assert.AreEqual(("call_0", "function", "delete_file"), ((string)call["id"]!, (string)call["type"]!, (string)function["name"]!));
        Assert.AreEqual(("test.zip", true), ((string)Mapping(function["arguments"])["name"]!, (bool)Mapping(function["arguments"])["to_bin"]!),
                        "a template writes a call's arguments from a mapping");
        var answer = Mapping(messages[3]);
        Assert.AreEqual(("tool", "call_0", "delete_file"), ((string)answer["role"]!, (string)answer["tool_call_id"]!, (string)answer["name"]!));
    }

    [TestMethod]
    public void TheToolsAreGivenAsFunctions_WithTheirSchemas_ReasoningOff_AndTheModelsOwnTokens()
    {
        var variables = Variables(WithACall, [DeleteFile]);

        var tool = Mapping(List(variables["tools"])[0]);
        var function = Mapping(tool["function"]);
        Assert.AreEqual(("function", "delete_file", "Delete a file."), ((string)tool["type"]!, (string)function["name"]!, (string)function["description"]!));
        Assert.AreEqual("object", (string)Mapping(function["parameters"])["type"]!);
        Assert.AreEqual((false, true, "<bos>", "<eos>"),
                        ((bool)variables["enable_thinking"]!, (bool)variables["add_generation_prompt"]!, (string)variables["bos_token"]!, (string)variables["eos_token"]!));
        Assert.IsFalse(Variables([new(TurnRole.User, "Hi")]).ContainsKey("tools"), "no tools offered, none given");
    }

    [TestMethod]
    public void MediaTheModelTakesIn_IsAMarkerBeforeTheText_AndMediaItCannot_ANote()
    {
        var image = new ChatMedia(MediaKind.Image, @"C:\shot.png", null);
        var speech = new ChatMedia(MediaKind.Audio, null, [1, 2, 3]);

        var parts = List(Mapping(List(Variables([new(TurnRole.User, "What is this?", [image])])["messages"])[0])["content"]);
        var heard = Mapping(List(Variables([new(TurnRole.User, "And this?", [speech])])["messages"])[0])["content"];

        Assert.AreEqual(2, parts.Count);
        Assert.AreEqual((Marker, "What is this?"), ((string)Mapping(parts[0])["text"]!, (string)Mapping(parts[1])["text"]!));
        Assert.AreEqual("[recording]\nAnd this?", heard, "a model that cannot hear is told a recording was there");
    }

    [TestMethod]
    public void GemmasOwnTemplate_DeclaresTheTools_AndWritesTheCallAndWhatCameOfIt()
    {
        string prompt = JinjaTemplate.Parse(ChatTemplateReferences.Template("gemma-4")).Render(Variables(WithACall, [DeleteFile]));

        StringAssert.Contains(prompt, "<|turn>system\nBe brief.<|tool>declaration:delete_file{description:<|\"|>Delete a file.<|\"|>");
        StringAssert.Contains(prompt, "<|tool_call>call:delete_file{name:<|\"|>test.zip<|\"|>,to_bin:true}<tool_call|>"
                                    + "<|tool_response>response:delete_file{value:<|\"|>Deleted 'test.zip'.<|\"|>}<tool_response|>");
    }

    [TestMethod]
    [DataRow("qwen-3.5")]
    [DataRow("qwen-3.6")]
    [DataRow("qwen-3.8")]
    public void QwensOwnTemplate_WritesTheCallAsItsTags_AndAsksForNoReasoning(string family)
    {
        string prompt = JinjaTemplate.Parse(ChatTemplateReferences.Template(family)).Render(Variables(WithACall, [DeleteFile]));

        // 3.5's template writes a boolean as Python prints it; 3.6 and later as JSON.
        string toBin = family == "qwen-3.5" ? "True" : "true";
        StringAssert.Contains(prompt, "<tool_call>\n<function=delete_file>\n<parameter=name>\ntest.zip\n</parameter>\n"
                                    + $"<parameter=to_bin>\n{toBin}\n</parameter>\n</function>\n</tool_call>");
        StringAssert.Contains(prompt, "<tool_response>\nDeleted 'test.zip'.\n</tool_response>");
        Assert.IsTrue(prompt.EndsWith("<|im_start|>assistant\n<think>\n\n</think>\n\n", StringComparison.Ordinal), "reasoning is off");
    }
}
