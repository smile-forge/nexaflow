using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Core.Models;

public partial class NotificationItem : ObservableObject
{
    [ObservableProperty] private string _title   = string.Empty;
    [ObservableProperty] private string _body    = string.Empty;
    [ObservableProperty] private bool   _isRead;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
