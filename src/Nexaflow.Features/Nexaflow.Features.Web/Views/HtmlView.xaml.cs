using Microsoft.Web.WebView2.Core;
using Nexaflow.Features.Common;
using Nexaflow.Features.Web.ViewModels;
using System;
using System.Windows.Controls;

namespace Nexaflow.Features.Web.Views;

public partial class HtmlView : UserControl, IPageView
{
    public HtmlViewModel ViewModel { get; }

    /// <summary>Raised when the URL or page title changes, so the tab title + breadcrumb can refresh.</summary>
    public event Action? PageChanged;

    public HtmlView(HtmlViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        Loaded += async (_, _) =>
        {
            await WebView.EnsureCoreWebView2Async();
            WebView.CoreWebView2.DocumentTitleChanged += (_, _) =>
            {
                ViewModel.PageTitle = WebView.CoreWebView2.DocumentTitle;
                PageChanged?.Invoke();
            };
            WebView.CoreWebView2.Navigate(viewModel.NavigationUri.ToString());
        };
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

    private void Back_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (WebView.CanGoBack) WebView.GoBack();
    }

    private void Forward_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (WebView.CanGoForward) WebView.GoForward();
    }

    private void Reload_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        WebView.Reload();
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;
}
