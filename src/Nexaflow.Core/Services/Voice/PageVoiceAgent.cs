using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Voice;

/// <summary>
/// The agent a voice session talks to: the workspace's, on the page in front of the user at the moment each turn is
/// taken — the page context and its tools, as for a typed request — asked to write its reply to be heard.
/// </summary>
public sealed class PageVoiceAgent(AIService service, Func<IPageViewModel?> currentPage, Func<bool> includeContext) : IVoiceAgent
{
    /// <summary>How a spoken reply is written: short sentences, no formatting read aloud, what speech cannot carry shown.</summary>
    internal const string SpokenStyle =
        "You are talking with the user by voice: everything you write is read aloud as you write it. Answer in short " +
        "spoken sentences, in the language the user speaks, without markdown formatting, lists or emoji. When the answer " +
        "needs a diagram, a table or code, include it as a fenced block or a table — it is shown on screen while you say " +
        "in a few words what it is — and do not read it out. When the user says goodbye or that they are done, call " +
        "end_voice_session.";

    public async Task RunAsync(string userText, IReadOnlyList<LlmMessage> history, IReadOnlyList<IClientTool> sessionTools,
                               IAIResponseHandler handler, CancellationToken ct)
    {
        var options = new AgentRunOptions { History = history, ReplyStyle = SpokenStyle, Tools = sessionTools };
        await service.RunAgentAsync(currentPage(), userText, includeContext(), handler, options, ct);
    }
}
