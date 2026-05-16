using Microsoft.Web.WebView2.Core;
using Nexaflow.Features.Common;
using Nexaflow.Features.Web.ViewModels;
using System.Windows.Controls;

namespace Nexaflow.Features.Web.Views;

public partial class HtmlView : UserControl, IPageView
{
    public HtmlViewModel ViewModel { get; }

    public HtmlView(HtmlViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        Loaded += async (_, _) =>
        {
            await WebView.EnsureCoreWebView2Async();
            WebView.CoreWebView2.Navigate(viewModel.NavigationUri.ToString());
        };
    }

    private void WebView_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
    {
        ViewModel.IsLoading  = true;
        ViewModel.CurrentUrl = e.Uri;
    }

    private void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        ViewModel.IsLoading  = false;
        ViewModel.CurrentUrl = WebView.Source?.ToString() ?? ViewModel.CurrentUrl;
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

    object? IPageView.ViewModel => ViewModel;

    string IPageView.GetContext()
    {
        var loading = ViewModel.IsLoading ? " (loading)" : string.Empty;
        return $"Web view: '{ViewModel.CurrentUrl}'{loading}.";
    }

    IReadOnlyList<ActionDescriptor> IPageView.GetAvailableActions() => [];
}
