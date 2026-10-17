using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.UsbProbe.ViewModels;

namespace Nexaflow.Features.UsbProbe.Views;

public partial class UsbDeviceView : UserControl, IPageView
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

    public IPageViewModel? ViewModel => _vm;

    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        if (pageParams.TryGetValue(UsbDevicePageRegistration.DeviceParam, out var device)) _vm.Retarget(device);
    }
}
