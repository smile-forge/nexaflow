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

    public Task<LlmResponse?> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken         ct = default)
    {
        var (systemInstruction, contents) = BuildRequest(messages);
        var config = systemInstruction is not null
            ? new GenerateContentConfig { SystemInstruction = systemInstruction }
            : null;

        return LlmStreamRunner.RunAsync(_activityManager, $"Gemini ({_model})…", ProviderName,
            ct => Deltas(contents, config, ct), ct);
    }

    private async IAsyncEnumerable<string?> Deltas(
        List<Content> contents, GenerateContentConfig? config,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in Client.Models.GenerateContentStreamAsync(
                           model: _model, contents: contents, config: config, cancellationToken: ct))
            yield return chunk.Text;
    }

    /// <summary>Maps the neutral message list to Gemini's wire shape (system instruction split out;
    /// user/model roles; images as inline-data parts). Internal + static so the mapping is unit-testable.</summary>
    internal static (Content? SystemInstruction, List<Content> Contents) BuildRequest(
        IReadOnlyList<LlmMessage> messages)
    {
        // Gemini separates system instructions from the conversation turns
        var (systemText, turns) = PromptComposer.SplitSystemPrompt(messages);
        Content? systemInstruction = systemText is null
            ? null
            : new Content { Parts = [new Part { Text = systemText }] };

        var contents = new List<Content>();
        foreach (var msg in turns)
        {
            List<Part> parts = msg.Role == LlmRole.User
                ? BuildUserParts(msg.Text, msg.Attachments)
                : [new Part { Text = msg.Text }];

            contents.Add(new Content
            {
                Role  = msg.Role == LlmRole.User ? "user" : "model",
                Parts = parts
            });
        }
        return (systemInstruction, contents);
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
    /// attachments keep the path-as-text behaviour appended to the prompt text part.
    /// </summary>
    internal static List<Part> BuildUserParts(string prompt, IReadOnlyList<LlmAttachment>? attachments)
    {
        var (images, files) = PromptComposer.PartitionAttachments(attachments);
        var text = PromptComposer.AppendFileList(prompt, files);

        var parts = new List<Part> { new Part { Text = text } };
        foreach (var img in images)
            parts.Add(new Part
            {
                InlineData = new Blob { Data = img.ReadBytes(), MimeType = img.ResolvedMimeType }
            });

        return parts;
    }
}
