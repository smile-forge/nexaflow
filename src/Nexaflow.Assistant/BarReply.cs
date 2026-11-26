using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Localization;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Assistant;

/// <summary>Where the AI bar's answer is: working with nothing to show yet, writing a reply as it arrives, waiting on an
/// approval, or showing what was said.</summary>
public enum BarReplyStage { Message, Approval, Running, Writing }

/// <summary>What the AI bar's answer needs of the window it is shown in: the runtime it answers for, and the page the user is on.</summary>
public interface IBarReplyHost
{
    /// <summary>The runtime the window shows now.</summary>
    WorkspaceRuntime CurrentRuntime { get; }

    /// <summary>The tab the user is on, which a conversation continued from the bar takes as context.</summary>
    Page? ActiveTab { get; }

    /// <summary>The page the user is on, which may take that tab as a context item.</summary>
    IPageViewModel? ActivePageViewModel { get; }
}

/// <summary>
/// The answer to a request typed into the AI bar, as any toolkit's bar shows it: the <see cref="IAIResponseHandler"/> the agent
/// answers on when the page in front of the user does not answer itself. It holds where the answer is (<see cref="Stage"/>), what
/// the assistant is doing, the reply, an approval waiting on the user's yes or no, and the one line the bar shows of all of it.
/// <para>
/// Requests typed into the bar are one conversation (<see cref="Conversation"/>), each continuing the last, until the answer is
/// dismissed or continued as a conversation of its own. A request that ends without a reply leaves the last one in place.
/// The agent calls in from any thread; what it shows changes on the UI thread.
/// </para>
/// </summary>
public sealed partial class BarReply : ObservableObject, IAIResponseHandler
{
    /// <summary>How a request answered in the bar is to be written: the answer first, briefly, the rest only when it is needed.</summary>
    public const string Style =
        "Your reply is shown on one line of the AI bar, the whole of it a hover away: lead with the answer in a sentence or " +
        "two, and write more only when the request needs it.";

    private const string DefaultName = "Aria";

    private readonly IUiThread _ui;
    private readonly IBarReplyHost _host;
    private TaskCompletionSource<bool>? _approval;
    private Page? _originTab;

    /// <param name="ui">The thread the window runs on.</param>
    /// <param name="host">The window the bar is in.</param>
    public BarReply(IUiThread ui, IBarReplyHost host)
    {
        _ui   = ui;
        _host = host;
    }

    /// <summary>The conversation the requests typed into the bar hold.</summary>
    public BarConversation Conversation { get; } = new();

    /// <summary>The assistant's name, as the workspace's persona gives it.</summary>
    [ObservableProperty] private string _assistantName = DefaultName;

    /// <summary>What was asked.</summary>
    [ObservableProperty] private string _request = string.Empty;

    /// <summary>The reply as markdown: as it is written, finished, or what an approval explains.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Line))] private string _text = string.Empty;

    /// <summary>What the assistant is doing.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Line))] private string _progress = string.Empty;

    /// <summary>The question an approval asks.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Line))] private string _question = string.Empty;

    /// <summary>Whether the approval asked for is of a plan, which wants room to show its diagram and steps.</summary>
    [ObservableProperty] private bool _asksAboutPlan;

    /// <summary>How much of the model's context the conversation fills, as its last step said; null until one has.</summary>
    [ObservableProperty] private ContextUse? _contextUse;

