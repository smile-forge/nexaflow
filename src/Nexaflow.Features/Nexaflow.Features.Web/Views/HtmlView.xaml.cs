using Microsoft.Web.WebView2.Core;
using Nexaflow.Features.Common;
using Nexaflow.Features.Web.ViewModels;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.Web.Views;

public partial class HtmlView : UserControl, IPageView
{
    private readonly IShellServices _shell;
    private bool _initStarted;   // Loaded can fire more than once (e.g. when a tab is torn off to a new window)

    public HtmlViewModel ViewModel { get; }

    /// <summary>Raised when the URL or page title changes, so the tab title + breadcrumb can refresh.</summary>
    public event Action? PageChanged;

    public HtmlView(HtmlViewModel viewModel, IShellServices shell)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        _shell      = shell;
        DataContext = viewModel;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded fires again whenever the view is re-parented (tab moved to another window). Skip if an
        // init is already running, the browser is already live (preserve the user's browsing state instead
        // of re-navigating), or we've already fallen back (don't re-prompt).
        if (_initStarted || WebView.CoreWebView2 is not null || !ViewModel.WebViewAvailable) return;
        _initStarted = true;

        try
        {
            // The default user-data folder is created next to the executable; under an installed build
            // that's Program Files (read-only for a standard user) and init throws. Pin it to a per-user
            // writable location — LocalAppData, since a browser cache is large and machine-specific.
            WebView.CreationProperties = new Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties
            {
                UserDataFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Smile", "nexaflow", "WebView2"),
            };
            await WebView.EnsureCoreWebView2Async();
            var core = WebView.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 initialised without a CoreWebView2.");

            core.DocumentTitleChanged += (_, _) =>
            {
                ViewModel.PageTitle = core.DocumentTitle;
                PageChanged?.Invoke();
            };
            core.Navigate(ViewModel.NavigationUri.ToString());
        }
        catch (Exception ex)
        {
            // The embedded Edge WebView2 control can't be created on this machine — most often because
            // the Evergreen WebView2 runtime isn't installed. Don't let the async-void handler crash the
            // app; fall back to the in-tab panel and offer to open the page in the default browser.
            HandleWebViewUnavailable(ex);
        }
        finally
        {
            _initStarted = false;   // allow a future re-parent to re-init if CoreWebView2 was torn down
        }
    }

    /// <summary>Navigates the embedded browser to <paramref name="url"/> (used by breadcrumb crumbs).</summary>
    public void NavigateTo(string url)
    {
        if (WebView.CoreWebView2 is not null)
            WebView.CoreWebView2.Navigate(url);
    }

    private void WebView_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
    {
        ViewModel.IsLoading  = true;
        ViewModel.CurrentUrl = e.Uri;
        ViewModel.PageTitle  = string.Empty;   // old title no longer applies; URL-form shows until the new title arrives
        PageChanged?.Invoke();
    }

    private void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        ViewModel.IsLoading  = false;
        ViewModel.CurrentUrl = WebView.Source?.ToString() ?? ViewModel.CurrentUrl;
        ViewModel.PageTitle  = WebView.CoreWebView2?.DocumentTitle ?? ViewModel.PageTitle;
        PageChanged?.Invoke();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CanGoBack) WebView.GoBack();
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CanGoForward) WebView.GoForward();
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        WebView.Reload();
    }

    // ── WebView2-unavailable fallback ─────────────────────────────────────

    private void HandleWebViewUnavailable(Exception ex)
    {
        ViewModel.IsLoading        = false;
        ViewModel.WebViewAvailable = false;
        ViewModel.RuntimeMissing   = ex is WebView2RuntimeNotFoundException;
        ViewModel.FailureMessage   = ViewModel.RuntimeMissing
            ? "The Microsoft Edge WebView2 runtime isn't installed on this PC, so the page can't be shown here."
            : "The embedded browser couldn't be started on this PC.";
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

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;
}
