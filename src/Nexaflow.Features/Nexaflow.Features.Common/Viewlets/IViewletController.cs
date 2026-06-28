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
}