    /// <summary>Whether the answer is in the bar.</summary>
    [ObservableProperty] private bool _isOpen;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Line))] private BarReplyStage _stage = BarReplyStage.Message;

    /// <summary>What the bar's one line says: what the assistant is doing, the approval it asks for, or its reply as plain text.</summary>
    public string Line => Stage switch
    {
        BarReplyStage.Running  => Progress.EndsWith('…') ? Progress : Progress + "…",
        BarReplyStage.Approval => Question,
        _                      => MarkdownProse.RunTogether(Text),
    };

    /// <summary>Files the agent's tools touched this run; kept with the conversation when the next request starts.</summary>
    public IReadOnlyList<string> AgentContextPaths { get; set; } = [];

    /// <summary>Raised when a request starts: the last answer's details are put away.</summary>
    public event EventHandler? Started;

    /// <summary>How a request typed into the bar runs: continuing the bar's conversation, its reply written for the line.</summary>
    public AgentRunOptions Run() => Conversation.Run(Style, OnUi);

    // ── IAIResponseHandler ────────────────────────────────────────────────

    public void BeginResponse(string userText) => OnUi(() =>
    {
        Conversation.Touched(AgentContextPaths);
        Started?.Invoke(this, EventArgs.Empty);
        TakeName();
        Request           = userText;
        Text              = string.Empty;
        _originTab        = _host.ActiveTab;
        AgentContextPaths = [];
        Progress          = Str.Get("Shell.AiResponse.Considering");
        Stage             = BarReplyStage.Running;
        IsOpen            = true;
    });

    public void ReportProgress(string message) => OnUi(() =>
    {
        Progress = message;
        Stage    = BarReplyStage.Running;
        IsOpen   = true;
    });

    public void ShowReplyDraft(string draftMarkdown) => OnUi(() =>
    {
        Text   = draftMarkdown;
        Stage  = BarReplyStage.Writing;
        IsOpen = true;
    });

    /// <summary>One answer holds one text, so a step that goes on to tools hands it back to the working line either way; a draft
    /// that is not kept is cleared, so it cannot surface again as a later stage's body.</summary>
    public void EndReplyDraft(bool keep) => OnUi(() =>
    {
        if (!keep) Text = string.Empty;
        Stage = BarReplyStage.Running;
    });

    public Task<bool> RequestToolBatchApprovalAsync(string explanationMarkdown, IReadOnlyList<ToolCall> batch, CancellationToken ct)
    {
        if (!_ui.IsCurrent) return _ui.Run(() => RequestToolBatchApprovalAsync(explanationMarkdown, batch, ct));

        TakeName();
        Text          = string.IsNullOrWhiteSpace(explanationMarkdown) ? $"{AssistantName} would like to run the following:" : explanationMarkdown;
        Question      = QuestionFor(batch);
        AsksAboutPlan = false;
        return WaitForAnswer(ct);
    }

    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
    {
        if (!_ui.IsCurrent) return _ui.Run(() => RequestPlanApprovalAsync(plan, ct));

        TakeName();
        Text          = PlanMarkdown(plan);
        Question      = Str.Format("Shell.AiResponse.ApprovePlanFormat", plan.Title);
        AsksAboutPlan = true;
        return WaitForAnswer(ct);
    }

    public Task<bool> RequestContextReleaseAsync(IReadOnlyList<string> destinations, CancellationToken ct)
    {
        if (!_ui.IsCurrent) return _ui.Run(() => RequestContextReleaseAsync(destinations, ct));

        TakeName();
        var to = string.Join(", ", destinations);
        Text          = Str.Format("Shell.AiResponse.ContextRelease.Explanation", to);
        Question      = Str.Format("Shell.AiResponse.ContextRelease.Question", to);
        AsksAboutPlan = false;
        return WaitForAnswer(ct);
    }

    /// <summary>The reply is shown, and is the newest exchange of the bar's conversation.</summary>
    public void ShowFinal(string finalMarkdown) => OnUi(() =>
    {
        Text   = finalMarkdown;
        Stage  = BarReplyStage.Message;
        IsOpen = true;
        Conversation.Answered(Request, finalMarkdown);
    });

    public void ReportContextUse(int tokens, int window) => OnUi(() => ContextUse = new(tokens, window));

    /// <summary>A request that ends without a reply — stopped, failed, or answered in the input box — leaves the bar's
    /// conversation as it was: its last reply back in the line, or the answer put away when it has none.</summary>
    public void Abort() => OnUi(() =>
    {
        if (Conversation.Exchanges.Count == 0) { PutAway(); return; }
        (Request, Text) = Conversation.Exchanges[^1];
        Stage = BarReplyStage.Message;
    });

    // ── What the user does ────────────────────────────────────────────────

    /// <summary>Says yes to the approval waited on.</summary>
    public void Approve()
    {
        Progress = Str.Get("Shell.AiResponse.Working");
        Stage    = BarReplyStage.Running;
        Answer(true);
    }

    /// <summary>Says no to the approval waited on.</summary>
    public void Deny()
    {
        Progress = Str.Format("Shell.AiResponse.DeclinedFormat", AssistantName);
        Stage    = BarReplyStage.Running;
        Answer(false);
    }

    /// <summary>Puts the answer away: an approval waited on is a no, and the bar's conversation ends.</summary>
    public void Dismiss()
    {
        Answer(false);
        PutAway();
    }

    /// <summary>
    /// Continues the bar's conversation as a conversation page of its own: its exchanges saved as one, opened in a tab, with the
    /// tab the user was on when it was asked added to it as context, and the files its runs touched attached.
    /// </summary>
    public async Task ContinueAsConversationAsync()
    {
        IReadOnlyList<(string Request, string Reply)> exchanges =
            Conversation.Exchanges.Count > 0 ? [.. Conversation.Exchanges]
            : string.IsNullOrEmpty(Request) ? [] : [(Request, Text)];
        string[] paths = [.. Conversation.Paths.Union(AgentContextPaths)];
        var runtime = _host.CurrentRuntime;
        PutAway();

        if (exchanges.Count == 0) return;

        var record = new ConversationRecord
        {
            Id        = Guid.NewGuid().ToString(),
            StartedAt = DateTime.Now,
            Messages  =
            [
                .. exchanges.SelectMany(e => new[]
                {
                    new ConversationMessage { Text = e.Request, IsUser = true,  Timestamp = DateTime.Now },
                    new ConversationMessage { Text = e.Reply,   IsUser = false, Timestamp = DateTime.Now },
                })
            ],
            Attachments = [.. paths]
        };
        record.DeriveTitle();

        try { await runtime.AiService!.SaveConversationAsync(record); }
        catch { /* not saved: the tab still opens on what it holds */ }

        runtime.ShellServices.OpenTab("Conversation", new Dictionary<string, string> { ["conversationId"] = record.Id });

        if (_originTab is not null && _host.ActivePageViewModel is IContextItemReceiver receiver)
            receiver.AddContextItem(_originTab);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>The answer leaves the bar, and the bar's conversation ends with it, the models that kept it letting it go.</summary>
    private void PutAway()
    {
        IsOpen = false;
        var ended = Conversation.End();
        _ = _host.CurrentRuntime?.AiService?.ForgetConversationAsync(ended);
        ContextUse = null;   // the next request starts a conversation of its own
    }

    private Task<bool> WaitForAnswer(CancellationToken ct)
    {
        Stage  = BarReplyStage.Approval;
        IsOpen = true;

        var asked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _approval = asked;
        ct.Register(() => asked.TrySetResult(false));
        return asked.Task;
    }

    private void Answer(bool yes)
    {
        var asked = _approval;
        _approval = null;
        asked?.TrySetResult(yes);
    }

    private void TakeName()
    {
        var persona = _host.CurrentRuntime?.Workspace?.Persona;
        AssistantName = string.IsNullOrWhiteSpace(persona?.Name) ? DefaultName : persona!.Name!.Trim();
    }

    /// <summary>The question an approval asks, in the words a voice conversation says the actions: "Draw a high-quality
    /// picture?"; a tool that does not say how it is announced is named by its name.</summary>
    private static string QuestionFor(IReadOnlyList<ToolCall> batch)
    {
        var calls = AnnouncedActions.Of(batch).Calls;
        var asked = string.Join(Str.Get("Shell.Voice.And"), calls.Select(c => $"{c.Action} {c.Target}"));
        return Str.Format("Shell.AiResponse.ApproveFormat",
                          asked.Length == 0 ? asked : char.ToUpper(asked[0], CultureInfo.CurrentCulture) + asked[1..]);
    }

    private static string PlanMarkdown(ClientPlan plan)
    {
        var sb = new StringBuilder();
        sb.Append("### ").Append(plan.Title).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(plan.Mermaid))
            sb.Append("```mermaid\n").Append(plan.Mermaid!.Trim()).Append("\n```\n\n");
        if (plan.Steps.Count > 0)
        {
            sb.Append("**Steps**\n\n");
            var n = 1;
            foreach (var s in plan.Steps)
            {
                sb.Append(n++).Append(". ").Append(s.Title);
                if (s.IsDecisionPoint)                  sb.Append("  _(decision)_");
                else if (!string.IsNullOrEmpty(s.Tool)) sb.Append("  `").Append(s.Tool).Append('`');
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    private void OnUi(Action action)
    {
        if (_ui.IsCurrent) action();
        else _ui.Run(action);
    }
}
