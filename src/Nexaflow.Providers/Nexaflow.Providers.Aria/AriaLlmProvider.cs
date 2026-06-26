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
    public const  string ProviderName = "Aria";
    public string Name => ProviderName;

    private readonly AriaClientService          _client;
    private readonly IBackgroundActivityManager _activityManager;

    public AriaLlmProvider(IBackgroundActivityManager activityManager)
    {
        _activityManager = activityManager;
        _client          = new AriaClientService();
    }

    // ── ILlmProvider ───────────────────────────────────────────────────────

    public Task<LlmResponse?> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken         ct = default)
    {
        // Aria uses a simple text pipe; serialise the conversation as a labelled transcript
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            var label = msg.Role switch
            {
                LlmRole.System    => "System",
                LlmRole.Assistant => "Assistant",
                _                 => "User"
            };
            sb.AppendLine($"{label}: {msg.Text}");
        }

        // Extract any attachment file paths for the named-pipe protocol (model is not applicable to Aria)
        var paths = messages
            .Where(m => m.Attachments is { Count: > 0 })
            .SelectMany(m => m.Attachments!)
            .Select(a => a.FilePath)
            .ToList();

        return SendCoreAsync(sb.ToString().TrimEnd(), paths.Count > 0 ? paths : null, ct);
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

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public async ValueTask DisposeAsync() => await _client.DisposeAsync();
}
