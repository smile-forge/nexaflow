using System.Collections.ObjectModel;
using System.IO;
using System.Text;
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
    private readonly IAIService _aiService;
    private readonly Page       _ownerPage;

    [ObservableProperty] private ConversationRecord? _conversation;
    [ObservableProperty] private string _title = "Conversation";
    [ObservableProperty] private int   _estimatedTokens;
    [ObservableProperty] private int?  _contextWindow;

    public ObservableCollection<ConversationMessage> Messages    { get; } = [];
    public ObservableCollection<Page>                ContextItems { get; } = [];
    public ObservableCollection<string>              Attachments  { get; } = [];

    public ConversationViewModel(IAIService aiService, Page ownerPage)
    {
        _aiService = aiService;
        _ownerPage = ownerPage;

        Messages.CollectionChanged    += (_, _) => RecomputeTokens();
        ContextItems.CollectionChanged += (_, _) => RecomputeTokens();
        Attachments.CollectionChanged  += (_, _) => RecomputeTokens();
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

        UpdateBreadcrumb();

        try { ContextWindow = await _aiService.GetConversationContextWindowAsync(); }
        catch { ContextWindow = null; }

        RecomputeTokens();
    }

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
    /// Combined context handed to the AI on every call: each pinned context item
    /// (folder listings for FileSystem tabs, plain GetContext text for others)
    /// followed by the last six messages of this conversation. Attachments are
    /// listed by path.
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
                sb.Append("  ").AppendLine(a);
        }

        sb.AppendLine("---");
        sb.AppendLine("[Conversation]");
        if (Conversation is null || Conversation.Messages.Count == 0)
            sb.AppendLine("(empty)");
        else
            foreach (var m in Conversation.Messages.TakeLast(6))
                sb.AppendLine((m.IsUser ? "User: " : "Assistant: ") + m.Text);

        return sb.ToString();
    }

    public IReadOnlyList<ActionDescriptor> GetAvailableActions() => [];
}
