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
    /// Tells the host to re-evaluate the displayed folder after a mutation the caller performed (e.g. the Git
    /// viewlet removing a worktree). If the folder is now gone, the file browser walks up to the nearest
    /// surviving ancestor — keeping the user in the <em>same</em> tab rather than stranding it on a dead
    /// location. If it still exists, the browser refreshes the folder contents and tears down + rebuilds the
    /// viewlets in place (the mutation may have changed which viewlets apply, e.g. a <c>.git</c> that's gone).
    /// </summary>
    void InvalidateLocation();
}
