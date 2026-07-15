using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Core.Models;

public partial class NotificationItem : ObservableObject
{
    [ObservableProperty] private string _title   = string.Empty;
    [ObservableProperty] private string _body    = string.Empty;
    [ObservableProperty] private bool   _isRead;
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>Severity — drives the toast/inbox icon and colour.</summary>
    public MessageSeverity Severity { get; init; } = MessageSeverity.Info;

    /// <summary>Whether this message also surfaces as a transient toast. Defaults to true — every notification
    /// toasts (and auto-dismisses); set false only for a deliberately inbox-only message.</summary>
    public bool ShowToast { get; init; } = true;

    /// <summary>Toast-only: shown as a toast but <b>not</b> kept in the inbox. For a passing offer (e.g.
    /// "Restore last session?") that is meaningless once it has scrolled past — persisting it would leave a
    /// dead entry in the notifications list. A transient message is fire-and-forget: if no window surfaces
    /// it, it's simply gone.</summary>
    public bool Transient { get; init; }

    /// <summary>Set by the first window that toasts this message, so others don't re-toast it.</summary>
    [ObservableProperty] private bool _shownAsToast;

    /// <summary>How long the toast stays up; null = severity default. Sticky messages use a long value.</summary>
    public TimeSpan? ToastDuration { get; init; }

    /// <summary>Optional action buttons (e.g. "Update", "Retry"), shown in the toast and the inbox.</summary>
    public IReadOnlyList<MessageAction> Actions { get; init; } = [];

    /// <summary>True when there is at least one action — bound by the UI to show/hide the button row.</summary>
    public bool HasActions => Actions.Count > 0;
}
