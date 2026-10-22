using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

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

    // The date ranges' invariant ids — what SelectedFilter holds and FilterCutoff reads, whatever the language.
    private const string TodayFilter     = "Today";
    private const string Last7DaysFilter = "Last 7 days";
    private const string ThisMonthFilter = "This month";
    private const string ThisYearFilter  = "This year";

    /// <summary>One date range the header dropdown offers: its invariant <paramref name="Id"/> and the
    /// <paramref name="Label"/> shown for it.</summary>
    public sealed record DateFilterChoice(string Id, string Label);

    /// <summary>Date-range filter options shown in the header dropdown, each id beside the words shown for it.</summary>
    public IReadOnlyList<DateFilterChoice> Filters { get; } =
    [
        new(TodayFilter,     Str.Get("AIChat.Browser.Filter.Today")),
        new(Last7DaysFilter, Str.Get("AIChat.Browser.Filter.Last7Days")),
        new(ThisMonthFilter, Str.Get("AIChat.Browser.Filter.ThisMonth")),
        new(ThisYearFilter,  Str.Get("AIChat.Browser.Filter.ThisYear")),
    ];

    /// <summary>The selected range's id — one of the <see cref="Filters"/> ids, never its label.</summary>
    [ObservableProperty] private string _selectedFilter = Last7DaysFilter;

    /// <summary>The row whose analysis overlay is open, or null when the overlay is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnalysisOverlayOpen))]
    private ConversationRowViewModel? _analysisOverlayItem;

    public bool IsAnalysisOverlayOpen => AnalysisOverlayItem is not null;

    /// <summary>Whether the workspace has a model to hold a conversation and summarise one: General Intelligence.</summary>
    public IAiAbilityGate Answering { get; }

    public AiChatViewModel(IAIService aiService, IShellServices shell, AiChatConfig config, Page ownerPage)
    {
        _aiService = aiService;
        _shell     = shell;
        _config    = config;

        // Live-update a row when its background analysis finishes (no manual refresh needed).
        _aiService.ConversationArtifactSaved += OnArtifactSaved;
        ownerPage.Closed += (_, _) => _aiService.ConversationArtifactSaved -= OnArtifactSaved;

        // A model assigned or taken away changes what a row with no summary says of it.
        Answering = _aiService.Gate(AiAbility.GeneralIntelligence);
        Answering.PropertyChanged += (_, _) => { foreach (var row in Items) _ = row.LoadAsync(); };
        ownerPage.Closed += (_, _) => Answering.Dispose();

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
            Str.Get("AIChat.Browser.Delete.Title"),
            Str.Format("AIChat.Browser.Delete.MessageFormat", row.Title),
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
            var row = new ConversationRowViewModel(r, _aiService, _config.IsAnalysisEnabled, () => Answering.IsEnabled ? null : Answering.DisabledTip);
            Items.Add(row);
            _ = row.LoadAsync();
        }
    }

    private DateTime FilterCutoff() => SelectedFilter switch
    {
        TodayFilter     => DateTime.Today,
        ThisMonthFilter => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1),
        ThisYearFilter  => new DateTime(DateTime.Now.Year, 1, 1),
        _               => DateTime.Now.AddDays(-7),   // Last7DaysFilter (default)
    };

    // ── IPageViewModel ────────────────────────────────────────────────────

    public string GetContext() => $"AI Chat browser: {Items.Count} conversation(s).";
}
