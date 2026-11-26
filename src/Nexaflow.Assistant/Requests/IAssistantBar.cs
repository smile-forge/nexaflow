using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Assistant.Requests;

/// <summary>
/// A window's AI bar, as the requests sent from it see it: where a reply is shown when the page it is about does not show the
/// assistant itself, how a request answered there is run, whether the page's context goes with a request, and what the bar does
/// with what a run hands back.
/// </summary>
public interface IAssistantBar
{
    /// <summary>Where the reply to a request is shown when the page it is about does not show the assistant itself.</summary>
    IAIResponseHandler Surface { get; }

    /// <summary>Whether the page's context goes with a request the agent answers — the bar's toggle, read as each turn runs.</summary>
    bool IncludeContext { get; }

    /// <summary>How a request answered on <see cref="Surface"/> is run: a turn of the bar's own conversation, its reply written for the bar.</summary>
    AgentRunOptions RunOptions();

    /// <summary>The agent answered by putting words in the bar for the user to finish them: shows them being typed in. Ends once they are.</summary>
    Task PrefillAsync(string text);

    /// <summary>The files the run just answered touched, carried into a "Continue as Conversation" of the bar's reply.</summary>
    void Touched(IReadOnlyList<string> paths);
}
