using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat.ViewModels;

/// <summary>
/// One row in the AI Chat conversation browser: the record plus its lazily-loaded analysis summary.
/// </summary>
public partial class ConversationRowViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IAIService _ai;
    private readonly bool       _analysisEnabled;

    public ConversationRecord Record { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnalysis))]
    private ConversationAnalysis? _analysis;

    [ObservableProperty] private string _summaryText = "Loading…";

    public ConversationRowViewModel(ConversationRecord record, IAIService ai, bool analysisEnabled)
    {
        Record           = record;
        _ai              = ai;
        _analysisEnabled = analysisEnabled;
    }

    public string Title        => Record.Title;
    public string StartDisplay => Record.StartedAt.ToString("MMM d, HH:mm");
    public string EndDisplay   => Record.Messages.Count > 0
        ? Record.Messages.Max(m => m.Timestamp).ToString("MMM d, HH:mm")
        : StartDisplay;
    public int    MessageCount => Record.Messages.Count;
    public bool   HasAnalysis  => Analysis is not null;

    /// <summary>Loads this row's analysis artifact (if enabled) and sets the middle-column summary text.</summary>
    public async Task LoadAsync()
    {
        if (!_analysisEnabled)
        {
            SummaryText = "Summarization disabled…";
            return;
        }

        try
        {
            var json = await _ai.LoadConversationArtifactAsync(Record.Id, ConversationAnalysisTask.ArtifactName);
            if (!string.IsNullOrWhiteSpace(json))
                Analysis = JsonSerializer.Deserialize<ConversationAnalysis>(json, JsonOpts);
        }
        catch { Analysis = null; }

        SummaryText = Analysis is { Summary.Length: > 0 } a
            ? a.Summary
            : "No summary yet — open the conversation, chat, then close it to analyze.";
    }
}
