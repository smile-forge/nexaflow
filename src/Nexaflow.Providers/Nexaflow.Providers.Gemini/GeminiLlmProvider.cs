using Google.GenAI;
using Google.GenAI.Types;
using Nexaflow.Providers.Common;
using System.Text;

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

    public GeminiLlmProvider(IBackgroundActivityManager activityManager, GeminiConfig config, ProviderModel model)
    {
        _activityManager = activityManager;
        _config          = config;
        _model           = model.Model;
    }

    // ── ILlmProvider ───────────────────────────────────────────────────────

    public Task<LlmResponse?> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken         ct = default)
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

        return SendAsync(contents, systemInstruction, ct);
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var client = new Client(apiKey: _config.ApiKey);
            var pager  = await client.Models.ListAsync();
            var names  = new List<string>();

            await foreach (var m in pager)
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
            return names;
        }
        catch
        {
            return [];
        }
    }

    // ── Internal ───────────────────────────────────────────────────────────

    private async Task<LlmResponse?> SendAsync(
        List<Content>     contents,
        Content?          systemInstruction,
        CancellationToken ct)
    {
        var activity = _activityManager.StartActivity($"Gemini ({_model})…");
        try
        {
            var client = new Client(apiKey: _config.ApiKey);
            var config = systemInstruction is not null
                ? new GenerateContentConfig { SystemInstruction = systemInstruction }
                : null;

            var sb = new StringBuilder();
            await foreach (var chunk in client.Models.GenerateContentStreamAsync(
                               model: _model, contents: contents, config: config))
            {
                var text = chunk.Text;
                if (text is not null)
                    sb.Append(text);
            }

            activity.Complete();
            var result = sb.ToString();
            return string.IsNullOrEmpty(result) ? null : new LlmResponse(result);
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);
            throw new LlmProviderException($"Gemini request failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Builds the final user turn's parts. Image attachments become inline-data parts; non-image
    /// attachments keep the path-as-text behaviour appended to the prompt text part.
    /// </summary>
    private static List<Part> BuildUserParts(string prompt, IReadOnlyList<LlmAttachment>? attachments)
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
