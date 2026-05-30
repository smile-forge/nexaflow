using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat.ViewModels;

/// <summary>
/// View-model for a single conversation page. Owns the conversation record,
/// the context items dragged onto the banner, file attachments, and the
/// estimated-token / context-window footer values.
/// </summary>
public partial class ConversationViewModel : ObservableObject, IPageViewModel
{
    private readonly IAIService    _aiService;
    private readonly IShellServices _shell;
    private readonly AiChatConfig  _config;
    private readonly Page          _ownerPage;

    /// <summary>True once an exchange has been appended in this open session — gates analysis on close.</summary>
    private bool _contentAddedThisSession;

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

    public ConversationViewModel(IAIService aiService, IShellServices shell, AiChatConfig config, Page ownerPage)
    {
        _aiService = aiService;
        _shell     = shell;
        _config    = config;
        _ownerPage = ownerPage;

        Messages.CollectionChanged    += (_, _) => RecomputeTokens();
        ContextItems.CollectionChanged += (_, _) => RecomputeTokens();
        Attachments.CollectionChanged  += (_, _) => RecomputeTokens();

        _ownerPage.Closed += OnOwnerClosed;
    }

    /// <summary>
    /// On tab close, queue a background analysis of the conversation when analysis is enabled and
    /// content was added this session. Replaces any prior analysis (covers the full current transcript).
    /// </summary>
    private void OnOwnerClosed(object? sender, EventArgs e)
    {
        _ownerPage.Closed -= OnOwnerClosed;

        if (!_config.IsAnalysisEnabled) return;
        if (!_contentAddedThisSession)  return;
        if (Conversation is not { Messages.Count: > 0 }) return;

        _shell.QueueBackgroundTask(new ConversationAnalysisTask(_aiService, Conversation));
    }

    /// <summary>Loads a persisted conversation by id and pulls the model context window.</summary>
    public async Task LoadAsync(string conversationId)
    {
        var all = await _aiService.LoadAllAsync();
        var rec = all.FirstOrDefault(c => c.Id == conversationId);
        if (rec is null) return;

        Conversation = rec;
        Title        = rec.Title;
        Messages.Clear();
        foreach (var m in rec.Messages.OrderBy(m => m.Timestamp))
            Messages.Add(m);

        Attachments.Clear();
        foreach (var a in rec.Attachments)
            Attachments.Add(a);

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

    /// <summary>
    /// Appends a user prompt + AI response to this conversation (and to disk).
    /// Called by ShellViewModel when the user types into the AI bar while a
    /// Conversation page is active.
    /// </summary>
    public async Task AppendExchangeAsync(string userText, string aiText)
    {
        if (Conversation is null) return;

        var u = new ConversationMessage { Text = userText, IsUser = true,  Timestamp = DateTime.Now };
        var a = new ConversationMessage { Text = aiText,   IsUser = false, Timestamp = DateTime.Now };

        Conversation.Messages.Add(u);
        Conversation.Messages.Add(a);
        Messages.Add(u);
        Messages.Add(a);
        Conversation.Attachments = [.. Attachments];
        _contentAddedThisSession = true;

        try { await _aiService.SaveAsync(Conversation); }
        catch { /* persistence failures shouldn't kill the UI */ }
    }

    /// <summary>Adds a page-as-context drop target. No-op if already present.</summary>
    public void AddContextItem(Page page)
    {
        if (page is null || ReferenceEquals(page, _ownerPage)) return;
        if (ContextItems.Contains(page)) return;
        ContextItems.Add(page);
    }

    public void RemoveContextItem(Page page) => ContextItems.Remove(page);

    [RelayCommand]
    private void RemoveContextItemCmd(Page page) => RemoveContextItem(page);

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

        foreach (var p in ContextItems)
        {
            var part = GetContextFor(p);
            if (string.IsNullOrWhiteSpace(part)) continue;
            sb.AppendLine("---");
            sb.AppendLine($"[Context item: {p.Title}]");
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
