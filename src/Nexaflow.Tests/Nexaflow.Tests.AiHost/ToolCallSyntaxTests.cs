using System.Text.Json.Nodes;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// A model's tool calls in its own syntax, read back into calls as its tokens come: Gemma 4's
/// <c>call:name{key:value}</c> between its own tokens, its strings between delimiter tokens; Qwen's
/// <c>&lt;function=…&gt;</c> with a <c>&lt;parameter=…&gt;</c> per argument, typed by the tool's schema. Which a model
/// writes is told by the special tokens in its vocabulary.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class ToolCallSyntaxTests
{
    private const int GemmaOpen = 1, GemmaClose = 2, GemmaQuote = 3, GemmaWait = 4, QwenOpen = 11, QwenClose = 12;

    private static readonly Dictionary<string, int> GemmaVocabulary = new()
    {
        ["<|tool_call>"] = GemmaOpen, ["<tool_call|>"] = GemmaClose, ["<|\"|>"] = GemmaQuote, ["<|tool_response>"] = GemmaWait,
    };

    private static readonly Dictionary<string, int> QwenVocabulary = new() { ["<tool_call>"] = QwenOpen, ["</tool_call>"] = QwenClose };

    private const string QwenTemplate = "…<tool_call>\\n<function=example_function_name>…";

    private static Func<string, int?> Special(Dictionary<string, int> vocabulary) => text => vocabulary.TryGetValue(text, out int id) ? id : null;

    private static readonly ChatTool OpenFolder = new("open_folder", "Opens a folder.",
        "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"tab\":{\"type\":\"integer\"},\"code\":{\"type\":\"string\"}}}");

    [TestMethod]
    public void TheSyntax_IsTheOneTheVocabularyHolds()
    {
        Assert.IsInstanceOfType<GemmaToolCalls>(ToolCallSyntax.Of(Special(GemmaVocabulary), ""));
        Assert.IsInstanceOfType<QwenToolCalls>(ToolCallSyntax.Of(Special(QwenVocabulary), QwenTemplate));
        Assert.IsNull(ToolCallSyntax.Of(Special(QwenVocabulary), "{{ tool_call | tojson }}"), "the same tokens with JSON calls in them are another syntax");
        Assert.IsNull(ToolCallSyntax.Of(Special([]), QwenTemplate), "a model with no call tokens writes no calls");
    }

    /// <summary>Feeds a reply's tokens to a reader: a number is that token, a string a text token of that text.</summary>
    private static (List<ToolCallToken> Seen, List<ChatToolCall> Made) Feed(ToolCallSyntax syntax, IReadOnlyList<ChatTool> tools, params object[] reply)
    {
        var texts = new Dictionary<int, string>();
        int next = 100;
        var tokens = reply.Select(r => r is int id ? id : Add((string)r)).ToList();
        int Add(string text) { texts[next] = text; return next++; }

        var reader = syntax.Read(token => texts.GetValueOrDefault(token, ""), tools);
        var seen = new List<ToolCallToken>();
        var made = new List<ChatToolCall>();
        foreach (int token in tokens)
        {
            seen.Add(reader.Take(token));
            if (seen[^1] == ToolCallToken.Made) made.Add(reader.Made!);
        }
        return (seen, made);
    }

    [TestMethod]
    public void AGemmaCall_IsReadFromItsTokens_TheTextAroundItLeftAsText_AndItsWaitEndingTheTurn()
    {
        var syntax = ToolCallSyntax.Of(Special(GemmaVocabulary), "")!;

        var (seen, made) = Feed(syntax, [OpenFolder], "Opening.", GemmaOpen, "call:open_folder{path:", GemmaQuote, "D:\\docs", GemmaQuote, ",tab:2}",
                                GemmaClose, GemmaWait);

        CollectionAssert.AreEqual(new[] { ToolCallToken.Text, ToolCallToken.Call, ToolCallToken.Call, ToolCallToken.Call, ToolCallToken.Call,
                                          ToolCallToken.Call, ToolCallToken.Call, ToolCallToken.Made, ToolCallToken.Waits }, seen);
        Assert.AreEqual(("call_0", "open_folder"), (made[0].Id, made[0].Name));
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("{\"path\":\"D:\\\\docs\",\"tab\":2}"), JsonNode.Parse(made[0].Arguments)), made[0].Arguments);
    }

    [TestMethod]
    [DataRow("call:delete_file{name:\"test.zip\"}", "delete_file", "{\"name\":\"test.zip\"}")]
    [DataRow("call:send{to:\"ada@example.test\",count:2,urgent:true,tags:[\"a\",\"b\"]}", "send",
             "{\"to\":\"ada@example.test\",\"count\":2,\"urgent\":true,\"tags\":[\"a\",\"b\"]}")]
    [DataRow("call:open{where:{kind:\"folder\",path:\"C:\\\\Temp\"}}", "open", "{\"where\":{\"kind\":\"folder\",\"path\":\"C:\\\\Temp\"}}")]
    [DataRow("call:set_mode{mode:fast}", "set_mode", "{\"mode\":\"fast\"}")]
    [DataRow("call:list_files{}", "list_files", "{}")]
    public void AGemmaCallAsWritten_IsReadAsTheToolAndItsArguments(string written, string tool, string arguments)
    {
        var call = GemmaToolCalls.Parse(written, "call_0");

        Assert.AreEqual(("call_0", tool), (call.Id, call.Name));
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(arguments), JsonNode.Parse(call.Arguments)), call.Arguments);
    }

    [TestMethod]
    public void GemmaArgumentsThatWillNotRead_AreKeptAsWritten_ForTheAppToHaveWrittenAgain()
    {
        var call = GemmaToolCalls.Parse("call:delete_file{name:\"test.zip\"", "call_1");

        Assert.AreEqual(("delete_file", "{name:\"test.zip\""), (call.Name, call.Arguments));
    }

    [TestMethod]
    public void AQwenCall_IsReadFromBetweenItsTokens_EachMadeAsItCloses()
    {
        var syntax = ToolCallSyntax.Of(Special(QwenVocabulary), QwenTemplate)!;

        var (seen, made) = Feed(syntax, [OpenFolder], "On it.", QwenOpen, "\n<function=open_folder>\n<parameter=path>\nD:\\docs\n</parameter>\n",
                                "</function>\n", QwenClose, "\n", QwenOpen, "<function=open_folder>\n<parameter=tab>\n3\n</parameter>\n</function>", QwenClose);

        Assert.AreEqual(ToolCallToken.Text, seen[0]);
        Assert.AreEqual(2, made.Count);
        Assert.AreEqual(("call_0", "open_folder", "call_1"), (made[0].Id, made[0].Name, made[1].Id));
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("{\"path\":\"D:\\\\docs\"}"), JsonNode.Parse(made[0].Arguments)), made[0].Arguments);
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("{\"tab\":3}"), JsonNode.Parse(made[1].Arguments)), made[1].Arguments);
    }

    [TestMethod]
    public void QwenArguments_AreTypedByTheToolsSchema_AndUndeclaredOnesReadAsJsonWhereTheyAre()
    {
        var call = QwenToolCalls.Parse("<function=open_folder>\n<parameter=code>\n123\n</parameter>\n<parameter=tab>\n7\n</parameter>\n"
                                     + "<parameter=extra>\n{\"a\": [1, true]}\n</parameter>\n<parameter=note>\nmulti\nline\n</parameter>\n</function>",
                                       "call_0", [OpenFolder]);

        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("{\"code\":\"123\",\"tab\":7,\"extra\":{\"a\":[1,true]},\"note\":\"multi\\nline\"}"),
                                          JsonNode.Parse(call.Arguments)), call.Arguments);
    }

    [TestMethod]
    public void AQwenBooleanWrittenAsPythonPrintsIt_IsReadAsABoolean()
    {
        var tool = new ChatTool("delete_file", "Deletes.", "{\"type\":\"object\",\"properties\":{\"to_bin\":{\"type\":\"boolean\"}}}");

        var call = QwenToolCalls.Parse("<function=delete_file>\n<parameter=to_bin>\nTrue\n</parameter>\n</function>", "call_0", [tool]);

        Assert.AreEqual("{\"to_bin\":true}", call.Arguments);
    }

    [TestMethod]
    public void AQwenCallWithNoFunction_IsPassedOnAsWritten_ForTheAppToHaveWrittenAgain()
    {
        var call = QwenToolCalls.Parse("\n{\"name\": \"open_folder\"}\n", "call_0", [OpenFolder]);

        Assert.AreEqual(("", "{\"name\": \"open_folder\"}"), (call.Name, call.Arguments));
    }

    [TestMethod]
    public void ReasoningSpans_AreTheOnesTheVocabularyHolds()
    {
        Assert.AreEqual(new ThinkingSyntax(21, 22), ThinkingSyntax.Of(Special(new() { ["<think>"] = 21, ["</think>"] = 22 })));
        Assert.AreEqual(new ThinkingSyntax(31, 32), ThinkingSyntax.Of(Special(new() { ["<|channel>"] = 31, ["<channel|>"] = 32 })));
        Assert.IsNull(ThinkingSyntax.Of(Special([])));
    }
}
