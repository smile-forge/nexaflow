using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat;

/// <summary>
/// Background task that deletes conversations whose last activity is older than the retention cutoff.
/// Queued when the AI Chat tab opens (see <see cref="ViewModels.AiChatViewModel"/>).
/// </summary>
public sealed class ConversationPurgeTask : IBackgroundTask
{
    private readonly IAIService _ai;
    private readonly DateTime   _cutoff;

    public ConversationPurgeTask(IAIService ai, DateTime cutoff)
    {
        _ai     = ai;
        _cutoff = cutoff;
    }

    public string Description => "Purging old conversations";

    public async Task RunAsync(CancellationToken ct)
    {
        var all = await _ai.LoadConversationsAsync();
        foreach (var rec in all)
        {
            ct.ThrowIfCancellationRequested();
            if (LastActivity(rec) < _cutoff)
                await _ai.DeleteConversationAsync(rec.Id);
        }
    }

    /// <summary>The most recent message time, or the start time when there are no messages.</summary>
    internal static DateTime LastActivity(ConversationRecord rec)
        => rec.Messages.Count > 0 ? rec.Messages.Max(m => m.Timestamp) : rec.StartedAt;
}
