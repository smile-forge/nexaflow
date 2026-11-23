using System.Collections.Specialized;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Serial.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Serial.Views;

[ViewFor(typeof(SerialViewModel))]
public partial class SerialView : UserControl
{
    readonly SerialViewModel _vm;

    public SerialView(SerialViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _vm = viewModel;
        // The transcript follows what arrives, as a terminal does, unless the reader has scrolled up to look back.
        _vm.Transcript.CollectionChanged += OnTranscriptChanged;
    }

    void OnTranscriptChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || _vm.Transcript.Count == 0) return;
        if (Following()) TranscriptList.ScrollIntoView(_vm.Transcript[^1]);
    }

    ScrollViewer? _scroller;

    bool Following()
    {
        _scroller ??= FindScroller(TranscriptList);
        return _scroller is null || _scroller.VerticalOffset >= _scroller.ScrollableHeight - 2;
    }

    static ScrollViewer? FindScroller(System.Windows.DependencyObject root)
    {
        if (root is ScrollViewer found) return found;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScroller(System.Windows.Media.VisualTreeHelper.GetChild(root, i)) is { } scroller) return scroller;
        return null;
    }
}
