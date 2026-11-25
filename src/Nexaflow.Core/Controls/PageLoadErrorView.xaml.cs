using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Dependencies;
using Nexaflow.Interaction;
using Nexaflow.Localization;
using Nexaflow.Visuals.Common.Dropping;

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
public partial class PageLoadErrorView : UserControl
{
    private readonly string _details;
    private readonly ExternalDependencyRegistry _dependencies;
    private readonly Action _showComponents;

    /// <param name="page">The page that failed, for its title and kind; null when there is none to name.</param>
    /// <param name="error">The exception the content factory threw.</param>
    /// <param name="dependencies">The session's components, any missing one of which is named as the likely cause — given by
    /// the session's <see cref="PageViews"/>, which makes this view.</param>
    /// <param name="showComponents">Opens the About page, which lists every component, in a window open on the session.</param>
    /// <param name="logPath">The crash log the failure was recorded in — by the <see cref="PageViews"/> that caught it.</param>
    public PageLoadErrorView(Page? page, Exception? error, ExternalDependencyRegistry dependencies, Action showComponents, string logPath)
    {
        _dependencies   = dependencies;
        _showComponents = showComponents;
        InitializeComponent();

        var what = Describe(page);
        HeadlineText.Text = what is null ? Str.Get("Shell.PageError.Headline") : Str.Format("Shell.PageError.HeadlineFormat", what);
        SummaryText.Text  = Str.Get("Shell.PageError.Summary");

        _details       = BuildDetails(page, error);
        DetailBox.Text = _details;

        ShowLikelyCauses();

        LogPathText.Text = Str.Format("Shell.PageError.LogPathFormat", logPath);
    }

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
            blocking = _dependencies.Reports.Where(r => r.IsBlocking).ToList();
        }
        catch { return; }

        if (blocking.Count == 0) return;

        DependencyList.ItemsSource = blocking;
        DependencyPanel.Visibility = Visibility.Visible;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        // The clipboard can be held by another process; not worth a second error surface.
        _ = ClipboardRouter.Write(this, ComposedPayload.OfText(_details));
    }

    private void OnComponentsClick(object sender, RoutedEventArgs e)
    {
        try { _showComponents(); }
        catch { }
    }
}
