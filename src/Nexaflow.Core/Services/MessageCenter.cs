using System.Collections.ObjectModel;
using System.Windows.Threading;
using Nexaflow.Core.Models;

namespace Nexaflow.Core.Services;

/// <summary>
/// App-wide inbox of <see cref="NotificationItem"/>s, shared by every window. A message is the source
/// of truth; toasts are a transient presentation of one. Messages posted while no window exists (e.g.
/// an update found by the <c>--prestart</c> daemon) stay <see cref="NotificationItem.ShowToast"/> +
/// not-yet-<see cref="NotificationItem.ShownAsToast"/> until a window opens and replays them.
/// In-memory only — action commands are live code and update/voice messages regenerate.
/// </summary>
public sealed class MessageCenter
{
    public static MessageCenter Instance { get; } = new();
    private MessageCenter() { }

    /// <summary>
    /// The app UI dispatcher. This singleton is forced into existence on the UI thread from
    /// <c>App.InitializeApp</c> so it captures the app dispatcher even in the windowless
    /// <c>--prestart</c> daemon — where the first <see cref="Post"/> would otherwise arrive from a
    /// background update-check thread and bind <c>_ui</c> to the wrong thread.
    /// </summary>
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    /// <summary>The one shared inbox; newest first.</summary>
    public ObservableCollection<NotificationItem> Messages { get; } = [];

    /// <summary>Raised after a message is posted, so the focused window can toast it.</summary>
    public event EventHandler<NotificationItem>? MessagePosted;

    /// <summary>Toast-worthy messages no window has surfaced yet — replayed when a window gains focus.</summary>
    public IEnumerable<NotificationItem> PendingToasts =>
        Messages.Where(m => m.ShowToast && !m.ShownAsToast);

    /// <summary>Adds a message to the inbox (newest first) and notifies listeners. Thread-safe.</summary>
    public void Post(NotificationItem message)
    {
        OnUi(() =>
        {
            Messages.Insert(0, message);
            MessagePosted?.Invoke(this, message);
        });
    }

    public void Remove(NotificationItem message) => OnUi(() => Messages.Remove(message));

    private void OnUi(Action action)
    {
        if (_ui.CheckAccess()) action();
        else _ui.Invoke(action);
    }
}
