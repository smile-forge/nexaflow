using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Rtf.ViewModels;

namespace Nexaflow.Features.Rtf.Views;

/// <summary>
/// The rich-text page: the document drawn on its own paper by the shared surface, read-only, with the match strip a
/// search fills beside it. The view-model reads the file; this hands the surface's own search to it, sends the links the
/// document holds to the shell, and tells it when the page is off screen so a change to the file waits until it is back.
/// </summary>
public partial class RtfView : UserControl, IPageView
{
    private readonly RtfViewModel _vm;

    IPageViewModel? IPageView.ViewModel => _vm;

    public RtfView(RtfViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        // Set before the binding first draws the document, so a link written relative to it resolves from the start.
        Document.BaseDirectory = vm.BaseDirectory;
        DataContext = vm;

        // A link out of the document is the shell's to route. Unhandled leaves it to open the way any other link does.
        Document.LinkNavigate += (_, e) => e.Handled = vm.FollowLink(e.Url);

        // Search collaboration: the surface finds and lights up its own drawn words.
        vm.FindInDocument = Document.FindInRendered;
        vm.StepDocument = Document.StepSearch;
        vm.ClearDocument = Document.ClearSearch;
        vm.DocumentMarkPositions = Document.SearchMarkPositions;

        // Ctrl+wheel zooms the document. Preview, so it beats the surface's own scrolling; a plain wheel still scrolls.
        PreviewMouseWheel += (_, e) => e.Handled = vm.Zoom.TryWheel(e);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged += OnViewModelChanged;

        if (!_vm.IsLoaded) await _vm.LoadAsync();
        _vm.OnScreen(true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged -= OnViewModelChanged;
        _vm.OnScreen(false);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RtfViewModel.MiniMapMarks)) MiniMapCanvas.Marks = _vm.MiniMapMarks;
    }
}
