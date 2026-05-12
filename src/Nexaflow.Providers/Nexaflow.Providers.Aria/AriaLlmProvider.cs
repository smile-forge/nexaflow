using Nexaflow.Providers.Common;
using System.Text;

namespace Nexaflow.Providers.Aria;

/// <summary>
/// <see cref="ILlmProvider"/> implementation backed by the Aria named-pipe service.
/// Activity is reported through the <see cref="IBackgroundActivityManager"/> supplied
/// at construction; the provider has no direct dependency on WPF or observable state.
/// </summary>
public sealed class AriaLlmProvider : ILlmProvider, IAsyncDisposable
{
    private readonly AriaClientService        _client;
    private readonly IBackgroundActivityManager _activityManager;

    public AriaLlmProvider(IBackgroundActivityManager activityManager)
    {
        _activityManager = activityManager;
        _client          = new AriaClientService();
    }

    // ── ILlmProvider ───────────────────────────────────────────────────────

    public Task<LlmResponse?> QueryAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<string>? attachments = null,
        CancellationToken ct = default)
    {
        var text = string.IsNullOrWhiteSpace(systemPrompt)
            ? userPrompt
            : $"{systemPrompt}\n\n{userPrompt}";

        return SendCoreAsync(text, attachments, ct);
    }

    public Task<LlmResponse?> ChatAsync(
        IReadOnlyList<LlmMessage> history,
        string newUserPrompt,
        IReadOnlyList<string>? attachments = null,
        CancellationToken ct = default)
    {
        // Aria uses a simple text pipe, so we serialise the conversation as a
        // labelled transcript and append the new user turn.
        var sb = new StringBuilder();
        foreach (var msg in history)
            sb.AppendLine(msg.IsUser ? $"User: {msg.Text}" : $"Assistant: {msg.Text}");
        sb.Append($"User: {newUserPrompt}");

        return SendCoreAsync(sb.ToString(), attachments, ct);
    }

    // ── Internal ───────────────────────────────────────────────────────────

    private async Task<LlmResponse?> SendCoreAsync(
        string text,
        IReadOnlyList<string>? attachments,
        CancellationToken ct)
    {
        var activity = _activityManager.StartActivity("Thinking…");
        try
        {
            var ariaResponse = await _client.SendAsync(text, attachments, ct);
            activity.Complete();

            if (ariaResponse is null) return null;
            return new LlmResponse(ariaResponse.RawText, ariaResponse.FocusTab);
        }
        catch (AriaClientService.AriaConnectionException ex)
        {
            activity.Fail(ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);
            throw;
        }
    }

    public async ValueTask DisposeAsync() => await _client.DisposeAsync();
}
