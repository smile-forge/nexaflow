namespace Nexaflow.Features.Common;

/// <summary>
/// Implemented by tab <see cref="System.Windows.Controls.UserControl"/> views to expose
/// their ViewModel to the shell and handle lifecycle events.
/// </summary>
public interface IPageView
{
    /// <summary>The view's primary ViewModel. Exposes AI context and actions to the pipeline.</summary>
    IPageViewModel? ViewModel { get; }

    /// <summary>
    /// Called by the shell when this tab is activated with a param set: on first load, when
    /// routing finds an existing tab, or when the user re-clicks the already-active tab (shell
    /// passes the tab's current params). The page decides what to do — navigate, reload, or
    /// nothing. Never called with null; an empty dict means "no params, just activate."
    /// </summary>
    void Reinitialize(Dictionary<string, string> pageParams) { }
}
