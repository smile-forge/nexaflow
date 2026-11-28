using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Nexaflow.Session.Services;

/// <summary>
/// The session's inbox of <see cref="NotificationItem"/>s, shared by every window. A message is the source of truth; toasts are
/// a transient presentation of one, and one window toasts each (<see cref="ClaimToast"/>). Messages posted while no window
/// exists (e.g. an update found by the <c>--prestart</c> daemon) stay <see cref="NotificationItem.ShowToast"/> and
/// not-yet-<see cref="NotificationItem.ShownAsToast"/> until a window opens and claims them. It keeps the unread count every
/// window's badge shows, whichever window a message is read in.
/// In-memory only — action commands are live code and update/voice messages regenerate.
/// </summary>
public sealed class MessageCenter
{
    /// <param name="ui">The thread the windows run on, where the inbox changes and its listeners hear of it.</param>
    public MessageCenter(IUiThread ui)
    {
        _ui = ui;
        Messages.CollectionChanged += (_, _) => FollowMessages();
    }

    /// <summary>The thread the windows run on: a message posted from any thread joins the inbox there.</summary>
    private readonly IUiThread _ui;

    /// <summary>The messages whose being read is followed: those in the inbox.</summary>
    private readonly HashSet<NotificationItem> _followed = [];

    /// <summary>The one shared inbox; newest first.</summary>
    public ObservableCollection<NotificationItem> Messages { get; } = [];

    /// <summary>Raised after a message is posted, so the focused window can toast it.</summary>
    public event EventHandler<NotificationItem>? MessagePosted;

    /// <summary>How many messages in the inbox are unread.</summary>
    public int UnreadCount { get; private set; }

    /// <summary>Raised when <see cref="UnreadCount"/> changes: a message came or went, or was read.</summary>
    public event EventHandler? UnreadChanged;

    /// <summary>Toast-worthy messages no window has surfaced yet — replayed when a window gains focus.</summary>
    public IEnumerable<NotificationItem> PendingToasts =>
        Messages.Where(m => m.ShowToast && !m.ShownAsToast);

    /// <summary>Adds a message to the inbox (newest first) and notifies listeners. Thread-safe.
    /// A <see cref="NotificationItem.Transient"/> message skips the inbox — it toasts and is gone.</summary>
    public void Post(NotificationItem message)
    {
        OnUi(() =>
        {
            if (!message.Transient) Messages.Insert(0, message);
            MessagePosted?.Invoke(this, message);
        });
    }

    public void Remove(NotificationItem message) => OnUi(() => Messages.Remove(message));

    /// <summary>Marks every message read: the inbox has been opened.</summary>
    public void MarkAllRead()
    {
        foreach (var message in Messages) message.IsRead = true;
    }

    /// <summary>Claims <paramref name="message"/> for the window about to toast it: true for the first to ask of a message that
    /// toasts, false from then on, so it toasts in one window only.</summary>
    public bool ClaimToast(NotificationItem message)
    {
        if (!message.ShowToast || message.ShownAsToast) return false;
        message.ShownAsToast = true;
        return true;
    }

    /// <summary>Claims every message no window has toasted yet: what a window gaining focus toasts.</summary>
    public IReadOnlyList<NotificationItem> ClaimPendingToasts() => [.. PendingToasts.ToList().Where(ClaimToast)];

    private void FollowMessages()
    {
        var inbox = Messages.ToHashSet();
        foreach (var gone in _followed.Where(message => !inbox.Contains(message)).ToList())
        {
            gone.PropertyChanged -= OnMessageChanged;
            _followed.Remove(gone);
        }
        foreach (var message in Messages)
            if (_followed.Add(message)) message.PropertyChanged += OnMessageChanged;
        Recount();
    }

    private void OnMessageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationItem.IsRead)) Recount();
    }

    private void Recount()
    {
        var unread = Messages.Count(message => !message.IsRead);
        if (unread == UnreadCount) return;
        UnreadCount = unread;
        UnreadChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnUi(Action action) => _ui.Run(action);
}
