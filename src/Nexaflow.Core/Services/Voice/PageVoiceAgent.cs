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
    /// <summary>How a spoken reply is written: short sentences, nothing that cannot be heard, what has to be seen shown.</summary>
    internal const string SpokenStyle =
        "You are speaking with the user rather than writing: everything you write is read aloud as you write it. Reply in " +
        "short, natural spoken sentences, in the language the user speaks, with no Markdown, lists or emoji. Anything that " +
        "has to be seen to be understood — a diagram, a table, some code — goes in a fenced block: it is shown on screen " +
        "while you say in a few words what it is. When something needs doing, do it: call the tool for it, without asking " +
        "first or saying what you will do. If the user adds instructions while you work, carry them out as well, and once " +
        "everything is done, end with one short sentence on what you did and anything you could not. When the user says " +
        "goodbye or that they are done, call end_voice_session.";

    public Task RunAsync(string userText, IReadOnlyList<LlmMessage> history, IReadOnlyList<IClientTool> sessionTools,
    IAIResponseHandler handler, Action<IReadOnlyList<LlmMessage>> ran, Action<IReadOnlyList<LlmMessage>> condensed,
                         LlmThinking thinking, CancellationToken ct)
    {
        var options = new AgentRunOptions
        {
            History    = pageKeepsConversation ? [] : history,
            ReplyStyle = SpokenStyle,
            Tools      = sessionTools,
            Ran        = ran,
            HistoryCondensed = condensed,
            Thinking   = thinking,
        };
        return ui.InvokeAsync(() => service.RunAgentAsync(currentPage(), userText, includeContext(), handler, options, ct),
                              DispatcherPriority.Normal, ct).Task.Unwrap();
    }

    public Task<bool> NeedsThoughtAsync(string userText, IReadOnlyList<LlmMessage> history, CancellationToken ct)
        => service.NeedsThoughtAsync(userText, history, ct);

    /// <summary>Asks the workspace's Disambiguation model what the words meant — the plain yeses and noes are told apart
    /// before it is asked. No model, or no clear choice, is unclear.</summary>
    public async Task<AnswerIntent> JudgeAnswerAsync(string announcement, string said, CancellationToken ct)
    {
        int? picked = await service.DisambiguateOptionAsync(
            $"A voice assistant said: \"{announcement}\" It does that in a few seconds unless the user stops it. " +
            $"While it waited, the user said: \"{said}\"",
            "What did the user mean?",
            [
                ("Go ahead",   "they want the action done now"),
                ("Cancel",     "they want the action not done"),
                ("More to do", "they are giving the assistant a further instruction, and leave the action as it is"),
            ], ct);
        return picked switch
        {
            0 => AnswerIntent.Proceed,
            1 => AnswerIntent.Cancel,
            2 => AnswerIntent.Instruction,
            _ => AnswerIntent.Unclear,
        };
    }

    public bool IsAway => service.ConversationMakingWay;

    public event EventHandler? AwayChanged
    {
        add => service.ProvidersChanged += value;
        remove => service.ProvidersChanged -= value;
    }
}
