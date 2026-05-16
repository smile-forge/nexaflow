using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.AIChat.Models;

public partial class ChatMessage : ObservableObject
{
    [ObservableProperty] private string _text        = string.Empty;
    [ObservableProperty] private bool   _isUser;
    [ObservableProperty] private bool   _isStreaming;

    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string TimestampDisplay => Timestamp.ToString("HH:mm");
}
