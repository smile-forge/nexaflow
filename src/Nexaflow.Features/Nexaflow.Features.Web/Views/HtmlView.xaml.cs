using Nexaflow.Features.Common;
using Nexaflow.Features.Web.ViewModels;
using Nexaflow.Visuals.Common.Viewing;
using Nexaflow.Visuals.Web;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.Web.Views;

[ViewFor(typeof(HtmlViewModel))]
public partial class HtmlView : UserControl, IAirspaceContent, IDisposable
{
    private CancellationTokenSource? _snapshotCts;

    public HtmlViewModel ViewModel { get; }

    public HtmlView(HtmlViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        // Let the view-model drive the live page on demand for AI visual context: capture a screenshot,
        // read the scroll position, and scroll through a long page.
        ViewModel.CaptureScreenshotAsync        = (edge, ct) => Surface.CapturePngAsync(edge, ct);
        ViewModel.GetScrollInfoAsync            = ct => Surface.GetScrollInfoAsync(ct);
        ViewModel.ScrollByViewportFractionAsync = (f, ct) => Surface.ScrollByViewportFractionAsync(f, ct);
        ViewModel.NavigationRequested          += url => Surface.NavigateTo(url);

        Surface.NavigationStarting  += Surface_NavigationStarting;
        Surface.NavigationCompleted += Surface_NavigationCompleted;
        Surface.DocumentTitleChanged += (_, _) => ViewModel.ReportDocumentTitle(Surface.DocumentTitle);
        // The nav buttons reflect where the user can actually go. WebView2 exposes CanGoBack/Forward as
        // plain properties with no change notification, so they're pushed on HistoryChanged rather than
        // bound.
        Surface.HistoryChanged += (_, _) => UpdateNavButtons();
        Surface.Unavailable    += (_, e) => ViewModel.ReportBrowserUnavailable(e.RuntimeMissing, e.Message);

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded fires again whenever the view is re-parented (tab moved to another window); the surface
        // is idempotent and leaves a live browser alone, preserving the user's browsing state. Don't
        // re-prompt once we've already fallen back either.
        if (Surface.IsReady || !ViewModel.WebViewAvailable) return;

        if (!await Surface.EnsureReadyAsync()) return;   // Unavailable already fired the fallback

        UpdateNavButtons();
        await ViewModel.Loaded;
        Surface.NavigateTo(ViewModel.NavigationUri.ToString());
    }

    private void Surface_NavigationStarting(object? sender, WebSurfaceNavigationEventArgs e)
    {
        CancelSnapshot();
        ViewModel.ReportNavigationStarting(e.Uri);
    }

    private void Surface_NavigationCompleted(object? sender, WebSurfaceNavigationEventArgs e)
    {
        ViewModel.ReportNavigationCompleted(e.Uri, Surface.DocumentTitle);
        TakeSnapshot();
    }

    /// <summary>Pictures the page just loaded for the context preview. The next navigation cancels it, so a slow
    /// capture of the old page never lands over the new one.</summary>
    private async void TakeSnapshot()
    {
        CancelSnapshot();
        var cts = _snapshotCts = new CancellationTokenSource();
        try { await ViewModel.TakeSnapshotAsync(cts.Token); }
        catch (OperationCanceledException) { /* a newer page, or the tab closed */ }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            // The browser went away mid-capture; the last snapshot stands.
        }
    }

    private void CancelSnapshot()
    {
        _snapshotCts?.Cancel();
        _snapshotCts?.Dispose();
        _snapshotCts = null;
    }

    /// <summary>Enables each nav button only when it would do something — a button the user can press to no
    /// effect is a defect, so the "can I?" test lives here rather than inside the click handler.</summary>
    private void UpdateNavButtons()
    {
        BackButton.IsEnabled    = Surface.CanGoBack;
        ForwardButton.IsEnabled = Surface.CanGoForward;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Surface.GoBack();

    private void Forward_Click(object sender, RoutedEventArgs e) => Surface.GoForward();

    private void Reload_Click(object sender, RoutedEventArgs e) => Surface.Reload();

    // ── WebView2-unavailable fallback ─────────────────────────────────────

    private void OpenExternal_Click(object sender, RoutedEventArgs e) => ViewModel.OpenInDefaultBrowser();

    private void InstallRuntime_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        ViewModel.OpenRuntimeDownload(e.Uri);
    }

    // ── IAirspaceContent ──────────────────────────────────────────────────

    /// <summary>
    /// Collapses the WebView2 while a shell overlay covers the page, and restores it after. The WebView's
    /// native HWND draws above all WPF content, so without this the overlay would be hidden behind it.
    /// </summary>
    void IAirspaceContent.SetCoveredByOverlay(bool covered) => ViewModel.IsCovered = covered;

    /// <summary>Ends the embedded browser process when the tab genuinely closes.</summary>
    public void Dispose()
    {
        CancelSnapshot();
        Surface.Dispose();
    }
}
