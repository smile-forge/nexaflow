namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by page UserControls that support a user-triggered refresh
/// (e.g. re-clicking the already-active tab). Pages implementing this will
/// have <see cref="Refresh"/> called by the shell when the active tab is
/// re-selected.
/// </summary>
public interface IRefreshable
{
    /// <summary>Refreshes the page content in-place without changing the current path or context.</summary>
    void Refresh();
}
