using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Models.Messages;
using Nexaflow.Providers.Claude;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Gemini;
using Nexaflow.Providers.Ollama;
using Nexaflow.Providers.OpenAI;
using OllamaSharp.Models.Chat;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// The neutral <see cref="LlmMessage"/> → vendor-SDK request mapping is the most regression-prone
/// provider code and used to be welded to the live network call. Each provider now exposes it as an
/// internal static builder; these tests pin the role mapping, the system-prompt split, and the
/// image-attachment encoding — all without a network.
/// </summary>
[TestClass]
public class RequestMappingTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    private static List<LlmMessage> Convo(bool withImage = false) =>
    [
        new(LlmRole.System, "be helpful"),
        new(LlmRole.User, "hi")
        {
            Attachments = withImage ? [new LlmAttachment("shot.png", "image/png", Png)] : null
        },
        new(LlmRole.Assistant, "yo"),
        new(LlmRole.User, "bye"),
    ];

    // ── Claude ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Claude_SplitsSystem_AndMapsRoles()
    {
        var (system, messages) = ClaudeLlmProvider.BuildRequest(Convo());
        Assert.AreEqual("be helpful", system);
        Assert.AreEqual(3, messages.Count);   // system excluded from turns
    }

    [TestMethod]
    public void Claude_ImageAttachment_BecomesVisionBlock()
    {
        var (_, messages) = ClaudeLlmProvider.BuildRequest(Convo(withImage: true));
        // The user turn with an image carries block content (text + image), not a plain string.
        Assert.IsTrue(messages[0].Content.TryPickContentBlockParams(out var blocks) && blocks!.Count == 2,
            "Expected a text block + an image block for the image-carrying user turn.");
    }

    [TestMethod]
    public void Claude_MaxOutputTokens_ScalesByFamily()
    {
        Assert.AreEqual(4096,  ClaudeLlmProvider.DefaultMaxOutputTokens("claude-3-opus-20240229"));
        Assert.AreEqual(8192,  ClaudeLlmProvider.DefaultMaxOutputTokens("claude-3-5-sonnet-20241022"));
        Assert.AreEqual(32_000, ClaudeLlmProvider.DefaultMaxOutputTokens("claude-opus-4-8"));
        Assert.AreEqual(32_000, ClaudeLlmProvider.DefaultMaxOutputTokens("claude-fable-5"));
    }

    /// <summary>A step that called a tool, what came of it, and something the user said meanwhile.</summary>
    private static List<LlmMessage> ToolStep() =>
    [
        new(LlmRole.System, "be helpful"),
        new(LlmRole.User, "delete old"),
        new(LlmRole.Assistant, "") { ToolCalls = [new LlmToolCall("c1", "delete_file", new JsonObject { ["name"] = "old" })] },
        new(LlmRole.Tool, "deleted") { Answers = new LlmToolCall("c1", "delete_file", []) },
        new(LlmRole.User, "and the zip too"),
    ];

    private static readonly LlmTool DeleteFile = new("delete_file", "Delete a file.", new JsonObject
    {
        ["type"]       = "object",
        ["properties"] = new JsonObject { ["name"] = new JsonObject { ["type"] = "string", ["description"] = "The file." } },
        ["required"]   = new JsonArray("name"),
    });

    [TestMethod]
    public void Claude_ACallIsAToolUseBlock_AndWhatCameOfIt_OpensTheNextUserTurn_WithWhatTheUserSaidBesides()
    {
        var (_, messages) = ClaudeLlmProvider.BuildRequest(ToolStep());

        Assert.AreEqual(3, messages.Count, "the results and the user's words are one user turn");
        var called   = JsonSerializer.Serialize(messages[1]);
        var answered = JsonSerializer.Serialize(messages[2]);
        StringAssert.Contains(called, "\"type\":\"tool_use\"");
        StringAssert.Contains(called, "\"id\":\"c1\"");
        StringAssert.Contains(called, "\"input\":{\"name\":\"old\"}");
        StringAssert.Contains(answered, "\"type\":\"tool_result\"");
        StringAssert.Contains(answered, "\"tool_use_id\":\"c1\"");
        Assert.IsTrue(answered.IndexOf("deleted", StringComparison.Ordinal) < answered.IndexOf("and the zip too", StringComparison.Ordinal),
                      "the result comes first, then what the user said");
    }

    [TestMethod]
    public void Claude_ATool_IsDeclaredWithItsSchema()
    {
        var declared = JsonSerializer.Serialize(ClaudeLlmProvider.Declare(DeleteFile));

        StringAssert.Contains(declared, "\"name\":\"delete_file\"");
        StringAssert.Contains(declared, "\"input_schema\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"");
    }

    [TestMethod]
    public async Task Claude_ACallIsRead_OnceItsBlockCloses_FromThePiecesOfItsArguments()
    {
        string[] wire =
        [
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"On it."}}""",
            """{"type":"content_block_stop","index":0}""",
            """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_1","name":"delete_file","input":{}}}""",
            """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"name\": "}}""",
            """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"\"old\"}"}}""",
            """{"type":"content_block_stop","index":1}""",
        ];

        var read = new List<LlmStreamEvent>();
        await foreach (var e in ClaudeLlmProvider.Events(Streamed(wire.Select(w => JsonSerializer.Deserialize<RawMessageStreamEvent>(w)!))))
            read.Add(e);

        Assert.AreEqual("On it.", ((LlmTextDelta)read[0]).Text);
        var call = ((LlmToolCallRequested)read[1]).Call;
        Assert.AreEqual(("toolu_1", "delete_file", "old"), (call.Id, call.Name, (string?)call.Arguments["name"]));
    }

    private static async IAsyncEnumerable<T> Streamed<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    // ── OpenAI ────────────────────────────────────────────────────────────

    [TestMethod]
    public void OpenAI_MapsRoles_InOrder()
    {
        var messages = OpenAILlmProvider.BuildChatMessages(Convo());
        Assert.AreEqual(4, messages.Count);   // system stays inline for OpenAI
        Assert.IsInstanceOfType<global::OpenAI.Chat.SystemChatMessage>(messages[0]);
        Assert.IsInstanceOfType<global::OpenAI.Chat.UserChatMessage>(messages[1]);
        Assert.IsInstanceOfType<global::OpenAI.Chat.AssistantChatMessage>(messages[2]);
    }

    [TestMethod]
    public void OpenAI_ImageAttachment_BecomesVisionPart()
    {
        var messages = OpenAILlmProvider.BuildChatMessages(Convo(withImage: true));
        var user = (global::OpenAI.Chat.UserChatMessage)messages[1];
        Assert.AreEqual(2, user.Content.Count, "Expected a text part + an image part.");
    }

    [TestMethod]
    public void OpenAI_VisionHeuristic_ExcludesSpecialisedModels()
    {
        Assert.IsTrue(OpenAILlmProvider.ModelSupportsVision("gpt-4o"));
        Assert.IsTrue(OpenAILlmProvider.ModelSupportsVision("gpt-4.1-mini"));
        Assert.IsFalse(OpenAILlmProvider.ModelSupportsVision("text-embedding-3-large"));
        Assert.IsFalse(OpenAILlmProvider.ModelSupportsVision("gpt-3.5-turbo"));
        Assert.IsFalse(OpenAILlmProvider.ModelSupportsVision(""));
    }

    [TestMethod]
    public void OpenAI_ACallRidesOnTheReply_AndWhatCameOfIt_IsAToolMessageAnsweringIt()
    {
        var messages = OpenAILlmProvider.BuildChatMessages(ToolStep());

        Assert.AreEqual(5, messages.Count);
        var called = (global::OpenAI.Chat.AssistantChatMessage)messages[2];
        var call   = called.ToolCalls.Single();
        Assert.AreEqual(("c1", "delete_file", "{\"name\":\"old\"}"), (call.Id, call.FunctionName, call.FunctionArguments.ToString()));
        var answered = (global::OpenAI.Chat.ToolChatMessage)messages[3];
        Assert.AreEqual(("c1", "deleted"), (answered.ToolCallId, answered.Content.Single().Text));
        Assert.IsInstanceOfType<global::OpenAI.Chat.UserChatMessage>(messages[4], "what the user said besides follows the results");
    }

    [TestMethod]
    public void OpenAI_ATool_IsAFunctionWithItsSchema()
    {
        var declared = OpenAILlmProvider.Declare(DeleteFile);

        Assert.AreEqual("delete_file", declared.FunctionName);
        StringAssert.Contains(declared.FunctionParameters.ToString(), "\"required\":[\"name\"]");
    }

    [TestMethod]
    public async Task OpenAI_ACallIsRead_FromItsPieces_TheFirstNamingIt()
    {
        string[] wire =
        [
            """{"id":"x","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{"role":"assistant","content":"On it."}}]}""",
            """{"id":"x","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"delete_file","arguments":"{\"name\""}}]}}]}""",
            """{"id":"x","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":": \"old\"}"}}]}}]}""",
            """{"id":"x","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}""",
        ];

        var read = new List<LlmStreamEvent>();
        await foreach (var e in OpenAILlmProvider.Events(Streamed(wire.Select(w =>
                           System.ClientModel.Primitives.ModelReaderWriter.Read<global::OpenAI.Chat.StreamingChatCompletionUpdate>(BinaryData.FromString(w))!))))
            read.Add(e);

        Assert.AreEqual("On it.", ((LlmTextDelta)read[0]).Text);
        var call = ((LlmToolCallRequested)read[1]).Call;
        Assert.AreEqual(("call_1", "delete_file", "old"), (call.Id, call.Name, (string?)call.Arguments["name"]));
    }

    // ── Gemini ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Gemini_SplitsSystem_AndMapsRoles()
    {
        var (system, contents) = GeminiLlmProvider.BuildRequest(Convo());
        Assert.IsNotNull(system);
        Assert.AreEqual(3, contents.Count);
        Assert.AreEqual("user",  contents[0].Role);
        Assert.AreEqual("model", contents[1].Role);
    }

    [TestMethod]
    public void Gemini_ImageAttachment_BecomesInlineDataPart()
    {
        var (_, contents) = GeminiLlmProvider.BuildRequest(Convo(withImage: true));
        Assert.AreEqual(2, contents[0].Parts!.Count, "Expected a text part + an inline-data part.");
        Assert.IsNotNull(contents[0].Parts![1].InlineData);
    }

    [TestMethod]
    public void Gemini_ACallIsAFunctionCallPart_WithItsSignature_AndWhatCameOfIt_OpensTheNextUserTurn()
    {
        var step = ToolStep();
        step[2] = step[2] with { ToolCalls = [step[2].ToolCalls![0] with { Signature = [7, 7] }] };

        var (_, contents) = GeminiLlmProvider.BuildRequest(step);

        Assert.AreEqual(3, contents.Count, "the responses and the user's words are one user turn");
        var called = contents[1].Parts!.Single();
        Assert.AreEqual(("model", "c1", "delete_file"), (contents[1].Role, called.FunctionCall?.Id, called.FunctionCall?.Name));
        Assert.AreEqual("old", called.FunctionCall!.Args!["name"].ToString());
        CollectionAssert.AreEqual(new byte[] { 7, 7 }, called.ThoughtSignature, "a thinking model's signature goes back with its call");
        var answered = contents[2].Parts!;
        Assert.AreEqual(("c1", "deleted"), (answered[0].FunctionResponse?.Id, answered[0].FunctionResponse?.Response?["output"]));
        Assert.AreEqual("and the zip too", answered[1].Text);
    }

    [TestMethod]
    public void Gemini_ATool_IsAFunctionWithItsSchema()
    {
        var declared = GeminiLlmProvider.Declare(DeleteFile);

        Assert.AreEqual("delete_file", declared.Name);
        Assert.AreSame(DeleteFile.Parameters, declared.ParametersJsonSchema);
    }

    [TestMethod]
    public async Task Gemini_ACallIsRead_WithItsSignature_AndItsThoughtsAreNeverText()
    {
        var chunk = new global::Google.GenAI.Types.GenerateContentResponse
        {
            Candidates =
            [
                new global::Google.GenAI.Types.Candidate
                {
                    Content = new global::Google.GenAI.Types.Content
                    {
                        Parts =
                        [
                            new global::Google.GenAI.Types.Part { Text = "Weighing it up.", Thought = true },
                            new global::Google.GenAI.Types.Part { Text = "On it." },
                            new global::Google.GenAI.Types.Part
                            {
                                FunctionCall     = new global::Google.GenAI.Types.FunctionCall { Name = "delete_file", Args = new() { ["name"] = "old" } },
                                ThoughtSignature = [1, 2],
                            },
                        ],
                    },
                },
            ],
        };

        var read = new List<LlmStreamEvent>();
        await foreach (var e in GeminiLlmProvider.Events(Streamed([chunk]))) read.Add(e);

        Assert.AreEqual(2, read.Count);
        Assert.AreEqual("On it.", ((LlmTextDelta)read[0]).Text);
        var call = ((LlmToolCallRequested)read[1]).Call;
        Assert.AreEqual(("call_0", "delete_file", "old"), (call.Id, call.Name, (string?)call.Arguments["name"]));
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, call.Signature);
    }

    // ── Ollama ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Ollama_KeepsSystemInline_AndMapsRoles()
    {
        var messages = OllamaLlmProvider.BuildMessages(Convo());
        Assert.AreEqual(4, messages.Count);
        Assert.AreEqual(ChatRole.System,    messages[0].Role);
        Assert.AreEqual(ChatRole.User,      messages[1].Role);
        Assert.AreEqual(ChatRole.Assistant, messages[2].Role);
    }

    [TestMethod]
    public void Ollama_ACallRidesOnTheReply_AndWhatCameOfIt_IsAToolMessageNamingTheTool()
    {
        var messages = OllamaLlmProvider.BuildMessages(ToolStep());

        Assert.AreEqual(5, messages.Count);
        var call = messages[2].ToolCalls!.Single();
        Assert.AreEqual(("c1", "delete_file", "old"), (call.Id, call.Function!.Name, call.Function.Arguments!["name"]?.ToString()));
        Assert.AreEqual((ChatRole.Tool, "deleted", "delete_file"), (messages[3].Role, messages[3].Content, messages[3].ToolName));
        Assert.AreEqual(ChatRole.User, messages[4].Role);
    }

    /// <summary>Answers a chat request with the lines given, keeping the request it was sent.</summary>
    private sealed class OllamaServer(params string[] lines) : HttpMessageHandler
    {
        public string? Sent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sent = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(string.Join('\n', lines) + "\n") };
        }
    }

    [TestMethod]
    public async Task Ollama_ToolsGoOutWithTheirSchema_AndACallComesBackWhole()
    {
        var server = new OllamaServer(
            """{"model":"m","created_at":"2026-01-01T00:00:00Z","message":{"role":"assistant","content":"On it."},"done":false}""",
            """{"model":"m","created_at":"2026-01-01T00:00:00Z","message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"delete_file","arguments":{"name":"old"}}}]},"done":false}""",
            """{"model":"m","created_at":"2026-01-01T00:00:00Z","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop"}""");
        var client  = new OllamaSharp.OllamaApiClient(new HttpClient(server) { BaseAddress = new Uri("http://localhost:11434") });
        var request = new ChatRequest { Model = "m", Messages = OllamaLlmProvider.BuildMessages(ToolStep()), Tools = [OllamaLlmProvider.Declare(DeleteFile)] };

        var read = new List<LlmStreamEvent>();
        await foreach (var e in OllamaLlmProvider.Events(client.ChatAsync(request))) read.Add(e);

        StringAssert.Contains(server.Sent, "\"parameters\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"");
        StringAssert.Contains(server.Sent, "\"tool_name\":\"delete_file\"");
        Assert.AreEqual("On it.", ((LlmTextDelta)read[0]).Text);
        var call = ((LlmToolCallRequested)read[1]).Call;
        Assert.AreEqual(("call_0", "delete_file", "old"), (call.Id, call.Name, (string?)call.Arguments["name"]));
    }

    [TestMethod]
    public void Ollama_VisionHeuristic_MatchesKnownFamilies()
    {
        Assert.IsTrue(OllamaLlmProvider.ModelSupportsVision("llava:13b"));
        Assert.IsTrue(OllamaLlmProvider.ModelSupportsVision("llama3.2-vision"));
        Assert.IsFalse(OllamaLlmProvider.ModelSupportsVision("llama3.1:8b"));
    }

    [TestMethod]
    public void OpenAI_AModelThatHears_GetsARecordingAsAudio_AnyOtherGetsItNamed()
    {
        LlmMessage[] convo = [new(LlmRole.User, "What is said?") { Attachments = [new LlmAttachment("clip.wav", "audio/wav", [1, 2, 3, 4])] }];

        var heard = (global::OpenAI.Chat.UserChatMessage)OpenAILlmProvider.BuildChatMessages(convo, hearsAudio: true)[0];
        var named = (global::OpenAI.Chat.UserChatMessage)OpenAILlmProvider.BuildChatMessages(convo, hearsAudio: false)[0];

        Assert.AreEqual(2, heard.Content.Count, "a text part and an audio part");
        #pragma warning disable OPENAI001 // the SDK marks chat audio input for evaluation
        Assert.IsNotNull(heard.Content[1].InputAudioBytes);
        #pragma warning restore OPENAI001
        Assert.AreEqual(1, named.Content.Count);
        StringAssert.Contains(named.Content[0].Text, "clip.wav");
    }

    [TestMethod]
    [DataRow("gpt-4o-audio-preview", true)]
    [DataRow("gpt-audio", true)]
    [DataRow("gpt-4o", false)]
    [DataRow("gpt-4o-realtime-preview", false)]
    [DataRow("gpt-4o-transcribe", false)]
    public void OpenAI_ModelsThatHear_AreNamedForIt(string model, bool hears)
        => Assert.AreEqual(hears, OpenAILlmProvider.ModelHearsAudio(model));
}
