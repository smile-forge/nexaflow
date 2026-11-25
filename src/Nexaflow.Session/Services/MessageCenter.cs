using System.Collections.ObjectModel;


namespace Nexaflow.Session.Services;

/// <summary>
/// The session's inbox of <see cref="NotificationItem"/>s, shared by every window. A message is the source
/// of truth; toasts are a transient presentation of one. Messages posted while no window exists (e.g.
/// an update found by the <c>--prestart</c> daemon) stay <see cref="NotificationItem.ShowToast"/> +
/// not-yet-<see cref="NotificationItem.ShownAsToast"/> until a window opens and replays them.
/// In-memory only — action commands are live code and update/voice messages regenerate.
/// </summary>
public sealed class MessageCenter
{
    /// <param name="ui">The thread the windows run on, where the inbox changes and its listeners hear of it.</param>
    public MessageCenter(IUiThread ui) => _ui = ui;

    /// <summary>The thread the windows run on: a message posted from any thread joins the inbox there.</summary>
    private readonly IUiThread _ui;

    /// <summary>The one shared inbox; newest first.</summary>
    public ObservableCollection<NotificationItem> Messages { get; } = [];

    /// <summary>Raised after a message is posted, so the focused window can toast it.</summary>
    public event EventHandler<NotificationItem>? MessagePosted;

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

    private void OnUi(Action action) => _ui.Run(action);
}
