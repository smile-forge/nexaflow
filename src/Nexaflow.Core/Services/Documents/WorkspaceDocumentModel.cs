using System.Text;

using Nexaflow.Core.Services.Agent;
using Nexaflow.IO.Documents.Processing;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Documents;

/// <summary>
/// The workspace's Documents model, held for a whole-document job: chosen once when the job starts, so every part of the
/// document is read by the same model, asked as background work nobody waits on token by token, and asked to answer without
/// reasoning first — the passes ask for what to write, not for thought about it.
/// </summary>
/// <param name="provider">The model.</param>
/// <param name="key">Which model it is: its provider and its name.</param>
/// <param name="context">How many tokens it reads at once, as it said or as Nexaflow's own models are taken to.</param>
internal sealed class WorkspaceDocumentModel(ILlmProvider provider, string key, int context) : IDocumentModel
{
    public string Key => key;

    public int ContextTokens => context;

    public int CharsPerToken => RequestTriage.CharsPerToken;

    public async Task<DocumentReply> AnswerAsync(string instructions, string asked, CancellationToken ct)
    {
        var text = new StringBuilder();
        var ended = DocumentReplyEnd.Finished;

        try
        {
            await foreach (var said in provider.StreamAsync([new(LlmRole.System, instructions), new(LlmRole.User, asked)], [],
                                                            LlmUrgency.Background, LlmThinking.Off, ct))
            {
                if (said is LlmTextDelta delta) text.Append(delta.Text);
                else if (said is LlmReplyEnded end) ended = Ended(end.Reason);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (LlmProviderException) { return new(text.ToString(), DocumentReplyEnd.Failed); }

        return new(text.ToString(), ended);
    }

    private static DocumentReplyEnd Ended(LlmReplyEnd reason) => reason switch
    {
        LlmReplyEnd.LengthLimit => DocumentReplyEnd.LengthLimit,
        LlmReplyEnd.ContextFull => DocumentReplyEnd.ContextFull,
        LlmReplyEnd.Circling    => DocumentReplyEnd.Circling,
        _                       => DocumentReplyEnd.Finished,
    };
}
