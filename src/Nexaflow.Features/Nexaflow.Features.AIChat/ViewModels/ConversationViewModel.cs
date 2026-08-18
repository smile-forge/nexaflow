using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
// Aliased, not imported: System.Windows.Controls also has a `Page`, which would collide with ours.
using UserControl = System.Windows.Controls.UserControl;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.AIChat.ViewModels.Timeline;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.AIChat.ViewModels;

/// <summary>
/// View-model for a single conversation page. Owns the conversation record,
/// the context items dragged onto the banner, file attachments, and the
/// estimated-token / context-window footer values.
/// </summary>
public partial class ConversationViewModel : ObservableObject, IPageViewModel, IAIResponseHandler, IContextItemReceiver
{
    private readonly IAIService    _aiService;
    private readonly IShellServices _shell;
    private readonly AiChatConfig  _config;
    private readonly Page          _ownerPage;

    /// <summary>True once an exchange has been appended in this open session — gates analysis on close.</summary>
    private bool _contentAddedThisSession;

    /// <summary>Suppresses context/attachment persistence while we repopulate from a loaded record.</summary>
    private bool _restoring;

    /// <summary>Detached pages this conversation created (via the context-area menu) and must close
    /// when they leave the context area or the conversation tab closes. Dragged tabs are NOT here —
    /// the tab strip owns those.</summary>
    private readonly HashSet<Page> _ownedContextPages = [];

    /// <summary>Prior analysis loaded on resume; when set, history before <see cref="_analyzedMessageCount"/> is summarized rather than replayed.</summary>
    private ConversationAnalysis? _resumeAnalysis;
    private int _analyzedMessageCount;

    /// <summary>Message-count cap used as history fallback when the model's context window is unknown.</summary>
    private const int FallbackHistoryMessages = 12;

    [ObservableProperty] private ConversationRecord? _conversation;
    [ObservableProperty] private string _title = "Conversation";
    [ObservableProperty] private int   _estimatedTokens;
    [ObservableProperty] private int?  _contextWindow;

    public ObservableCollection<ConversationMessage>  Messages     { get; } = [];
    public ObservableCollection<ContextItemViewModel> ContextItems { get; } = [];
    public ObservableCollection<string>               Attachments  { get; } = [];

    /// <summary>Drives the "drag tabs or files here" empty-state — the count binding went through a bool
    /// converter (always Visible), so the hint never cleared once an item was pinned.</summary>
    public bool HasContextItems => ContextItems.Count > 0;

    // ── Collapsed banner ──────────────────────────────────────────────────

    /// <summary>How many entries the collapsed summary names before it starts counting.</summary>
    private const int CollapsedSummaryLimit = 3;

    /// <summary>Collapses the banner to a single summary row. Chrome state, deliberately not persisted:
    /// a conversation reopens showing what the model can see.</summary>
    [ObservableProperty] private bool _isContextCollapsed;

    [RelayCommand]
    private void ToggleContextCollapsed() => IsContextCollapsed = !IsContextCollapsed;

    /// <summary>The first few pinned pages and attachments, identity only — the collapsed row is a
    /// reminder of what's pinned, not a place to unpin it.</summary>
    public IReadOnlyList<ContextSummaryEntry> CollapsedContext
        => [.. ContextSummary().Take(CollapsedSummaryLimit)];

    /// <summary>"and 2 more" once the summary is capped, so a truncated row never reads as the whole list.</summary>
    public string CollapsedContextOverflow
        => HasCollapsedOverflow ? $"and {ContextCount - CollapsedSummaryLimit} more" : string.Empty;

    public bool HasCollapsedOverflow => ContextCount > CollapsedSummaryLimit;
    public bool HasNoContext         => ContextCount == 0;

    private int ContextCount => ContextItems.Count + Attachments.Count;

    private IEnumerable<ContextSummaryEntry> ContextSummary()
        => ContextItems.Select(c => new ContextSummaryEntry(
                    c.Page.Icon ?? string.Empty, c.Page.Title, c.Page.SecurityRisk))
            // An attachment is a file the user chose by name — there is no scope behind it to rate.
            .Concat(Attachments.Select(a => new ContextSummaryEntry(
                    "📎", Path.GetFileName(a), ContextSecurityRisk.Low)));

    private void NotifyCollapsedSummary()
    {
        OnPropertyChanged(nameof(CollapsedContext));
        OnPropertyChanged(nameof(CollapsedContextOverflow));
        OnPropertyChanged(nameof(HasCollapsedOverflow));
        OnPropertyChanged(nameof(HasNoContext));
    }

    /// <summary>The chip whose preview is open, or null when the panel is collapsed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreviewOpen))]
    [NotifyPropertyChangedFor(nameof(PreviewTitle))]
    private ContextItemViewModel? _selectedContextItem;

    /// <summary>The selected page's own read-only view (<see cref="IContextPreview"/>), or null when it
    /// offers none — in which case the panel falls back to an identity placeholder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewContent))]
    private UserControl? _previewContent;

    public bool   IsPreviewOpen     => SelectedContextItem is not null;
    public bool   HasPreviewContent => PreviewContent is not null;
    public string PreviewTitle      => SelectedContextItem?.Page.Title ?? string.Empty;

    /// <summary>The context text this page contributes to the prompt — shown in the placeholder so an
    /// un-previewable page still tells you what the AI actually sees of it.</summary>
    public string PreviewFallbackText =>
        SelectedContextItem is { } item ? GetContextFor(item.Page) : string.Empty;

