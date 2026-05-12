using System.Windows.Controls;
using Nexaflow.Features.Common;
namespace Nexaflow.Core.Views;
public partial class PlaceholderPage : UserControl, IRefreshable
{
    public PlaceholderPage() => InitializeComponent();
    public void Refresh() { /* nothing to refresh */ }
}
