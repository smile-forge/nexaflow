namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by a page view that hosts an "airspace" control — a native child window (HWND), such as
/// WebView2, that renders on top of ALL WPF content regardless of z-order. WPF can't paint over it, so
/// the shell's modal overlays (the AI response panel, Options, confirmations…) would be hidden behind it.
/// <para>
/// The shell calls <see cref="SetCoveredByOverlay"/> on every realized page view in a window when a
/// window-modal overlay is shown or dismissed over the content area. A view that hosts such a control
/// hides it while covered (e.g. collapses the WebView2) so the overlay is visible, and restores it after.
/// </para>
/// </summary>
public interface IAirspaceContent
{
    /// <summary>
    /// Called with true when a window-modal overlay now covers the page content, and false when the last
    /// one is dismissed. Always invoked on the UI thread. Implementations toggle their airspace control's
    /// visibility accordingly; idempotent (the same value may be delivered more than once).
    /// </summary>
    void SetCoveredByOverlay(bool covered);
}
