using System.Collections.Generic;
using System.Windows.Controls;
using Nexaflow.Features.Common;
namespace Nexaflow.Core.Views;
public partial class PlaceholderPage : UserControl, IPageView
{
    public PlaceholderPage() => InitializeComponent();
    public object? ViewModel => null;
    public string GetContext() => string.Empty;
    public IReadOnlyList<ActionDescriptor> GetAvailableActions() => [];
}
