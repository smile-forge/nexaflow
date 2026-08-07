using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat.ViewModels;

/// <summary>
/// Conversation browser: a single list of conversations (title, start/end, message count, summary)
/// with per-row Open / Analysis / Delete actions and a date filter. Analysis detail is shown in a modal
/// overlay that must be dismissed before interacting with another row. Actual chatting happens in
/// ConversationView.
/// </summary>
public partial class AiChatViewModel : ObservableObject, IPageViewModel
{
    private readonly IAIService     _aiService;
    private readonly IShellServices _shell;
    private readonly AiChatConfig   _config;

    /// <summary>All loaded records (unfiltered); <see cref="Items"/> is the filtered, sorted view.</summary>
    private List<ConversationRecord> _allRecords = [];

    /// <summary>Conversation rows for the active filter, newest first.</summary>
    public ObservableCollection<ConversationRowViewModel> Items { get; } = [];

    /// <summary>Date-range filter options shown in the header dropdown.</summary>
    public IReadOnlyList<string> Filters { get; } = ["Today", "Last 7 days", "This month", "This year"];

    [ObservableProperty] private string _selectedFilter = "Last 7 days";

    /// <summary>The row whose analysis overlay is open, or null when the overlay is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnalysisOverlayOpen))]
    private ConversationRowViewModel? _analysisOverlayItem;

    public bool IsAnalysisOverlayOpen => AnalysisOverlayItem is not null;

    public AiChatViewModel(IAIService aiService, IShellServices shell, AiChatConfig config, Page ownerPage)
    {
        _aiService = aiService;
        _shell     = shell;
        _config    = config;

        // Live-update a row when its background analysis finishes (no manual refresh needed).
        _aiService.ConversationArtifactSaved += OnArtifactSaved;
        ownerPage.Closed += (_, _) => _aiService.ConversationArtifactSaved -= OnArtifactSaved;

        // Purge conversations past the retention window, then reflect the result in the list.
        if (_config.RetentionCutoff() is { } cutoff)
            _shell.QueueBackgroundTask(new ConversationPurgeTask(_aiService, cutoff),
                onComplete: success => { _ = RefreshAsync(); });

        _ = RefreshAsync();
    }

    partial void OnSelectedFilterChanged(string value)
    {
        // Picking a date range is a different question from the one "?" asked; ClearSearch re-applies the
        // filter itself, so this only rebuilds when there was no search to drop.
        if (IsSearchFiltering) ClearSearch();
        else                   ApplyFilter();
    }

    /// <summary>A conversation's artifact (analysis) was saved — reload that row so its summary appears.
    /// May arrive on a background thread, so marshal to the UI before touching <see cref="Items"/>.</summary>
    private void OnArtifactSaved(string conversationId) => _ = _shell.RunOnUiAsync(() =>
    {
        var row = Items.FirstOrDefault(r => r.Record.Id == conversationId);
        if (row is not null) _ = row.LoadAsync();
        else                 _ = RefreshAsync();   // a brand-new conversation not yet in the list
    });

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

    /// <summary>Confirms, then permanently deletes a conversation and drops it from the list.</summary>
    [RelayCommand]
    public void DeleteConversation(ConversationRowViewModel? row)
    {
        if (row is null) return;
        _shell.ShowConfirmation(
            "Delete conversation",
            $"Permanently delete \"{row.Title}\"? This can't be undone.",
            () =>
            {
                _ = _aiService.DeleteConversationAsync(row.Record.Id);
                _allRecords.RemoveAll(r => r.Id == row.Record.Id);
                Items.Remove(row);
            },
            () => { });
    }

    /// <summary>Reloads the conversation list from disk, then applies the active filter. Called on (re)activation.</summary>
    public async Task RefreshAsync()
    {
        _allRecords = (await _aiService.LoadConversationsAsync()).ToList();
        ApplyFilter();
    }

    /// <summary>Rebuilds <see cref="Items"/> from <see cref="_allRecords"/> — for the selected date range,
    /// or for a running "?" search, whichever is deciding (see <c>RecordsToList</c>).</summary>
    private void ApplyFilter()
    {
        AnalysisOverlayItem = null;

        Items.Clear();
        foreach (var r in RecordsToList().OrderByDescending(ConversationPurgeTask.LastActivity))
        {
            var row = new ConversationRowViewModel(r, _aiService, _config.IsAnalysisEnabled);
            Items.Add(row);
            _ = row.LoadAsync();
        }
    }

    private DateTime FilterCutoff() => SelectedFilter switch
    {
        "Today"      => DateTime.Today,
        "This month" => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1),
        "This year"  => new DateTime(DateTime.Now.Year, 1, 1),
        _            => DateTime.Now.AddDays(-7),   // "Last 7 days" (default)
    };

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext() => $"AI Chat browser: {Items.Count} conversation(s).";
}
