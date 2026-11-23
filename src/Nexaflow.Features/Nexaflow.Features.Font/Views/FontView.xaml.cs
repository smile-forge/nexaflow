using System.Windows.Controls;
using Nexaflow.Features.Font.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Font.Views;

[ViewFor(typeof(FontViewModel))]
public partial class FontView : UserControl
{
    public FontView(FontViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += (_, _) => Focus();
    }
}
