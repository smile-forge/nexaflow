using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.AIChat.ViewModels;

/// <summary>
/// One row in the AI Chat conversation browser: the record plus its lazily-loaded analysis summary.
/// </summary>
public partial class ConversationRowViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IAIService    _ai;
    private readonly bool          _analysisEnabled;
    private readonly Func<string?> _disabledTip;

    public ConversationRecord Record { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnalysis))]
    private ConversationAnalysis? _analysis;

    [ObservableProperty] private string _summaryText = Str.Get("AIChat.Browser.Row.Loading");

    /// <param name="disabledTip">Why no conversation is summarised now — no model to do it — or null when one is.</param>
    public ConversationRowViewModel(ConversationRecord record, IAIService ai, bool analysisEnabled, Func<string?> disabledTip)
    {
        Record           = record;
        _ai              = ai;
        _analysisEnabled = analysisEnabled;
        _disabledTip     = disabledTip;
    }

    public string Title        => ConversationViewModel.DisplayTitle(Record);
    public string StartDisplay => Record.StartedAt.ToString(Str.Get("AIChat.Browser.Row.DateFormat"));
    public string EndDisplay   => Record.Messages.Count > 0
        ? Record.Messages.Max(m => m.Timestamp).ToString(Str.Get("AIChat.Browser.Row.DateFormat"))
        : StartDisplay;
    public int    MessageCount => Record.Messages.Count;
    public bool   HasAnalysis  => Analysis is not null;

    /// <summary>Loads this row's analysis artifact (if enabled) and sets the middle-column summary text.</summary>
    public async Task LoadAsync()
    {
        if (!_analysisEnabled)
        {
            SummaryText = Str.Get("AIChat.Browser.Row.SummaryDisabled");
            return;
        }

        try
        {
            var json = await _ai.LoadConversationArtifactAsync(Record.Id, ConversationAnalysisTask.ArtifactName);
            if (!string.IsNullOrWhiteSpace(json))
                Analysis = JsonSerializer.Deserialize<ConversationAnalysis>(json, JsonOpts);
        }
        catch { Analysis = null; }

        SummaryText = Analysis is { Summary.Length: > 0 } a ? a.Summary
                    : _disabledTip() is { } disabled      ? disabled
                    : Str.Get("AIChat.Browser.Row.NoSummary");
    }
}
