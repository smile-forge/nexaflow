using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Common;
namespace Nexaflow.Core.Views;

/// <summary>
/// The content of a tab the shell could not fill. Left wordless it is indistinguishable from a feature that
/// loaded and drew nothing, so a caller that knows <em>why</em> the page is empty says so here.
/// </summary>
public partial class PlaceholderPage : UserControl, IPageView
{
    public PlaceholderPage() => InitializeComponent();

    public PlaceholderPage(string headline, string? detail = null) : this()
    {
        HeadlineText.Text = headline;
        if (string.IsNullOrWhiteSpace(detail)) return;
        DetailText.Text = detail;
        DetailText.Visibility = Visibility.Visible;
    }

    public IPageViewModel? ViewModel => null;
}
