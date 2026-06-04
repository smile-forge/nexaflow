using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nexaflow.Features.AIChat.ViewModels.Timeline;

/// <summary>A committed user message bubble.</summary>
public sealed class TimelineUserMessage(string text)
{
    public string Text => text;
}

/// <summary>A committed assistant message bubble (markdown).</summary>
public sealed class TimelineAssistantMessage(string text)
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

    public string Explanation { get; }
    public string Summary     { get; }

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
