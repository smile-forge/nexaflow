using Nexaflow.Providers.Common;
using System.Runtime.CompilerServices;
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

    /// <summary>The Aria service answers a request whole, so its reply arrives as a single text event.</summary>
    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
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

        var text = await SendCoreAsync(sb.ToString().TrimEnd(), paths.Count > 0 ? paths : null, ct);
        if (!string.IsNullOrEmpty(text))
            yield return new LlmTextDelta(text);
    }

    // ── Internal ───────────────────────────────────────────────────────────

    private async Task<string?> SendCoreAsync(
        string text,
        IReadOnlyList<string>? attachments,
        CancellationToken ct)
    {
        var activity = _activityManager.StartActivity("Thinking…");
        try
        {
            var ariaResponse = await _client.SendAsync(text, attachments, ct);
            activity.Complete();
            return ariaResponse?.RawText;
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
