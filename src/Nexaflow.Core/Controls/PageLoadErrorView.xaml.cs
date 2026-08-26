using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Dependencies;

namespace Nexaflow.Core.Controls;

/// <summary>
/// What a tab shows when the feature's view could not be constructed.
/// <para>
/// The alternative — and what happened before this existed — is that the exception escapes tab activation
/// and reaches the app-level dispatcher handler, which can only post "Something went wrong". That toast is
/// unactionable for the user and unreportable for us: it names neither the tab nor the cause. Here the
/// failure stays attached to the tab that owns it, the exception is selectable and copyable, and any
/// missing third-party component is named as the likely reason.
/// </para>
/// </summary>
public partial class PageLoadErrorView : UserControl, IPageView
{
    private readonly string _details;

    public PageLoadErrorView() : this(null, null) { }

    /// <param name="page">The page that failed, for its title and kind. Null in design/test construction.</param>
    /// <param name="error">The exception the content factory threw.</param>
    public PageLoadErrorView(Page? page, Exception? error)
    {
        InitializeComponent();

        var what = Describe(page);
        HeadlineText.Text = what is null ? "This tab couldn't open" : $"{what} couldn't open";
        SummaryText.Text  =
            "The rest of Nexaflow is unaffected — you can close this tab and carry on. "
            + "If it keeps happening, the detail below is what to report.";

        _details       = BuildDetails(page, error);
        DetailBox.Text = _details;

        ShowLikelyCauses();

        LogPathText.Text = $"Also written to {Path.Combine(ConfigManager.Instance.BaseDir, "crash.log")}";
    }

    /// <summary>This page has no feature view-model — it exists because building one failed.</summary>
    public IPageViewModel? ViewModel => null;

    private static string? Describe(Page? page)
    {
        if (page is null) return null;
        if (!string.IsNullOrWhiteSpace(page.Title))    return page.Title;
        if (!string.IsNullOrWhiteSpace(page.PageKind)) return page.PageKind;
        return null;
    }

    private static string BuildDetails(Page? page, Exception? error)
    {
        var lines = new List<string>();
        if (page?.PageKind is { Length: > 0 } kind) lines.Add($"Page kind: {kind}");
        if (page?.Title    is { Length: > 0 } title) lines.Add($"Title: {title}");

        lines.Add(error?.ToString() ?? "No exception was recorded.");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Names any Required component the last probe could not find. This is a correlation, not a diagnosis —
    /// hence "probably" in the UI and nothing at all when everything is present. A missing native runtime is
    /// by far the most common way a view constructor throws, so pointing at it is worth the hedge.
    /// </summary>
    private void ShowLikelyCauses()
    {
        IReadOnlyList<ExternalDependencyReport> blocking;
        try
        {
            blocking = ExternalDependencyRegistry.Instance.Reports.Where(r => r.IsBlocking).ToList();
        }
        catch { return; }

        if (blocking.Count == 0) return;

        DependencyList.ItemsSource = blocking;
        DependencyPanel.Visibility = Visibility.Visible;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_details); }
        catch { /* the clipboard can be held by another process; not worth a second error surface */ }
    }

    private void OnComponentsClick(object sender, RoutedEventArgs e)
    {
        // Via the interface, like AboutControl's reset button: the concrete ShellServices implements the
        // options members explicitly.
        IShellServices? shell = WorkspaceManager.Instance.FirstActive?.ShellServices;
        try { shell?.OpenOptions("about"); }
        catch { }
    }
}