    /// <summary>The rendered thread: user/assistant bubbles plus live agent activity (tool batches,
    /// approval prompts, the "considering/running" line). Tool/activity items are session-only — only
    /// <see cref="Messages"/> is persisted.</summary>
    public ObservableCollection<object> Timeline { get; } = [];

    /// <summary>True while an agent turn is in flight.</summary>
    [ObservableProperty] private bool _isAgentRunning;

    /// <summary>Messages the user submitted while the current turn was generating, awaiting delivery to
    /// the model at the next turn boundary. Shown as faint "queued" chips.</summary>
    public ObservableCollection<string> PendingInterjections { get; } = [];

    // Current in-flight turn: the user messages (original + delivered interjections) to persist on
    // ShowFinal / revert on Abort, the live activity line, and every Timeline item added this turn.
    private readonly List<ConversationMessage> _pendingUserMessages = [];
    private TimelineActivity? _activity;
    private TimelineToolBatch? _currentToolBatch;   // accumulates consecutive tool steps into one entry
    private readonly List<object> _turnItems = [];

    public ConversationViewModel(IAIService aiService, IShellServices shell, AiChatConfig config, Page ownerPage)
    {
        _aiService = aiService;
        _shell     = shell;
        _config    = config;
        _ownerPage = ownerPage;

        Messages.CollectionChanged    += (_, _) => RecomputeTokens();
        ContextItems.CollectionChanged += (_, _) =>
        {
            RecomputeTokens();
            OnPropertyChanged(nameof(IsContextReady));
            OnPropertyChanged(nameof(HasContextItems));
            NotifyCollapsedSummary();
            SyncContext();
        };
        Attachments.CollectionChanged  += (_, _) =>
        {
            RecomputeTokens();
            NotifyCollapsedSummary();
            SyncAttachments();
        };

        _ownerPage.Closed += OnOwnerClosed;
    }

    /// <summary>
    /// On tab close, queue a background analysis of the conversation when analysis is enabled and
    /// content was added this session. Replaces any prior analysis (covers the full current transcript).
    /// </summary>
    private void OnOwnerClosed(object? sender, EventArgs e)
    {
        _ownerPage.Closed -= OnOwnerClosed;

        // Drop risk-badge subscriptions so pinned (possibly long-lived) page view-models don't keep
        // this conversation alive.
        UntrackAllRisk();

        // Close every detached page we created — they have no tab to own their lifecycle.
        // Must run regardless of analysis settings.
        foreach (var p in _ownedContextPages) p.RaiseClosed();
        _ownedContextPages.Clear();

        if (!_config.IsAnalysisEnabled) return;
        if (Conversation is not { Messages.Count: > 0 }) return;

        // Analyze when the transcript changed this session, OR when this conversation has no prior
        // analysis yet (so opening + closing an older, never-summarized conversation still generates one).
        if (!_contentAddedThisSession && _resumeAnalysis is not null) return;

        _shell.QueueBackgroundTask(new ConversationAnalysisTask(_aiService, Conversation));
    }

    /// <summary>Loads a persisted conversation by id and pulls the model context window.</summary>
    public async Task LoadAsync(string conversationId)
    {
        var all = await _aiService.LoadConversationsAsync();
        var rec = all.FirstOrDefault(c => c.Id == conversationId);
        if (rec is null) return;

        // Hit ids are thread positions, so loading a different conversation into this tab invalidates
        // every one of them.
        ClearSearch();

        Conversation = rec;
        Title        = rec.Title;
        Messages.Clear();
        Timeline.Clear();
        foreach (var m in rec.Messages.OrderBy(m => m.Timestamp))
        {
            Messages.Add(m);
            Timeline.Add(m.IsUser ? new TimelineUserMessage(m) : (object)new TimelineAssistantMessage(m.Text));
        }
        MarkLastUserMessage();

        // Repopulate attachments + the pinned-context panel from the saved record. Guarded so the
        // collection-changed handlers don't re-persist (and briefly write an empty state) mid-restore.
        _restoring = true;
        try
        {
            // Reactivating the tab re-runs this (IPageView.Reinitialize), so start from empty — otherwise
            // every reactivation stacks another copy of the saved context onto the strip.
            ClearContext();

            Attachments.Clear();
            foreach (var a in rec.Attachments)
                Attachments.Add(a);

            // IAIService rebuilds the page definitions; we realize them and own their lifetime.
            foreach (var page in _aiService.RestoreContextPages(rec))
            {
                page.GetOrCreateContent();   // realize the VM so GetContext/tools resolve
                _ownedContextPages.Add(page);
                AddContextItem(page);
            }
        }
        finally { _restoring = false; }

        UpdateBreadcrumb();

        // Resume context: prefer a prior analysis (only replay messages it doesn't already cover).
        try
        {
            var json = await _aiService.LoadConversationArtifactAsync(rec.Id, ConversationAnalysisTask.ArtifactName);
            if (!string.IsNullOrWhiteSpace(json))
            {
                _resumeAnalysis = JsonSerializer.Deserialize<ConversationAnalysis>(json, AnalysisJsonOpts);
                _analyzedMessageCount = _resumeAnalysis?.AnalyzedMessageCount ?? 0;
            }
        }
        catch { _resumeAnalysis = null; }

        try { ContextWindow = await _aiService.GetConversationContextWindowAsync(); }
        catch { ContextWindow = null; }

        RecomputeTokens();
    }

    private static readonly JsonSerializerOptions AnalysisJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Default title carried by a fresh record until the first exchange derives a real one.</summary>
    private const string DefaultTitle = "New conversation";

