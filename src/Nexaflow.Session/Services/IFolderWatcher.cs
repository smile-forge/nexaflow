using Nexaflow.Features.Common;

namespace Nexaflow.Session.Services;

/// <summary>
/// Watches folders the way the desktop shell sees them change — a file made, renamed or deleted by any program — and tells
/// each subscriber on the UI thread, a burst of changes settled into one. How it hears of a change is the platform's (on
/// Windows, the shell's change notifications, delivered to a window of the toolkit's), so the app makes it and hands
/// <see cref="FolderActivity"/> the means to.
/// </summary>
public interface IFolderWatcher : IDisposable
{
    /// <summary>Tells <paramref name="onChanged"/> what changed in <paramref name="path"/> until the result is disposed.</summary>
    IDisposable Watch(string path, Action<FolderChange> onChanged);
}

