using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Notebook.ViewModels;

namespace Nexaflow.Features.Notebook.Views;

/// <summary>The Notebook page: a vertical list of cells (markdown rendered, code syntax-highlighted) with a
/// code-structure outline on the right. Loads the notebook when shown.</summary>
public partial class NotebookView : UserControl, IPageView
{
    private readonly NotebookViewModel _vm;

    IPageViewModel? IPageView.ViewModel => _vm;

    public NotebookView(NotebookViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += OnLoaded;
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (!_vm.IsLoaded) await _vm.LoadAsync();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotebookViewModel.ScrollToCellIndex)) ScrollToSearchHit();
    }

    /// <summary>Brings the search's current cell into view. The cell list isn't virtualized, so a container
    /// exists for every cell — but it is generated on layout, hence the Loaded-priority post rather than
    /// reading it straight away.</summary>
    private void ScrollToSearchHit()
    {
        var index = _vm.ScrollToCellIndex;
        if (index < 0) return;

        Dispatcher.InvokeAsync(() =>
        {
            if (CellList.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
                container.BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
