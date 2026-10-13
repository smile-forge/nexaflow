using System.Text.Json.Nodes;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// A model's tool calls in its own syntax, read back into calls — Gemma 4's <c>call:name{key:value}</c>, its keys bare
/// and its strings between delimiters the reader has already made quotes — and the conversation as its chat template
/// takes the calls and what came of them.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class ToolCallSyntaxTests
{
    [TestMethod]
    [DataRow("call:delete_file{name:\"test.zip\"}", "delete_file", "{\"name\":\"test.zip\"}")]
    [DataRow("call:send{to:\"ada@example.test\",count:2,urgent:true,tags:[\"a\",\"b\"]}", "send",
             "{\"to\":\"ada@example.test\",\"count\":2,\"urgent\":true,\"tags\":[\"a\",\"b\"]}")]
    [DataRow("call:open{where:{kind:\"folder\",path:\"C:\\\\Temp\"}}", "open", "{\"where\":{\"kind\":\"folder\",\"path\":\"C:\\\\Temp\"}}")]
    [DataRow("call:set_mode{mode:fast}", "set_mode", "{\"mode\":\"fast\"}")]
    [DataRow("call:list_files{}", "list_files", "{}")]
    public void ACallInTheModelsSyntax_IsReadAsTheToolAndItsArguments(string written, string tool, string arguments)
    {
        var call = ToolCallSyntax.Parse(written, "call_0");

        Assert.AreEqual(("call_0", tool), (call.Id, call.Name));
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(arguments), JsonNode.Parse(call.Arguments)), call.Arguments);
    }

    [TestMethod]
    public void ArgumentsThatWillNotRead_AreNone_SoTheToolSaysWhatItIsMissing()
    {
        var call = ToolCallSyntax.Parse("call:delete_file{name:\"test.zip\"", "call_1");

        Assert.AreEqual(("delete_file", "{}"), (call.Name, call.Arguments));
    }

    [TestMethod]
    public void TheTemplate_IsGivenTheCallsAndWhatAnswersThem_TheToolsDeclaredAsItWouldDeclareThem()
    {
        var call = new ChatToolCall("call_0", "delete_file", "{\"name\":\"test.zip\",\"to_bin\":true}");
        ChatTurn[] turns =
        [
            new(TurnRole.System, "Be brief."),
            new(TurnRole.User, "Delete it."),
            new(TurnRole.Assistant, "") { ToolCalls = [call] },
            new(TurnRole.Tool, "Deleted 'test.zip'.") { Answers = call },
        ];

        var messages = JsonNode.Parse(ChatPrompt.Messages(turns, MediaPlan.None, [DeleteFile]))!.AsArray();

        Assert.AreEqual("Be brief." + Declared, (string?)messages[0]!["content"], "the declarations end the system turn");
        var made = messages[2]!["tool_calls"]![0]!;
        Assert.AreEqual(("call_0", "delete_file", "name:<|\"|>test.zip<|\"|>,to_bin:true"),
                        ((string?)made["id"], (string?)made["function"]!["name"], (string?)made["function"]!["arguments"]),
                        "a call made is remembered as the model writes it, keys in order");
        Assert.AreEqual(("tool", "call_0", "delete_file"),
                        ((string?)messages[3]!["role"], (string?)messages[3]!["tool_call_id"], (string?)messages[3]!["name"]));
    }

    private static readonly ChatTool DeleteFile = new("delete_file", "Delete a file.",
        "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"description\":\"The file.\"},"
      + "\"to_bin\":{\"type\":\"boolean\",\"description\":\"To the Recycle Bin.\"}},\"required\":[\"name\"]}");

    /// <summary>What Gemma 4's own template writes for <see cref="DeleteFile"/>.</summary>
    private const string Declared =
        "<|tool>declaration:delete_file{description:<|\"|>Delete a file.<|\"|>,parameters:{properties:{"
      + "name:{description:<|\"|>The file.<|\"|>,type:<|\"|>STRING<|\"|>},"
      + "to_bin:{description:<|\"|>To the Recycle Bin.<|\"|>,type:<|\"|>BOOLEAN<|\"|>}},"
      + "required:[<|\"|>name<|\"|>],type:<|\"|>OBJECT<|\"|>}}<tool|>";

    [TestMethod]
    [TestCategory("OwnedModel")]
    public void GemmasOwnTemplate_RendersTheTools_ACall_AndWhatCameOfIt()
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_GEMMA") is not { Length: > 0 } folder)
        {
            Assert.Inconclusive("NEXAFLOW_TEST_GEMMA names no Gemma 4 E4B folder on this machine.");
            return;
        }
        using var model = Nexaflow.AiHost.Models.ChatModel.Load(
            new ModelSpec("gemma-4-e4b-it", "genai-chat", folder, null, "gemma4-onnx-community-q4f16", 8L << 30, 16_384), "cpu");
        var call = new ChatToolCall("call_0", "delete_file", "{\"name\":\"test.zip\"}");
        ChatTurn[] turns =
        [
            new(TurnRole.System, "Be brief."),
            new(TurnRole.User, "Delete it."),
            new(TurnRole.Assistant, "") { ToolCalls = [call] },
            new(TurnRole.Tool, "Deleted.") { Answers = call },
        ];

        string prompt = ChatPrompt.Render(model, turns, generationPrompt: true, MediaPlan.None, [DeleteFile]);

        Assert.IsNotNull(model.ToolCalls, "its vocabulary holds its call syntax");
        StringAssert.Contains(prompt, "<|turn>system\nBe brief." + Declared + "<turn|>");
        StringAssert.Contains(prompt, "<|tool_call>call:delete_file{name:<|\"|>test.zip<|\"|>}<tool_call|>"
                                    + "<|tool_response>response:delete_file{value:<|\"|>Deleted.<|\"|>}<tool_response|>");
    }

    [TestMethod]
    public void AReplyThatEndedInCalls_IsExtendedStraightOn_ByWhatCameOfThem()
    {
        var call = new ChatToolCall("call_0", "delete_file", "{\"name\":\"test.zip\"}");
        ChatTurn[] read = [new(TurnRole.User, "Delete it."), new(TurnRole.Assistant, "") { ToolCalls = [call] }];
        ChatTurn[] turns = [.. read, new(TurnRole.Tool, "Deleted.") { Answers = call }];

        // As Gemma's template renders them: the call, then the model waiting — a token it generated itself.
        static string Render(IReadOnlyList<ChatTurn> t, bool prompt)
            => "<|turn>user\nDelete it.<turn|>\n<|turn>model\n<|tool_call>call:delete_file{name:<|\"|>test.zip<|\"|>}<tool_call|><|tool_response>"
             + (t.Count > 2 ? "response:delete_file{value:<|\"|>Deleted.<|\"|>}<tool_response|>" : "");

        Assert.IsTrue(ConversationMatch.Continues(read, turns));
        Assert.AreEqual("response:delete_file{value:<|\"|>Deleted.<|\"|>}<tool_response|>", ConversationMatch.Delta(read, turns, Render));
    }
}
