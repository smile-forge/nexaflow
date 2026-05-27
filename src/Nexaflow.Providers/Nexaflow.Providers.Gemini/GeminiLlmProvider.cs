using Google.GenAI;
using Google.GenAI.Types;
using Nexaflow.Providers.Common;
using System.Text;

namespace Nexaflow.Providers.Gemini;

public sealed class GeminiLlmProvider : ILlmProvider
{
    public const  string ProviderName = "Gemini";
    public string Name => ProviderName;

    private readonly GeminiConfig                _config;
    private readonly IBackgroundActivityManager  _activityManager;

    public GeminiLlmProvider(IBackgroundActivityManager activityManager, GeminiConfig config)
    {
        _activityManager = activityManager;
        _config          = config;
    }

    // ── ILlmProvider ───────────────────────────────────────────────────────

    public Task<LlmResponse?> CompleteAsync(
        IReadOnlyList<LlmMessage>     messages,
        string                        model,
        IReadOnlyList<LlmAttachment>? attachments = null,
        CancellationToken             ct = default)
    {
        // Gemini separates system instructions from the conversation turns
        Content? systemInstruction = null;
        var start = 0;
        if (messages.Count > 0 && messages[0].Role == LlmRole.System)
        {
            systemInstruction = new Content
            {
                Parts = [new Part { Text = messages[0].Text }]
            };
            start = 1;
        }

        var contents = new List<Content>();
        for (var i = start; i < messages.Count; i++)
        {
            var msg        = messages[i];
            var isLastUser = msg.Role == LlmRole.User && i == messages.Count - 1;
            var text       = isLastUser ? BuildUserText(msg.Text, attachments) : msg.Text;

            contents.Add(new Content
            {
                Role  = msg.Role == LlmRole.User ? "user" : "model",
                Parts = [new Part { Text = text }]
            });
        }

        return SendAsync(model, contents, systemInstruction, ct);
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
        string            model,
        List<Content>     contents,
        Content?          systemInstruction,
        CancellationToken ct)
    {
        var activity = _activityManager.StartActivity($"Gemini ({model})…");
        try
        {
            var client = new Client(apiKey: _config.ApiKey);
            var config = systemInstruction is not null
                ? new GenerateContentConfig { SystemInstruction = systemInstruction }
                : null;

            var sb = new StringBuilder();
            await foreach (var chunk in client.Models.GenerateContentStreamAsync(
                               model: model, contents: contents, config: config))
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

    private static string BuildUserText(string prompt, IReadOnlyList<LlmAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return prompt;

        var sb = new StringBuilder(prompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Attached files:");
        foreach (var a in attachments)
            sb.AppendLine($"  {a.FilePath}");

        return sb.ToString();
    }
}
