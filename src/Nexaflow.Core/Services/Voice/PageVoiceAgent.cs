using System.Windows.Threading;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Voice;

/// <summary>
/// The agent a voice session talks to: the workspace's, on the page in front of the user at the moment each turn is
/// taken — the page context and its tools, as for a typed request — asked to write its reply to be heard. A page that
/// keeps the conversation itself, the conversation page, already gives the agent every turn as its context, so the
/// session's own record is not given again.
/// <para>Each turn runs on the UI thread, as a typed request does: the page, its tools and the handler that shows the
/// conversation all live there — a tool refreshes the page it acted on as it returns.</para>
/// </summary>
public sealed class PageVoiceAgent(AIService service, Dispatcher ui, Func<IPageViewModel?> currentPage, Func<bool> includeContext, bool pageKeepsConversation)
    : IVoiceAgent
{
    /// <summary>How a spoken reply is written: short sentences, no formatting read aloud, what speech cannot carry shown.</summary>
    internal const string SpokenStyle =
        "You are talking with the user by voice: everything you write is read aloud as you write it. Answer in short " +
        "spoken sentences, in the language the user speaks, without markdown formatting, lists or emoji. When the answer " +
        "needs a diagram, a table or code, include it as a fenced block or a table — it is shown on screen while you say " +
        "in a few words what it is — and do not read it out. When you use a tool, write only its block, with nothing " +
        "before it: what you are about to do is announced for you. When the user says goodbye or that they are done, " +
        "call end_voice_session.";

    public Task RunAsync(string userText, IReadOnlyList<LlmMessage> history, IReadOnlyList<IClientTool> sessionTools,
                         IAIResponseHandler handler, CancellationToken ct)
    {
        var options = new AgentRunOptions { History = pageKeepsConversation ? [] : history, ReplyStyle = SpokenStyle, Tools = sessionTools };
        return ui.InvokeAsync(() => service.RunAgentAsync(currentPage(), userText, includeContext(), handler, options, ct),
                              DispatcherPriority.Normal, ct).Task.Unwrap();
    }
}
