using System.Text.Json;
using Google.GenAI;
using Google.GenAI.Types;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.Gemini;

public sealed class GeminiLlmProvider : ILlmProvider
{
    public const  string ProviderName = "Gemini";
    public string Name => ProviderName;

    /// <summary>Gemini models are multimodal; image attachments are sent as inline data parts.</summary>
    public bool SupportsImages => true;

    public DataResidency Residency => DataResidency.LeavesMachine;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_config.ApiKey);

    private readonly GeminiConfig                _config;
    private readonly IBackgroundActivityManager  _activityManager;
    private readonly string                      _model;
    private Client?                              _client;

    /// <summary>Cached from /models metadata on first ask; 0 = not yet known.</summary>
    private int _contextLength;

    public string? LastModelListError { get; private set; }

    public GeminiLlmProvider(IBackgroundActivityManager activityManager, GeminiConfig config, ProviderModel model)
    {
        _activityManager = activityManager;
        _config          = config;
        _model           = model.Model;
    }

    // The pooled instance is long-lived with a fixed config payload — build the SDK client once.
    private Client Client => _client ??= new Client(apiKey: _config.ApiKey);

    // ── ILlmProvider ───────────────────────────────────────────────────────

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken         ct = default)
        => Stream(messages, [], ct);

    /// <summary>Gemini calls the tools it is offered as function calls, each whole in the chunk that carries it.</summary>
    public bool SupportsToolCalls => true;

    public IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmTool>    tools,
        LlmUrgency                urgency,
        CancellationToken         ct = default)
        => Stream(messages, tools, ct);

    private IAsyncEnumerable<LlmStreamEvent> Stream(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmTool> tools, CancellationToken ct)
    {
        var (systemInstruction, contents) = BuildRequest(messages);
        var config = systemInstruction is not null || tools.Count > 0
            ? new GenerateContentConfig
            {
                SystemInstruction = systemInstruction,
                Tools             = tools.Count > 0 ? [new Tool { FunctionDeclarations = [.. tools.Select(Declare)] }] : null,
            }
            : null;

        return LlmStreamRunner.RunAsync(_activityManager, $"Gemini ({_model})…", ProviderName,
            ct => Events(Client.Models.GenerateContentStreamAsync(model: _model, contents: contents, config: config, cancellationToken: ct)), ct);
    }

    /// <summary>A tool as Gemini declares it: a function, its parameters' JSON Schema as given.</summary>
    internal static FunctionDeclaration Declare(LlmTool tool)
        => new() { Name = tool.Name, Description = tool.Description, ParametersJsonSchema = tool.Parameters };

    /// <summary>The reply as it streams: its text — never its thoughts — and each call it makes, with the signature a
    /// thinking model sends to have back with it. Gemini gives a call an id only sometimes; one without is numbered.</summary>
    internal static async IAsyncEnumerable<LlmStreamEvent> Events(IAsyncEnumerable<GenerateContentResponse> chunks)
    {
        int made = 0;
        await foreach (var chunk in chunks)
            foreach (var part in chunk.Candidates?.FirstOrDefault()?.Content?.Parts ?? [])
            {
                if (part.FunctionCall is { } call)
                {
                    var arguments = call.Args is null ? null : JsonSerializer.Serialize(call.Args);
                    yield return new LlmToolCallRequested(
                        LlmToolCall.Read(call.Id ?? $"call_{made}", call.Name ?? "", arguments) with { Signature = part.ThoughtSignature });
                    made++;
                }
                else if (part.Thought != true && !string.IsNullOrEmpty(part.Text))
                    yield return new LlmTextDelta(part.Text);
            }
    }

    /// <summary>Maps the neutral message list to Gemini's wire shape (system instruction split out;
    /// user/model roles; images as inline-data parts; a reply's calls as function-call parts). What came of a step's calls
    /// is the user turn after it — every function response, then anything the user says besides. Internal + static so the
    /// mapping is unit-testable.</summary>
    internal static (Content? SystemInstruction, List<Content> Contents) BuildRequest(
        IReadOnlyList<LlmMessage> messages)
    {
        // Gemini separates system instructions from the conversation turns
        var (systemText, turns) = PromptComposer.SplitSystemPrompt(messages);
        Content? systemInstruction = systemText is null
            ? null
            : new Content { Parts = [new Part { Text = systemText }] };

        var contents = new List<Content>();
        List<Part>? results = null;   // a step's function responses, awaiting the user turn they open
        foreach (var msg in turns)
        {
            if (msg.Role == LlmRole.Tool)
            {
                (results ??= []).Add(new Part
                {
                    FunctionResponse = new FunctionResponse
                    {
                        Id       = msg.Answers?.Id,
                        Name     = msg.Answers?.Name,
                        Response = new Dictionary<string, object> { ["output"] = msg.Text },
                    },
                });
                continue;
            }
            if (results is not null)
            {
                if (msg.Role == LlmRole.User) results.AddRange(BuildUserParts(msg.Text, msg.Attachments));
                contents.Add(new Content { Role = "user", Parts = results });
                results = null;
                if (msg.Role == LlmRole.User) continue;
            }

            contents.Add(new Content
            {
                Role  = msg.Role == LlmRole.User ? "user" : "model",
                Parts = msg.Role == LlmRole.User ? BuildUserParts(msg.Text, msg.Attachments) : ModelParts(msg),
            });
        }
        if (results is not null)
            contents.Add(new Content { Role = "user", Parts = results });
        return (systemInstruction, contents);
    }

    /// <summary>A reply: its text, then each call it made, with the signature it came with.</summary>
    private static List<Part> ModelParts(LlmMessage msg)
    {
        if (msg.ToolCalls is not { Count: > 0 } calls) return [new Part { Text = msg.Text }];

        var parts = new List<Part>();
        if (!string.IsNullOrWhiteSpace(msg.Text)) parts.Add(new Part { Text = msg.Text });
        foreach (var call in calls)
            parts.Add(new Part
            {
                FunctionCall     = new FunctionCall
                {
                    Id   = call.Id,
                    Name = call.Name,
                    Args = JsonSerializer.Deserialize<Dictionary<string, object>>(call.Arguments.ToJsonString()),
                },
                ThoughtSignature = call.Signature,
            });
        return parts;
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var pager = await Client.Models.ListAsync(cancellationToken: ct);
            var names = new List<string>();

            await foreach (var m in pager.WithCancellation(ct))
            {
                if (m.Name is null || !m.Name.Contains("gemini", StringComparison.OrdinalIgnoreCase))
                    continue;

                // API returns "models/gemini-2.0-flash" — strip the prefix
                var name = m.Name.StartsWith("models/", StringComparison.Ordinal)
                    ? m.Name["models/".Length..]
                    : m.Name;
                names.Add(name);
            }

            names.Sort();
            LastModelListError = null;
            return names;
        }
        catch (Exception ex)
        {
            LastModelListError = ex.Message;
            return [];
        }
    }

    public async Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_model)) return null;
        if (_contextLength == 0)
        {
            // /models metadata carries the real per-model input token limit.
            try
            {
                var meta = await Client.Models.GetAsync(_model, null, ct);
                _contextLength = meta?.InputTokenLimit ?? 0;
            }
            catch { /* metadata unavailable — stay unknown */ }
        }
        return _contextLength > 0 ? new ModelInfo(_contextLength, DisplayName: _model) : null;
    }

    /// <summary>
    /// Builds the final user turn's parts. Image attachments become inline-data parts; non-image
    /// attachments keep the path-as-text behaviour appended to the prompt text part, which is left out when empty.
    /// </summary>
    internal static List<Part> BuildUserParts(string prompt, IReadOnlyList<LlmAttachment>? attachments)
    {
        var (images, files) = PromptComposer.PartitionAttachments(attachments);
        var text = PromptComposer.AppendFileList(prompt, files);

        var parts = new List<Part>();
        if (text.Length > 0) parts.Add(new Part { Text = text });   // Gemini refuses an empty text part
        foreach (var img in images)
            parts.Add(new Part
            {
                InlineData = new Blob { Data = img.ReadBytes(), MimeType = img.ResolvedMimeType }
            });

        return parts;
    }
}
