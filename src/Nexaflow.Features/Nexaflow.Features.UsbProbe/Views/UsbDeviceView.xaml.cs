using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.UsbProbe.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.UsbProbe.Views;

[ViewFor(typeof(UsbDeviceViewModel))]
public partial class UsbDeviceView : UserControl
{
    readonly UsbDeviceViewModel _vm;

    public UsbDeviceView(UsbDeviceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _vm = viewModel;
        // Each tool provider draws its own tools: its templates join this page's resources before its first tile.
        _vm.TemplatesNeeded += source => Resources.MergedDictionaries.Add(new ResourceDictionary { Source = source });
        Loaded += async (_, _) =>
        {
            _vm.SetPageShown(true);
            await _vm.StartAsync();
        };
        Unloaded += (_, _) => _vm.SetPageShown(false);
    }
}
