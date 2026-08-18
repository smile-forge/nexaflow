using Nexaflow.Features.Common;
using Nexaflow.Features.Web.ViewModels;
using Nexaflow.Visuals.Web;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.Web.Views;

public partial class HtmlView : UserControl, IPageView, IAirspaceContent, IDisposable
{
    private readonly IShellServices _shell;

    public HtmlViewModel ViewModel { get; }

    /// <summary>Raised when the URL or page title changes, so the tab title + breadcrumb can refresh.</summary>
    public event Action? PageChanged;

    public HtmlView(HtmlViewModel viewModel, IShellServices shell)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        _shell      = shell;
        DataContext = viewModel;

        // Let the view-model drive the live page on demand for AI visual context: capture a screenshot,
        // read the scroll position, and scroll through a long page.
        ViewModel.CaptureScreenshotAsync        = ct => Surface.CapturePngAsync(1200, ct);
        ViewModel.GetScrollInfoAsync            = ct => Surface.GetScrollInfoAsync(ct);
        ViewModel.ScrollByViewportFractionAsync = (f, ct) => Surface.ScrollByViewportFractionAsync(f, ct);

        Surface.NavigationStarting  += Surface_NavigationStarting;
        Surface.NavigationCompleted += Surface_NavigationCompleted;
        Surface.DocumentTitleChanged += (_, _) =>
        {
            ViewModel.PageTitle = Surface.DocumentTitle;
            PageChanged?.Invoke();
        };
        // The nav buttons reflect where the user can actually go. WebView2 exposes CanGoBack/Forward as
        // plain properties with no change notification, so they're pushed on HistoryChanged rather than
        // bound.
        Surface.HistoryChanged += (_, _) => UpdateNavButtons();
        Surface.Unavailable    += (_, e) => HandleWebViewUnavailable(e);

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
        Surface.NavigateTo(ViewModel.NavigationUri.ToString());
    }

    /// <summary>Navigates the embedded browser to <paramref name="url"/> (used by breadcrumb crumbs).</summary>
    public void NavigateTo(string url) => Surface.NavigateTo(url);

    private void Surface_NavigationStarting(object? sender, WebSurfaceNavigationEventArgs e)
    {
        ViewModel.IsLoading  = true;
        ViewModel.CurrentUrl = e.Uri;
        ViewModel.PageTitle  = string.Empty;   // old title no longer applies; URL-form shows until the new title arrives
        PageChanged?.Invoke();
    }

    private void Surface_NavigationCompleted(object? sender, WebSurfaceNavigationEventArgs e)
    {
        ViewModel.IsLoading  = false;
        ViewModel.CurrentUrl = e.Uri;
        ViewModel.PageTitle  = Surface.DocumentTitle;
        PageChanged?.Invoke();
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

    private void HandleWebViewUnavailable(WebSurfaceUnavailableEventArgs e)
    {
        ViewModel.IsLoading        = false;
        ViewModel.WebViewAvailable = false;
        ViewModel.RuntimeMissing   = e.RuntimeMissing;
        ViewModel.FailureMessage   = e.Message;
        PageChanged?.Invoke();

        // Proactively offer to open it externally; the in-tab panel stays as a fallback if they decline.
        if (CanOpenExternally())
            _shell.ShowConfirmation(
                "Browser unavailable",
                $"{ViewModel.FailureMessage} Open this page in your default web browser instead?",
                onConfirm: OpenInDefaultBrowser,
                onCancel: static () => { });
    }

    private void OpenExternal_Click(object sender, RoutedEventArgs e) => OpenInDefaultBrowser();

    private void InstallRuntime_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { _shell.ShowError($"Couldn't open the download page: {ex.Message}"); }
    }

    private void OpenInDefaultBrowser()
    {
        if (!CanOpenExternally())
        {
            _shell.ShowError("This page can't be opened in an external browser.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(ViewModel.NavigationUri.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _shell.ShowError($"Couldn't open your default browser: {ex.Message}");
        }
    }

    /// <summary>Only hand http(s)/file URLs to the OS shell — never arbitrary schemes from a .url file.</summary>
    private bool CanOpenExternally()
    {
        var scheme = ViewModel.NavigationUri.Scheme;
        return scheme == Uri.UriSchemeHttp
            || scheme == Uri.UriSchemeHttps
            || scheme == Uri.UriSchemeFile;
    }

    // ── IAirspaceContent ──────────────────────────────────────────────────

    /// <summary>
    /// Collapses the WebView2 while a shell overlay covers the page, and restores it after. The WebView's
    /// native HWND draws above all WPF content, so without this the overlay would be hidden behind it.
    /// </summary>
    void IAirspaceContent.SetCoveredByOverlay(bool covered) => ViewModel.IsCovered = covered;

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;

    /// <summary>Ends the embedded browser process when the tab genuinely closes.</summary>
    public void Dispose() => Surface.Dispose();
}
