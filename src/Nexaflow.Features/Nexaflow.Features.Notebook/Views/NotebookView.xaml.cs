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
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (!_vm.IsLoaded) await _vm.LoadAsync();
    }
}
