using System.Windows.Controls;
using Nexaflow.Features.Common;
namespace Nexaflow.Core.Views;
public partial class PlaceholderPage : UserControl, IPageView
{
    public PlaceholderPage() => InitializeComponent();
    public IPageViewModel? ViewModel => null;
}
