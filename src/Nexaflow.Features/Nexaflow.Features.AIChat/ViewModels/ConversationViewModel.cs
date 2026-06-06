using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.AIChat.ViewModels.Timeline;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;

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

    public ObservableCollection<ConversationMessage> Messages    { get; } = [];
    public ObservableCollection<Page>                ContextItems { get; } = [];
    public ObservableCollection<string>              Attachments  { get; } = [];

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
        ContextItems.CollectionChanged += (_, _) => { RecomputeTokens(); OnPropertyChanged(nameof(IsContextReady)); SyncContext(); };
        Attachments.CollectionChanged  += (_, _) => { RecomputeTokens(); SyncAttachments(); };

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

        Conversation = rec;
        Title        = rec.Title;
        Messages.Clear();
        Timeline.Clear();
        foreach (var m in rec.Messages.OrderBy(m => m.Timestamp))
        {
            Messages.Add(m);
            Timeline.Add(m.IsUser ? new TimelineUserMessage(m.Text) : (object)new TimelineAssistantMessage(m.Text));
        }

        // Repopulate attachments + the pinned-context panel from the saved record. Guarded so the
        // collection-changed handlers don't re-persist (and briefly write an empty state) mid-restore.
        _restoring = true;
        try
        {
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

        AddTurnItem(new TimelineUserMessage(userText));
        _activity = new TimelineActivity { Text = "Considering" };
        AddTurnItem(_activity);
        IsAgentRunning = true;
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
                var bubble = new TimelineUserMessage(t);
                InsertBeforeActivity(bubble);
                _turnItems.Add(bubble);
            }
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

    public Task<bool> RequestToolBatchApprovalAsync(string explanationMarkdown, IReadOnlyList<ToolCall> batch, CancellationToken ct)
        => RequestApproval(string.IsNullOrWhiteSpace(explanationMarkdown) ? "Run these tools?" : explanationMarkdown,
                           BuildBatchSummary(batch), ct);

    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
        => RequestApproval($"Plan: {plan.Title}", $"Approve plan: {plan.Title}", ct);

    private Task<bool> RequestApproval(string explanation, string summary, CancellationToken ct)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return dispatcher.Invoke(() => RequestApproval(explanation, summary, ct));

        var item = new TimelineApproval(explanation, summary);
        InsertBeforeActivity(item);
        _turnItems.Add(item);
        ct.Register(item.Cancel);
        return item.Decision;
    }

    /// <summary>Render the final answer and persist the exchange (original prompt + any interjections).</summary>
    public void ShowFinal(string finalMarkdown) => OnUi(async () =>
    {
        RemoveActivity();
        AddTurnItem(new TimelineAssistantMessage(finalMarkdown));

        var assistant = new ConversationMessage { Text = finalMarkdown, IsUser = false, Timestamp = DateTime.Now };
        Messages.Add(assistant);
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
    });

    private async Task PersistExchangeAsync(IReadOnlyList<ConversationMessage> users, ConversationMessage assistant)
    {
        if (Conversation is null) return;

        foreach (var u in users) Conversation.Messages.Add(u);
        Conversation.Messages.Add(assistant);
        Conversation.Attachments = [.. Attachments];
        _contentAddedThisSession = true;

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

    private static void OnUi(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is not null && !d.CheckAccess()) d.Invoke(action);
        else action();
    }

    /// <summary>
    /// Mirrors the pinned context into the record and persists it (so it repopulates on reopen).
    /// Skipped while restoring; a brand-new chat is left unsaved until its first exchange to avoid
    /// cluttering the browser with empty conversations.
    /// </summary>
    private void SyncContext()
    {
        if (_restoring || Conversation is null) return;

        _aiService.SetConversationContext(Conversation, ContextItems);

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

    /// <summary>Adds a page-as-context drop target. No-op if already present.</summary>
    public void AddContextItem(Page page)
    {
        if (page is null || ReferenceEquals(page, _ownerPage)) return;
        if (ContextItems.Contains(page)) return;
        ContextItems.Add(page);
        TrackRisk(page);
    }

    public void RemoveContextItem(Page page)
    {
        ContextItems.Remove(page);
        UntrackRisk(page);

        // A detached page we created has no tab — close it when it leaves the context area.
        if (_ownedContextPages.Remove(page))
            page.RaiseClosed();
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
        UpdateRisk(page);
        OnPropertyChanged(nameof(IsContextReady));
        if (_riskSubscriptions.ContainsKey(page)) return;
        if ((page.Content as IPageView)?.ViewModel is not INotifyPropertyChanged source) return;

        PropertyChangedEventHandler handler = (_, _) =>
        {
            UpdateRisk(page);
            OnPropertyChanged(nameof(IsContextReady));
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
    private void RemoveContextItemCmd(Page page) => RemoveContextItem(page);

    /// <summary>Lightweight page definitions that can be created context-free and pinned here (e.g.
    /// "Projects" when enabled), for the context-area menu — which reads each page's Title/Icon.
    /// Evaluated fresh each access (reflects live enablement) and excludes already-pinned kinds.</summary>
    public IReadOnlyList<Page> AvailableContextPages
        => [.. _shell.GetContextItemPages().Where(cand => ContextItems.All(c => c.PageKind != cand.PageKind))];

    /// <summary>Pins a context-free page (built by the menu) as a context item: realizes its content
    /// and takes ownership of its lifecycle (closed when removed / when this conversation closes).</summary>
    [RelayCommand]
    private void AddContextPage(Page page)
    {
        if (page is null) return;
        if (page.PageKind is { } kind && ContextItems.Any(p => p.PageKind == kind)) return;   // already pinned

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
        foreach (var p in ContextItems)      words += CountWords(GetContextFor(p));
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

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{size:0.#} {units[u]}";
    }

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
        ContextItems.All(p => (p.Content as IPageView)?.ViewModel?.IsContextReady ?? true);

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
            .Select(p => (p.Content as IPageView)?.ViewModel?.GetSecurityContext())
            .Where(s => s is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() >= 2;

        foreach (var p in ContextItems)
        {
            var part = GetContextFor(p);
            if (string.IsNullOrWhiteSpace(part)) continue;
            sb.AppendLine("---");
            var security = (p.Content as IPageView)?.ViewModel?.GetSecurityContext();
            sb.AppendLine(multiContext && security is not null
                ? $"[Context item: {p.Title} — security context: {NameFor(security)} (scope: {security})]"
                : $"[Context item: {p.Title}]");
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
        foreach (var p in ContextItems)
        {
            if ((p.Content as IPageView)?.ViewModel is not IPageViewModel vm) continue;
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
