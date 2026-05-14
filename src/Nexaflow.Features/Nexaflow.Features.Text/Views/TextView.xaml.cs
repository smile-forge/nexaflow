using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nexaflow.Features.Text.Rendering;
using Nexaflow.Features.Text.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Features.Text.Views;

public partial class TextView : UserControl
{
    private readonly TextViewModel            _vm;
    private readonly SearchHighlightRenderer  _renderer = new();

    public TextView(TextViewModel vm)
    {
        InitializeComponent();

        _vm        = vm;
        DataContext = vm;

        // Wire AvalonEdit after init — document is owned by the ViewModel
        Editor.Document = vm.Document;
        Editor.TextArea.TextView.BackgroundRenderers.Add(_renderer);
        Editor.ShowLineNumbers = vm.ShowLineNumbers;
        Editor.WordWrap        = vm.WordWrap;

        // Clipboard buttons delegate to AvalonEdit commands
        CutButton.Click   += (_, _) => ApplicationCommands.Cut.Execute(null,   Editor.TextArea);
        CopyButton.Click  += (_, _) => ApplicationCommands.Copy.Execute(null,  Editor.TextArea);
        PasteButton.Click += (_, _) => ApplicationCommands.Paste.Execute(null, Editor.TextArea);

        vm.PropertyChanged += OnVmPropertyChanged;
        Editor.TextArea.Caret.PositionChanged += (_, _) => vm.CurrentCaretOffset = Editor.CaretOffset;
        Unloaded += OnUnloaded;

        Loaded += async (_, _) =>
        {
            await vm.LoadAsync(CancellationToken.None);

            // Keep minimap bottom padding in sync with horizontal scrollbar visibility.
            // When the horizontal scrollbar is shown it raises the bottom of the vertical
            // scrollbar's thumb-travel region, so the minimap marks must shift up too.
            var sv = FindDescendant<ScrollViewer>(Editor);
            if (sv is not null)
            {
                SyncMinimapBottomPadding(sv);
                sv.ScrollChanged += (_, _) => SyncMinimapBottomPadding(sv);
            }
        };
    }

    private void SyncMinimapBottomPadding(ScrollViewer sv)
    {
        var hBarHeight = sv.ComputedHorizontalScrollBarVisibility == Visibility.Visible
            ? FindDescendant<ScrollBar>(Editor, sb => sb.Orientation == Orientation.Horizontal)?.ActualHeight ?? 6.0
            : 0.0;
        MiniMapCanvas.BottomPadding = 8.0 + hBarHeight;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TextViewModel.SearchHighlights):
                _renderer.Highlights = _vm.SearchHighlights;
                Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
                break;

            case nameof(TextViewModel.ScrollToOffset):
                if (_vm.ScrollToOffset >= 0)
                    ScrollToOffset(_vm.ScrollToOffset);
                break;

            case nameof(TextViewModel.MiniMapMarks):
                MiniMapCanvas.Marks = _vm.MiniMapMarks;
                break;

            case nameof(TextViewModel.ShowLineNumbers):
                Editor.ShowLineNumbers = _vm.ShowLineNumbers;
                break;

            case nameof(TextViewModel.WordWrap):
                Editor.WordWrap = _vm.WordWrap;
                break;
        }
    }

    private void ScrollToOffset(int offset)
    {
        if (Editor.Document is null) return;
        if (offset < 0 || offset >= Editor.Document.TextLength) return;

        var line = Editor.Document.GetLineByOffset(offset);
        Editor.CaretOffset = offset;

        Dispatcher.InvokeAsync(() =>
        {
            Editor.ScrollToLine(line.LineNumber);

            var sv = FindDescendant<ScrollViewer>(Editor);
            if (sv is null) return;

            // After ScrollToLine the target line is at or near top; pull it to centre.
            var lineHeight  = Editor.TextArea.TextView.DefaultLineHeight;
            var targetY     = (line.LineNumber - 1) * lineHeight;
            var centred     = Math.Max(0, targetY - sv.ViewportHeight / 2.0);
            sv.ScrollToVerticalOffset(centred);
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && (predicate is null || predicate(match))) return match;
            var found = FindDescendant<T>(child, predicate);
            if (found is not null) return found;
        }
        return null;
    }

    private async void Editor_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_vm.IsLargeFile) return;

        var sv = FindDescendant<ScrollViewer>(Editor);
        if (sv is null) return;

        var lineHeight = Editor.TextArea.TextView.DefaultLineHeight;
        if (lineHeight <= 0) return;

        // 1-based line number at the bottom of the current viewport
        var bottomLine = (int)Math.Ceiling((sv.VerticalOffset + sv.ViewportHeight) / lineHeight);

        // Load chunks until real content covers the viewport (plus look-ahead buffer)
        if (bottomLine >= _vm.LoadedLineCount)
            await _vm.EnsureContentLoadedUpToLineAsync(bottomLine);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.Dispose();
    }
}