    /// <summary>
    /// Initializes a brand-new, empty conversation. The record is held in memory and only
    /// persisted on the first completed exchange, so abandoned blank conversations never clutter
    /// the browser.
    /// </summary>
    public async Task StartNew()
    {
        Conversation = new ConversationRecord();
        Title        = Conversation.Title;
        Messages.Clear();
        Attachments.Clear();
        UpdateBreadcrumb();

        try { ContextWindow = await _aiService.GetConversationContextWindowAsync(); }
        catch { ContextWindow = null; }

        RecomputeTokens();
    }

    /// <summary>
    /// Seeds and runs a first turn from an external opener (e.g. the Projects "Task to AI" / "Plan with
    /// AI" buttons open this tab with an <c>initialPrompt</c> param). Echoes the prompt as the user
    /// message and runs the agent, rendering into the timeline like any other turn — the context pinned
    /// by <see cref="LoadAsync"/> / <see cref="StartNew"/> is already in place, so the agent sees it.
    /// </summary>
    public async Task SendSeedAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || IsAgentRunning) return;

        BeginResponse(prompt);   // echo the user message + "considering" placeholder
        try
        {
            var response = await _aiService.RunAgentAsync(this, prompt, includeContext: true, this);
            if (response is null || response.Kind != AiResponseKind.Message)
                Abort();   // prefill / cancelled / no provider — drop the unfinished turn
        }
        catch { Abort(); }
    }

    // ── IAIResponseHandler (inline agent rendering) ───────────────────────
    // The agent loop drives these; everything is rendered into Timeline. Marshalled to the UI thread.

    /// <summary>A new turn: echo the user's message and show a "considering" placeholder.</summary>
    public void BeginResponse(string userText) => OnUi(() =>
    {
        _turnItems.Clear();
        _pendingUserMessages.Clear();
        _currentToolBatch = null;

        var user = new ConversationMessage { Text = userText, IsUser = true, Timestamp = DateTime.Now };
        _pendingUserMessages.Add(user);
        Messages.Add(user);                                      // token counting

        // Before MarkLastUserMessage: it suppresses the rewind affordance while a turn is in flight.
        IsAgentRunning = true;
        AddTurnItem(new TimelineUserMessage(user));
        MarkLastUserMessage();
        _activity = new TimelineActivity { Text = "Considering" };
        AddTurnItem(_activity);
    });

    /// <summary>Queue a message submitted mid-response; delivered to the model at the next turn boundary.</summary>
    public void Enqueue(string text) => OnUi(() =>
    {
        if (!string.IsNullOrWhiteSpace(text)) PendingInterjections.Add(text);
    });

    /// <summary>Drain queued interjections, committing them into the current turn (bubbles + persistence),
    /// and return their text for the loop to feed the model.</summary>
    public IReadOnlyList<string> TakeInterjections()
    {
        IReadOnlyList<string> taken = [];
        OnUi(() =>
        {
            if (PendingInterjections.Count == 0) return;
            var texts = PendingInterjections.ToList();
            PendingInterjections.Clear();
            foreach (var t in texts)
            {
                var msg = new ConversationMessage { Text = t, IsUser = true, Timestamp = DateTime.Now };
                _pendingUserMessages.Add(msg);
                Messages.Add(msg);
                var bubble = new TimelineUserMessage(msg);
                InsertBeforeActivity(bubble);
                _turnItems.Add(bubble);
            }
            MarkLastUserMessage();
            _currentToolBatch = null;   // tools after an interjection form a new group below it
            taken = texts;
        });
        return taken;
    }

    /// <summary>Drain interjections still queued after the run ended (cleared, not committed) — the
    /// shell re-runs them as a fresh turn.</summary>
    public IReadOnlyList<string> DrainQueue()
    {
        IReadOnlyList<string> taken = [];
        OnUi(() =>
        {
            if (PendingInterjections.Count == 0) return;
            taken = PendingInterjections.ToList();
            PendingInterjections.Clear();
        });
        return taken;
    }

    public void ReportProgress(string message) => OnUi(() =>
    {
        if (_activity is not null) _activity.Text = message;
    });

    public void OnToolBatchStarting(IReadOnlyList<ToolCall> batch) { }

    /// <summary>Fold a completed batch into this turn's single collapsed "Ran N tools" entry (created on
    /// the first batch, reused for later steps), above the live line.</summary>
    public void OnToolBatchFinished(IReadOnlyList<ToolRunResult> results) => OnUi(() =>
    {
        if (_currentToolBatch is null)
        {
            _currentToolBatch = new TimelineToolBatch();
            InsertBeforeActivity(_currentToolBatch);
            _turnItems.Add(_currentToolBatch);
        }

        foreach (var r in results)
            _currentToolBatch.Tools.Add(new TimelineToolRun(r.Tool, r.Result.IsError, r.Result.Summary));

        if (_activity is not null) _activity.Text = "Considering";   // back to thinking
    });

    // The model's prose is the *explanation* and the tool list is the *question* — passing the prose as both
    // (as this used to) meant an approval prompt could only ever show the bare "Run x?" and the reason was
    // dropped on the floor. An empty explanation now simply collapses in the template.
    public Task<bool> RequestToolBatchApprovalAsync(string explanationMarkdown, IReadOnlyList<ToolCall> batch, CancellationToken ct)
        => RequestApproval(explanationMarkdown, BuildBatchSummary(batch), ct);

    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
        => RequestApproval(plan.Title, $"Approve plan: {plan.Title}?", ct);

    private Task<bool> RequestApproval(string explanation, string summary, CancellationToken ct)
        => _shell.RunOnUiAsync(() =>
        {
            var item = new TimelineApproval(explanation, summary);
            InsertBeforeActivity(item);
            _turnItems.Add(item);
            ct.Register(item.Cancel);
            return item.Decision;
        });

    /// <summary>
    /// Render the final answer and persist the exchange (original prompt + any interjections).
    /// <para>
    /// A model can end a turn saying nothing — most often after a denied or exhausted tool batch. Rendering
    /// that verbatim produced an empty bubble and wrote an empty message to disk, so the conversation
    /// carried a permanent blank. An empty answer is now reported as what it is (a session-only notice) and
    /// only the user's message is kept.
    /// </para>
    /// </summary>
    public void ShowFinal(string finalMarkdown) => OnUi(async () =>
    {
        RemoveActivity();

        var empty = string.IsNullOrWhiteSpace(finalMarkdown);
        ConversationMessage? assistant = null;

        if (empty)
        {
            AddTurnItem(new TimelineNotice("The assistant ended the turn without a reply."));
        }
        else
        {
            AddTurnItem(new TimelineAssistantMessage(finalMarkdown));
            assistant = new ConversationMessage { Text = finalMarkdown, IsUser = false, Timestamp = DateTime.Now };
            Messages.Add(assistant);
        }

        IsAgentRunning = false;

        var users = _pendingUserMessages.ToList();
        _turnItems.Clear();
        _pendingUserMessages.Clear();
        _currentToolBatch = null;
        if (users.Count > 0) await PersistExchangeAsync(users, assistant);
    });

    /// <summary>Cancel / error / prefill: drop the unfinished turn's items (it isn't persisted).</summary>
    public void Abort() => OnUi(() =>
    {
        RemoveActivity();
        foreach (var item in _turnItems) Timeline.Remove(item);
        foreach (var u in _pendingUserMessages) Messages.Remove(u);
        _turnItems.Clear();
        _pendingUserMessages.Clear();
        PendingInterjections.Clear();
        _currentToolBatch = null;
        IsAgentRunning = false;
        MarkLastUserMessage();
    });

    /// <param name="assistant">Null when the model said nothing — the user's message is still kept, because
    /// it happened; inventing an assistant message to pair with it would not be true.</param>
    private async Task PersistExchangeAsync(IReadOnlyList<ConversationMessage> users, ConversationMessage? assistant)
    {
        if (Conversation is null) return;

        foreach (var u in users) Conversation.Messages.Add(u);
        if (assistant is not null) Conversation.Messages.Add(assistant);
        Conversation.Attachments = [.. Attachments];
        _contentAddedThisSession = true;
        MarkLastUserMessage();

        // A fresh conversation keeps the placeholder title until its first user message names it.
        if (string.Equals(Conversation.Title, DefaultTitle, StringComparison.Ordinal))
        {
            Conversation.DeriveTitle();
            Title = Conversation.Title;
            UpdateBreadcrumb();
        }

        try { await _aiService.SaveConversationAsync(Conversation); }
        catch { /* persistence failures shouldn't kill the UI */ }
    }

    // ── Timestamps + rewind ───────────────────────────────────────────────

    /// <summary>Rewind is offered on the newest user message only, so exactly one bubble carries the button.
    /// Also re-reads every relative timestamp ("5 minutes ago" ages), which is cheap and only needs to be
    /// right when the thread actually changes.</summary>
    private void MarkLastUserMessage()
    {
        TimelineUserMessage? last = null;
        foreach (var item in Timeline)
        {
            if (item is not TimelineUserMessage user) continue;
            user.IsLast = false;
            user.RefreshTimestamp();
            last = user;
        }
        if (last is not null && !IsAgentRunning) last.IsLast = true;
    }

    /// <summary>
    /// Rewinds the conversation to <paramref name="target"/>: everything from that message onward is
    /// dropped — from the timeline, the token count and the saved record — and its text is handed back to
    /// the AI input box so it can be edited and re-sent.
    /// <para>
    /// Disabled while a turn is in flight: a running agent owns <see cref="Messages"/> through
    /// <c>_pendingUserMessages</c>, and truncating underneath it would leave the turn writing into a
    /// conversation that no longer exists.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task RewindTo(TimelineUserMessage? target)
    {
        if (target is null || IsAgentRunning || Conversation is null) return;

        var cut = Messages.IndexOf(target.Message);
        if (cut < 0) return;

        for (int i = Messages.Count - 1; i >= cut; i--) Messages.RemoveAt(i);

        var recordCut = Conversation.Messages.IndexOf(target.Message);
        if (recordCut >= 0)
            Conversation.Messages.RemoveRange(recordCut, Conversation.Messages.Count - recordCut);

        // The resume analysis indexes into the message list by count, so a truncation that lands before it
        // would make it summarize messages that no longer exist.
        if (_analyzedMessageCount > Conversation.Messages.Count)
        {
            _analyzedMessageCount = 0;
            _resumeAnalysis       = null;
        }

        RebuildTimeline();
        _contentAddedThisSession = true;

        _shell.InsertChatInput(target.Text);

        try { await _aiService.SaveConversationAsync(Conversation); }
        catch { /* the in-memory rewind already happened; a failed write shouldn't undo it */ }
    }

    /// <summary>Re-renders the thread from <see cref="Messages"/>. Live agent items (tool batches, the
    /// activity line, approvals) are session-only and there is no turn in flight when this runs.</summary>
    private void RebuildTimeline()
    {
        // Rewinding renumbers the thread, so the search's positions would land on bubbles that were
        // never matched.
        ClearSearch();

        Timeline.Clear();
        foreach (var m in Messages)
            Timeline.Add(m.IsUser ? new TimelineUserMessage(m) : (object)new TimelineAssistantMessage(m.Text));
        MarkLastUserMessage();
        RecomputeTokens();
    }

    private void AddTurnItem(object item) { Timeline.Add(item); _turnItems.Add(item); }

    private void InsertBeforeActivity(object item)
    {
        var idx = _activity is not null ? Timeline.IndexOf(_activity) : -1;
        if (idx >= 0) Timeline.Insert(idx, item);
        else          Timeline.Add(item);
    }

    private void RemoveActivity()
    {
        if (_activity is not null) { Timeline.Remove(_activity); _turnItems.Remove(_activity); _activity = null; }
    }

    private static string BuildBatchSummary(IReadOnlyList<ToolCall> batch)
    {
        if (batch.Count == 1) return $"Run {batch[0].Tool}?";
        var grouped = batch.GroupBy(c => c.Tool).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key);
        return "Run " + string.Join(", ", grouped) + "?";
    }

    // Run UI work on the workspace UI thread (inline if already there) via the shell — no dispatcher here.
    private void OnUi(Action action) => _ = _shell.RunOnUiAsync(action);

    /// <summary>
    /// Mirrors the pinned context into the record and persists it (so it repopulates on reopen).
    /// Skipped while restoring; a brand-new chat is left unsaved until its first exchange to avoid
    /// cluttering the browser with empty conversations.
    /// </summary>
    private void SyncContext()
    {
        if (_restoring || Conversation is null) return;

        _aiService.SetConversationContext(Conversation, ContextItems.Select(c => c.Page));

        if (Conversation.Messages.Count > 0)
            _ = _aiService.SaveConversationAsync(Conversation);
    }

    /// <summary>
    /// Mirrors dropped-file attachments into the record and persists them, so files pinned as context
    /// survive close/reopen even without a following message exchange. Skipped while restoring and for
    /// not-yet-persisted (empty) conversations (those save on their first exchange).
    /// </summary>
    private void SyncAttachments()
    {
        if (_restoring || Conversation is null) return;

        Conversation.Attachments = [.. Attachments];

        if (Conversation.Messages.Count > 0)
            _ = _aiService.SaveConversationAsync(Conversation);
    }

    /// <summary>How long a duplicate-add pulse stays lit.</summary>
    private const int FlashMs = 900;

    /// <summary>
    /// Adds a page-as-context chip. A page that's already pinned isn't added twice — instead the existing
    /// chip pulses, so a repeated drag reads as "it's already there" rather than as nothing happening.
    /// </summary>
    public void AddContextItem(Page page)
    {
        if (page is null || ReferenceEquals(page, _ownerPage)) return;

        if (FindExisting(page) is { } existing)
        {
            Flash(existing);
            return;
        }

        ContextItems.Add(new ContextItemViewModel(page));
        TrackRisk(page);
    }

    /// <summary>
    /// Identity, not reference. Restoring a saved conversation rebuilds its context as <em>fresh</em>
    /// <see cref="Page"/> objects, and the same folder can be open as two different tab objects — in both
    /// cases reference equality says "new" when the user means "same thing". Two pages are the same context
    /// when they'd be restored from the same <c>ContextRef</c>: identical page kind and parameters.
    /// </summary>
    private ContextItemViewModel? FindExisting(Page page)
        => ContextItems.FirstOrDefault(c => ReferenceEquals(c.Page, page) || SameTarget(c.Page, page));

    private static bool SameTarget(Page a, Page b)
        => !string.IsNullOrEmpty(a.PageKind)
           && string.Equals(a.PageKind, b.PageKind, StringComparison.Ordinal)
           && SameParams(a.PageParams, b.PageParams);

    private static bool SameParams(Dictionary<string, string>? a, Dictionary<string, string>? b)
    {
        var left  = a ?? [];
        var right = b ?? [];
        if (left.Count != right.Count) return false;
        foreach (var (key, value) in left)
            if (!right.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal))
                return false;
        return true;
    }

    private void Flash(ContextItemViewModel item) => _ = FlashAsync(item);

    private async Task FlashAsync(ContextItemViewModel item)
    {
        await _shell.RunOnUiAsync(() => item.IsFlashing = true);
        await Task.Delay(FlashMs);
        await _shell.RunOnUiAsync(() => item.IsFlashing = false);
    }

    public void RemoveContextItem(Page page)
    {
        if (FindExisting(page) is { } item) RemoveContextItem(item);
    }

    private void RemoveContextItem(ContextItemViewModel item)
    {
        if (ReferenceEquals(item, SelectedContextItem)) SelectedContextItem = null;

        ContextItems.Remove(item);
        UntrackRisk(item.Page);

        // A detached page we created has no tab — close it when it leaves the context area.
        if (_ownedContextPages.Remove(item.Page))
            item.Page.RaiseClosed();
    }

    /// <summary>
    /// Empties the strip: drops risk subscriptions and closes every page we own (a restored or
    /// menu-added page has no tab to own its lifetime). Called before a (re)load — without it a second
    /// <c>Reinitialize</c> restores the saved context <em>again</em>, as new Page objects, and the strip
    /// grows a duplicate set every time the tab is reactivated.
    /// </summary>
    private void ClearContext()
    {
        SelectedContextItem = null;

        foreach (var item in ContextItems) UntrackRisk(item.Page);
        foreach (var page in _ownedContextPages) page.RaiseClosed();

        _ownedContextPages.Clear();
        ContextItems.Clear();
    }

    // ── Selection + preview panel ─────────────────────────────────────────

    /// <summary>Clicking a chip selects it (and opens the preview); clicking the selected one closes it.</summary>
    [RelayCommand]
    private void SelectContextItem(ContextItemViewModel? item)
        => SelectedContextItem = ReferenceEquals(item, SelectedContextItem) ? null : item;

    /// <summary>The panel's own close button — deselects, which collapses it.</summary>
    [RelayCommand]
    private void ClosePreview() => SelectedContextItem = null;

    partial void OnSelectedContextItemChanged(ContextItemViewModel? oldValue, ContextItemViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;

        // Built fresh per selection and dropped on deselect — a page's live tab content is already parented
        // in the tab host and cannot be re-hosted here, so IContextPreview hands back a separate element.
        PreviewContent = newValue is null ? null : BuildPreview(newValue.Page);
        OnPropertyChanged(nameof(PreviewFallbackText));
    }

    private static UserControl? BuildPreview(Page page)
    {
        if ((page.Content as IPageView)?.ViewModel is not IContextPreview previewable) return null;
        try   { return previewable.CreateContextPreview(); }
        catch { return null; }   // a broken preview must not take the conversation down with it
    }

    // ── Live security-risk badge + context readiness ──────────────────────
    // Keep each pinned page's SecurityRisk mirrored from its view-model and refreshed whenever that
    // view-model changes (e.g. a file-system tab navigates to a riskier folder), so the chip badge is
    // always truthful. The same subscription drives IsContextReady, so a pinned page finishing its
    // background gather (e.g. SystemInfo) re-evaluates the aggregate and wakes a held AI send.
    // Subscriptions are torn down on remove / conversation close to avoid leaks.

    private readonly Dictionary<Page, PropertyChangedEventHandler> _riskSubscriptions = [];

    private void TrackRisk(Page page)
    {
        // The summary pills carry the badge too, and the risk is only known once UpdateRisk has run —
        // after the collection change that first raised them. Re-raise here or a freshly pinned page
        // shows in the collapsed row unbadged.
        UpdateRisk(page);
        OnPropertyChanged(nameof(IsContextReady));
        NotifyCollapsedSummary();
        if (_riskSubscriptions.ContainsKey(page)) return;
        if ((page.Content as IPageView)?.ViewModel is not INotifyPropertyChanged source) return;

        PropertyChangedEventHandler handler = (_, _) =>
        {
            UpdateRisk(page);
            OnPropertyChanged(nameof(IsContextReady));
            NotifyCollapsedSummary();
        };
        _riskSubscriptions[page] = handler;
        source.PropertyChanged += handler;
    }

    private void UntrackRisk(Page page)
    {
        if (!_riskSubscriptions.Remove(page, out var handler)) return;
        if ((page.Content as IPageView)?.ViewModel is INotifyPropertyChanged source)
            source.PropertyChanged -= handler;
    }

    private void UntrackAllRisk()
    {
        foreach (var (page, handler) in _riskSubscriptions)
            if ((page.Content as IPageView)?.ViewModel is INotifyPropertyChanged source)
                source.PropertyChanged -= handler;
        _riskSubscriptions.Clear();
    }

    private static void UpdateRisk(Page page)
        => page.SecurityRisk = (page.Content as IPageView)?.ViewModel?.GetContextSecurityRisk() ?? ContextSecurityRisk.Low;

    [RelayCommand]
    private void RemoveContextItemCmd(ContextItemViewModel item) => RemoveContextItem(item);

    /// <summary>The workspace's open tabs that aren't this conversation and aren't pinned already — the
    /// "Open tabs" submenu of the context-area menu. Same targets drag-and-drop offers, reachable without
    /// a drag. Evaluated fresh each access, since tabs open and close while the menu is closed.</summary>
    public IReadOnlyList<Page> AvailableOpenTabs
        => [.. _shell.GetOpenTabs()
                     .Where(tab => !ReferenceEquals(tab, _ownerPage) && FindExisting(tab) is null)];

    /// <summary>Pins an already-open tab as a context item. Unlike <see cref="AddContextPage"/> this takes
    /// no ownership — the tab strip owns the page, and unpinning must not close it.</summary>
    [RelayCommand]
    private void AddOpenTab(Page page) => AddContextItem(page);

    /// <summary>Lightweight page definitions that can be created context-free and pinned here (e.g.
    /// "Projects" when enabled), for the context-area menu — which reads each page's Title/Icon.
    /// Evaluated fresh each access (reflects live enablement) and excludes already-pinned kinds.</summary>
    public IReadOnlyList<Page> AvailableContextPages
        => [.. _shell.GetContextItemPages().Where(cand => ContextItems.All(c => c.Page.PageKind != cand.PageKind))];

    /// <summary>Pins a context-free page (built by the menu) as a context item: realizes its content
    /// and takes ownership of its lifecycle (closed when removed / when this conversation closes).</summary>
    [RelayCommand]
    private void AddContextPage(Page page)
    {
        if (page is null) return;

        // Already pinned — pulse the chip rather than build a second page we'd then have to throw away.
        if (FindExisting(page) is { } existing)
        {
            Flash(existing);
            return;
        }

        page.GetOrCreateContent();   // realize VM so GetContext/GetClientTools resolve
        _ownedContextPages.Add(page);
        AddContextItem(page);
    }

    [RelayCommand]
    private void RemoveAttachmentCmd(string path) => RemoveAttachment(path);

    public void AddAttachment(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (Attachments.Contains(path)) return;
        Attachments.Add(path);
    }

    public void RemoveAttachment(string path) => Attachments.Remove(path);

    private void RecomputeTokens()
    {
        // 1.1 tokens per word, summed over messages + context-item-derived context strings.
        long words = 0;
        foreach (var m in Messages)          words += CountWords(m.Text);
        foreach (var c in ContextItems)      words += CountWords(GetContextFor(c.Page));
        foreach (var a in Attachments)       words += CountWords(a);

        EstimatedTokens = (int)Math.Min(int.MaxValue, words * 11 / 10);
    }

    private static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return Regex.Matches(text, "[\\S]+").Count;
    }

    private static string GetContextFor(Page page)
    {
        var view = page.Content as IPageView;
        if (view?.ViewModel is not IPageViewModel vm) return string.Empty;

        var baseText = vm.GetContext();

        // For FileSystem tabs, augment with a directory listing so the AI sees
        // real folder content, not just the path. The page identifies itself via
        // its structured FileSystemContext.
        if (vm.GetContextObject() is FileSystemContext fsCtx
            && !string.IsNullOrEmpty(fsCtx.CurrentPath))
        {
            var listing = BuildFolderListing(fsCtx.CurrentPath);
            if (!string.IsNullOrEmpty(listing))
                return string.IsNullOrEmpty(baseText) ? listing : $"{baseText}\n{listing}";
        }

        return baseText;
    }

    private static string BuildFolderListing(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath)) return string.Empty;
            var di = new DirectoryInfo(folderPath);
            var sb = new StringBuilder();
            sb.Append("Path: ").AppendLine(folderPath);
            sb.AppendLine("Contents:");

            foreach (var d in di.EnumerateDirectories().Take(200))
            {
                sb.Append("  [DIR]  ")
                  .Append(d.Name).Append('/')
                  .Append("  ")
                  .AppendLine(d.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
            }
            foreach (var f in di.EnumerateFiles().Take(400))
            {
                var ext = string.IsNullOrEmpty(f.Extension) ? "FILE" : f.Extension.TrimStart('.').ToUpperInvariant();
                sb.Append("  [").Append(ext).Append("]  ")
                  .Append(f.Name).Append("  ")
                  .Append(FormatSize(f.Length)).Append("  ")
                  .AppendLine(f.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
            }
            return sb.ToString();
        }
        catch { return string.Empty; }
    }

    private static string FormatSize(long bytes) => SizeFormatter.FormatBytes(bytes);

    private void UpdateBreadcrumb()
    {
        _ownerPage.Title = Title;
        _ownerPage.Breadcrumbs.Clear();
        _ownerPage.Breadcrumbs.Add(new BreadcrumbSegment
        {
            Label          = "Conversations",
            TargetPageKind = "AIChat",
        });
        _ownerPage.Breadcrumbs.Add(new BreadcrumbSegment { Label = Title });
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    /// <summary>Ready only when every pinned context item is ready — a page still gathering its data
    /// (e.g. SystemInfo's background scan) holds the whole conversation's send. Re-evaluated whenever a
    /// pinned page changes (risk subscription) or the set of items changes.</summary>
    public bool IsContextReady =>
        ContextItems.All(c => (c.Page.Content as IPageView)?.ViewModel?.IsContextReady ?? true);

    /// <summary>
    /// Combined context handed to the AI on every call: pinned context items, attachments, then
    /// conversation history. History is budgeted to 75% of the model's context window (older
    /// messages dropped once that's exceeded). When the conversation was resumed with a prior
    /// analysis, that analysis is included in place of the messages it already covers, and only
    /// later messages are replayed. With an unknown context window, the last
    /// <see cref="FallbackHistoryMessages"/> messages are used.
    /// </summary>
    public string GetContext()
    {
        var sb = new StringBuilder();

        // When ≥2 pinned pages have DISTINCT security contexts, label each block with its context
        // handle so the agent can match a listing to a tool's security_context selector.
        var multiContext = ContextItems
            .Select(c => (c.Page.Content as IPageView)?.ViewModel?.GetSecurityContext())
            .Where(s => s is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() >= 2;

        foreach (var c in ContextItems)
        {
            var page = c.Page;
            var part = GetContextFor(page);
            if (string.IsNullOrWhiteSpace(part)) continue;
            sb.AppendLine("---");
            var security = (page.Content as IPageView)?.ViewModel?.GetSecurityContext();
            sb.AppendLine(multiContext && security is not null
                ? $"[Context item: {page.Title} — security context: {NameFor(security)} (scope: {security})]"
                : $"[Context item: {page.Title}]");
            sb.AppendLine(part);
        }

        if (Attachments.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine("[Attachments]");
            foreach (var a in Attachments)
            {
                sb.AppendLine($"File: {a}");
                var content = TryReadText(a);
                if (content is not null)
                {
                    sb.AppendLine(content);
                    sb.AppendLine();
                }
            }
        }

        var hasWindow  = ContextWindow is int w && w > 0;
        long budget    = hasWindow ? (long)(ContextWindow!.Value * 0.75) : long.MaxValue;
        long remaining = budget - EstTokens(sb.ToString());

        var messages   = Conversation?.Messages ?? [];
        var startIndex = 0;

        if (_resumeAnalysis is not null)
        {
            var block = BuildAnalysisBlock(_resumeAnalysis);
            sb.AppendLine("---");
            sb.AppendLine("[Prior conversation analysis]");
            sb.AppendLine(block);
            remaining -= EstTokens(block);
            startIndex = Math.Min(_analyzedMessageCount, messages.Count);
        }

        sb.AppendLine("---");
        sb.AppendLine("[Conversation]");

        var pool = messages.Skip(startIndex).ToList();
        if (pool.Count == 0)
        {
            sb.AppendLine(_resumeAnalysis is not null ? "(no messages since analysis)" : "(empty)");
            return sb.ToString();
        }

        // Pick the most recent messages that fit the budget (always keep at least the latest).
        List<ConversationMessage> selected;
        if (hasWindow)
        {
            selected = [];
            long acc = 0;
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                var t = EstTokens((pool[i].IsUser ? "User: " : "Assistant: ") + pool[i].Text);
                if (acc + t > remaining && selected.Count > 0) break;
                acc += t;
                selected.Insert(0, pool[i]);
            }
        }
        else
        {
            selected = [.. pool.TakeLast(FallbackHistoryMessages)];
        }

        if (selected.Count < pool.Count)
            sb.AppendLine("(earlier messages omitted — context budget)");
        foreach (var m in selected)
            sb.AppendLine((m.IsUser ? "User: " : "Assistant: ") + m.Text);

        return sb.ToString();
    }

    /// <summary>Stable short handle per security-context string, shared between <see cref="GetContext"/>
    /// labelling and <see cref="GetClientTools"/> routing so the agent can correlate a context listing
    /// with the right tool security context. Persists for the VM's life; idempotent per scope string.</summary>
    private readonly Dictionary<string, string> _securityContextNames = new(StringComparer.OrdinalIgnoreCase);

    private string NameFor(string securityContext)
    {
        if (!_securityContextNames.TryGetValue(securityContext, out var name))
            _securityContextNames[securityContext] = name = $"context-{_securityContextNames.Count + 1}";
        return name;
    }

    /// <summary>
    /// The conversation is a meta of its context-item tabs: it exposes the union of every pinned page's
    /// client tools to the agent, so the AI can act on those tabs (read files, query JSON, …) — not just
    /// read their text context. When the same tool name appears in two or more pages with DISTINCT
    /// security contexts (e.g. two file-system tabs rooted at different folders), it is exposed once as a
    /// <see cref="MultiContextClientTool"/> with a <c>security_context</c> selector that routes the call
    /// to the right page — so the agent can target either, and never act in the wrong scope. Tools that
    /// live in a single context (or context-less pages) are first-wins as before.
    /// </summary>
    public IReadOnlyList<IClientTool> GetClientTools()
    {
        // Every tool across pinned pages, tagged with its page's security context (null = none).
        var entries = new List<(string? Security, IClientTool Tool)>();
        foreach (var c in ContextItems)
        {
            if ((c.Page.Content as IPageView)?.ViewModel is not IPageViewModel vm) continue;
            var security = vm.GetSecurityContext();
            foreach (var t in vm.GetClientTools())
                entries.Add((security, t));
        }

        var result = new List<IClientTool>();
        foreach (var group in entries.GroupBy(e => e.Tool.Name, StringComparer.OrdinalIgnoreCase))
        {
            // One bound tool per distinct named security context within this tool name.
            var byContextName = new Dictionary<string, IClientTool>(StringComparer.Ordinal);
            var scopeByName   = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (security, tool) in group)
            {
                if (security is null) continue;
                var name = NameFor(security);
                if (byContextName.TryAdd(name, tool)) scopeByName[name] = security;
            }

            result.Add(byContextName.Count >= 2
                ? new MultiContextClientTool(group.First().Tool, byContextName, scopeByName)
                : group.First().Tool);
        }

        return result;
    }

    /// <summary>Estimated tokens for a string, using the same 1.1-tokens-per-word heuristic as the footer.</summary>
    private static long EstTokens(string? text) => CountWords(text) * 11L / 10L;

    /// <summary>Renders a prior analysis as a compact context block.</summary>
    private static string BuildAnalysisBlock(ConversationAnalysis a)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(a.Summary))              sb.Append("Summary: ").AppendLine(a.Summary);
        if (!string.IsNullOrWhiteSpace(a.Topic))               sb.Append("Topic: ").AppendLine(a.Topic);
        if (!string.IsNullOrWhiteSpace(a.Tone))                sb.Append("Tone: ").AppendLine(a.Tone);
        if (!string.IsNullOrWhiteSpace(a.ToneEvolution))       sb.Append("Tone evolution: ").AppendLine(a.ToneEvolution);
        if (!string.IsNullOrWhiteSpace(a.UnderstandingOfUser)) sb.Append("Understanding of user: ").AppendLine(a.UnderstandingOfUser);
        if (!string.IsNullOrWhiteSpace(a.UnderstandingOfAgent))sb.Append("Understanding of agent: ").AppendLine(a.UnderstandingOfAgent);
        if (a.KeyDecisionPoints.Count > 0)   sb.Append("Key decisions: ").AppendLine(string.Join("; ", a.KeyDecisionPoints));
        if (a.ImportantFacts.Count > 0)      sb.Append("Important facts: ").AppendLine(string.Join("; ", a.ImportantFacts));
        if (a.ValuableAttachments.Count > 0) sb.Append("Valuable attachments: ").AppendLine(string.Join("; ", a.ValuableAttachments));
        return sb.ToString();
    }

    /// <summary>
    /// Reads an attached file's text (capped, binary-guarded) for inclusion in context, so the AI
    /// can actually see the file's content. Returns null when missing, binary, too unwieldy, or unreadable.
    /// </summary>
    private static string? TryReadText(string path, int capBytes = 32 * 1024)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var take  = (int)Math.Min(new FileInfo(path).Length, capBytes);
            var bytes = new byte[take];
            using (var fs = File.OpenRead(path))
                fs.ReadExactly(bytes, 0, take);
            if (Array.IndexOf(bytes, (byte)0) >= 0) return null;   // looks binary
            var text = Encoding.UTF8.GetString(bytes);
            return new FileInfo(path).Length > capBytes
                ? text + $"\n…(truncated — first {capBytes / 1024} KB)"
                : text;
        }
        catch { return null; }
    }
}
