using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat.ViewModels;

/// <summary>
/// Conversation browser: a single list of conversations (title, start/end, message count, summary)
/// with per-row Open and Analysis actions. Analysis detail is shown in a modal overlay that must be
/// dismissed before interacting with another row. Actual chatting happens in ConversationView.
/// </summary>
public partial class AiChatViewModel : ObservableObject, IPageViewModel
{
    private readonly IAIService     _aiService;
    private readonly IShellServices _shell;
    private readonly AiChatConfig   _config;

    /// <summary>Conversation rows, newest first.</summary>
    public ObservableCollection<ConversationRowViewModel> Items { get; } = [];

    /// <summary>The row whose analysis overlay is open, or null when the overlay is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnalysisOverlayOpen))]
    private ConversationRowViewModel? _analysisOverlayItem;

    public bool IsAnalysisOverlayOpen => AnalysisOverlayItem is not null;

    public AiChatViewModel(IAIService aiService, IShellServices shell, AiChatConfig config)
    {
        _aiService = aiService;
        _shell     = shell;
        _config    = config;
        _ = RefreshAsync();
    }

    /// <summary>Opens a fresh, empty conversation tab (persisted on its first exchange).</summary>
    [RelayCommand]
    public void NewConversation()
        => _shell.OpenTab("Conversation", new Dictionary<string, string>());

    /// <summary>Opens the conversation in a ConversationView tab.</summary>
    [RelayCommand]
    public void OpenConversation(ConversationRowViewModel? row)
    {
        if (row is null) return;
        _shell.OpenTab("Conversation", new Dictionary<string, string> { ["conversationId"] = row.Record.Id });
    }

    /// <summary>Opens the analysis detail overlay for a row.</summary>
    [RelayCommand]
    public void ShowAnalysis(ConversationRowViewModel? row)
    {
        if (row is null) return;
        AnalysisOverlayItem = row;
    }

    /// <summary>Dismisses the analysis overlay.</summary>
    [RelayCommand]
    public void CloseAnalysis() => AnalysisOverlayItem = null;

    /// <summary>Reloads the conversation list (and each row's analysis). Called on (re)activation.</summary>
    public async Task RefreshAsync()
    {
        AnalysisOverlayItem = null;

        var records = (await _aiService.LoadAllAsync()).ToList();
        Items.Clear();
        foreach (var r in records)
        {
            var row = new ConversationRowViewModel(r, _aiService, _config.IsAnalysisEnabled);
            Items.Add(row);
            _ = row.LoadAsync();
        }
    }

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext() => $"AI Chat browser: {Items.Count} conversation(s).";
}
