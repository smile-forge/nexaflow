using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.AIChat.ViewModels.Timeline;

/// <summary>A committed user message bubble. Holds the underlying <see cref="ConversationMessage"/> rather
/// than a copy of its text, so the rewind affordance has something to rewind <em>to</em>.</summary>
public sealed partial class TimelineUserMessage(ConversationMessage message) : ObservableObject
{
    public ConversationMessage Message => message;

    public string   Text      => message.Text;
    public DateTime Timestamp => message.Timestamp;

    /// <summary>Recomputed rather than stored: "5 minutes ago" is only true for five minutes.</summary>
    public string TimestampDisplay => RelativeTimeFormatter.Format(message.Timestamp);

    /// <summary>Only the newest user message offers rewind — rewinding to an earlier one would silently
    /// discard everything after it, which is a different (and much sharper) gesture.</summary>
    [ObservableProperty] private bool _isLast;

    /// <summary>True when a "?" page search matched this message — see ConversationViewModel.Search.</summary>
    [ObservableProperty] private bool _isSearchHit;

    public void RefreshTimestamp() => OnPropertyChanged(nameof(TimestampDisplay));
}

/// <summary>A committed assistant message bubble (markdown). Observable only so a "?" search can mark it
/// in place — a conversation is read, not filtered, so marking is how a result is shown.</summary>
public sealed partial class TimelineAssistantMessage(string text) : ObservableObject
{
    public string Text => text;

    /// <summary>True when a "?" page search matched this message — see ConversationViewModel.Search.</summary>
    [ObservableProperty] private bool _isSearchHit;
}

/// <summary>A muted, session-only line — e.g. the model finished a turn without saying anything. Never
/// persisted: it records the absence of a message, not a message.</summary>
public sealed class TimelineNotice(string text)
{
    public string Text => text;
}

/// <summary>One executed tool inside a batch.</summary>
public sealed class TimelineToolRun(string tool, bool isError, string summary)
{
    public string Tool    => tool;
    public bool   IsError => isError;
    public string Summary => summary;
}

/// <summary>The tool calls run during one assistant turn (across however many model steps), collapsed
/// to "Ran N tools" and expandable. Accumulates as more steps run.</summary>
public sealed partial class TimelineToolBatch : ObservableObject
{
    public TimelineToolBatch()
        => Tools.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Header));

    public ObservableCollection<TimelineToolRun> Tools { get; } = [];

    [ObservableProperty] private bool _isExpanded;

    public string Header => $"Ran {Tools.Count} tool{(Tools.Count == 1 ? "" : "s")}";
}

/// <summary>The single live "Considering… / Running X…" line shown while the agent works.</summary>
public sealed partial class TimelineActivity : ObservableObject
{
    [ObservableProperty] private string _text = "Considering";
}

/// <summary>An inline approve/deny prompt for a tool batch (or plan). Resolves a decision task.</summary>
public sealed partial class TimelineApproval : ObservableObject
{
    private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TimelineApproval(string explanation, string summary)
    {
        Explanation = explanation;
        Summary     = summary;
    }

    /// <summary>The model's own prose for why it wants to run these tools. Rendered above the question —
    /// without it an approval prompt is a bare "Run x?" with no stated reason.</summary>
    public string Explanation { get; }

    public string Summary     { get; }

    /// <summary>False when the model gave no reason, so the template can collapse the row rather than
    /// leave an empty gap.</summary>
    public bool HasExplanation => !string.IsNullOrWhiteSpace(Explanation);

    [ObservableProperty] private bool _resolved;
    [ObservableProperty] private bool _approved;

    /// <summary>Completes true (approve) / false (deny or cancel).</summary>
    public Task<bool> Decision => _tcs.Task;

    [RelayCommand]
    private void Approve()
    {
        if (Resolved) return;
        Approved = true; Resolved = true;
        _tcs.TrySetResult(true);
    }

    [RelayCommand]
    private void Deny()
    {
        if (Resolved) return;
        Approved = false; Resolved = true;
        _tcs.TrySetResult(false);
    }

    /// <summary>Resolve as denied without user action (run cancelled).</summary>
    public void Cancel()
    {
        if (Resolved) return;
        Resolved = true;
        _tcs.TrySetResult(false);
    }
}
