using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Common.Viewlets;

/// <summary>
/// The host-provided handle passed to <see cref="IFolderViewlet.CreateView"/>. Lets the viewlet read and
/// change its current <see cref="ViewletDisplayMode"/> (SingleBar / DoubleBar / Large / Full) and observe
/// mode changes the host drives — so the viewlet stays ignorant of the file browser's chrome.
/// </summary>
public interface IViewletController
{
    ViewletDisplayMode CurrentMode { get; }
    event Action<ViewletDisplayMode>? ModeChanged;
    void SetDisplayMode(ViewletDisplayMode mode);

    /// <summary>
    /// Asks the host to quiesce every viewlet on this folder (plus the file browser's own folder-touching
    /// work) before the caller mutates it — e.g. the Git viewlet deleting a worktree. The host fans out to
    /// each active viewlet view implementing <see cref="IViewletQuiescible"/> and awaits them, so on return
    /// no viewlet is holding a handle or running a child process against the folder. Best-effort.
    /// </summary>
    Task QuiesceFolderAsync(CancellationToken ct = default);

    /// <summary>
    /// Tells the host the displayed folder may no longer exist (e.g. the Git viewlet just deleted this
    /// worktree). The file browser re-checks the current path and, if it's gone, walks up to the nearest
    /// surviving ancestor — keeping the user in the <em>same</em> tab rather than stranding it on a dead
    /// location. A no-op when the folder still exists.
    /// </summary>
    void InvalidateLocation();
}
