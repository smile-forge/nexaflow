using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Core.Models;

public enum TaskStatus { Running, Completed, Failed }

public partial class BackgroundTask : ObservableObject
{
    [ObservableProperty] private string     _description = string.Empty;
    [ObservableProperty] private TaskStatus _status      = TaskStatus.Running;
    public DateTime StartedAt { get; set; } = DateTime.Now;
}
