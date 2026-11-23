using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Viewing;
namespace Nexaflow.Core.Views;

/// <summary>
/// Draws a <see cref="PlaceholderViewModel"/>: a tab that only says why it is empty.
/// </summary>
[ViewFor(typeof(PlaceholderViewModel))]
public partial class PlaceholderPage : UserControl
{
    public PlaceholderPage() => InitializeComponent();

    public PlaceholderPage(PlaceholderViewModel model) : this()
    {
        HeadlineText.Text = model.Headline;
        if (string.IsNullOrWhiteSpace(model.Detail)) return;
        DetailText.Text = model.Detail;
        DetailText.Visibility = Visibility.Visible;
    }
}
