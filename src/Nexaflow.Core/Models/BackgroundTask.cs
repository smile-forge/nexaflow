using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nexaflow.Core.Models;

public enum TaskStatus { Running, Completed, Failed, Stopped }

/// <summary>
/// A piece of work the app is doing in the background, as its activity list shows it: what it is, how it stands, and what it
/// is doing now. A job the assistant set going also names its workspace and can be stopped.
/// </summary>
public partial class BackgroundTask : ObservableObject
{
    [ObservableProperty] private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private TaskStatus _status = TaskStatus.Running;

    public DateTime StartedAt { get; set; } = DateTime.Now;

    /// <summary>The workspace whose assistant set it going; null for work the app does of its own accord.</summary>
    public WorkspaceRuntime? Workspace { get; init; }

    /// <summary>Stops it, for work that can be stopped; null for work that cannot.</summary>
    public CancellationTokenSource? Stopping { get; init; }

    /// <summary>What it is doing now, where it says: the step a picture has reached, say.</summary>
    [ObservableProperty] private string? _detail;

    /// <summary>How long it has run, as of the last <see cref="Tick"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ElapsedText))]
    private TimeSpan _elapsed;

    /// <summary>How long it has run, as a clock shows it: minutes and seconds, and hours once it reaches one.</summary>
    public string ElapsedText => Elapsed.TotalHours >= 1 ? Elapsed.ToString(@"h\:mm\:ss") : Elapsed.ToString(@"m\:ss");

    public bool CanStop => Stopping is not null && Status == TaskStatus.Running;

    /// <summary>Brings <see cref="Elapsed"/> up to <paramref name="now"/>.</summary>
    public void Tick(DateTime now) => Elapsed = now - StartedAt;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => Stopping!.Cancel();
}
