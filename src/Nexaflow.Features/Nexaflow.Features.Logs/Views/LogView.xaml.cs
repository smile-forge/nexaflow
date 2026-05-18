using ICSharpCode.AvalonEdit.Rendering;
using Nexaflow.Features.Common;
using Nexaflow.Features.Logs.Parsing;
using Nexaflow.Features.Logs.Rendering;
using Nexaflow.Features.Logs.ViewModels;
using System.IO;
using System.Windows.Input;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Nexaflow.Features.Logs.Views;

public partial class LogView : UserControl, IPageView
{
    private readonly LogViewModel                  _vm;
    private readonly LogLevelColorizer             _levelColorizer;
    private readonly LogFilterColorizer            _filterColorizer;
    private readonly CustomTermHighlightRenderer   _customTermRenderer;
    private readonly SelectedLinesHighlightRenderer _selectedLinesRenderer;
    private readonly LineSelectionMargin           _selectionMargin;

    public LogView(LogViewModel vm)
    {
        InitializeComponent();

        _vm         = vm;
        DataContext = vm;

        // Wire AvalonEdit
        Editor.Document = vm.Document;

        // Colourizers (line transformers)
        _levelColorizer  = new LogLevelColorizer  { Parser = vm.ActiveParser };
        _filterColorizer = new LogFilterColorizer { Parser = vm.ActiveParser };
        Editor.TextArea.TextView.LineTransformers.Add(_levelColorizer);
        Editor.TextArea.TextView.LineTransformers.Add(_filterColorizer);

        // Seed the faded brush from WPF resources if available
        if (TryFindResource("TextMutedBrush") is Brush mutedBrush)
            _filterColorizer.FadedBrush = mutedBrush;

        // Custom term renderer (background renderer)
        _customTermRenderer = new CustomTermHighlightRenderer();
        Editor.TextArea.TextView.BackgroundRenderers.Add(_customTermRenderer);

        // Selected-lines renderer — draws persistent highlight behind selected lines
        _selectedLinesRenderer = new SelectedLinesHighlightRenderer();
        Editor.TextArea.TextView.BackgroundRenderers.Add(_selectedLinesRenderer);

        // Line selection margin — inserted leftmost so it sits beside line numbers
        _selectionMargin = new LineSelectionMargin(
            vm.ToggleLineSelection,
            () => vm.SelectedLineNumbers);
        Editor.TextArea.LeftMargins.Insert(0, _selectionMargin);

        vm.PropertyChanged          += OnVmPropertyChanged;
        vm.ScrollToBottomRequested  += (_, _) => Editor.ScrollToEnd();

        Loaded   += async (_, _) => await vm.LoadAsync(CancellationToken.None);
        Unloaded += OnUnloaded;
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => _vm;

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LogViewModel.HighlightFatal):
            case nameof(LogViewModel.HighlightError):
            case nameof(LogViewModel.HighlightWarning):
            case nameof(LogViewModel.HighlightInfo):
            case nameof(LogViewModel.HighlightDebug):
                UpdateLevelColorizer();
                break;

            case nameof(LogViewModel.ActiveParser):
                _levelColorizer.Parser  = _vm.ActiveParser;
                _filterColorizer.Parser = _vm.ActiveParser;
                Editor.TextArea.TextView.Redraw();
                break;

            case nameof(LogViewModel.ActiveFilter):
            case nameof(LogViewModel.FilterStart):
            case nameof(LogViewModel.FilterEnd):
                _filterColorizer.ActiveFilter    = _vm.ActiveFilter;
                _filterColorizer.TimestampRange  = (_vm.FilterStart, _vm.FilterEnd);
                _filterColorizer.Parser          = _vm.ActiveParser;
                Editor.TextArea.TextView.Redraw();
                break;

            case nameof(LogViewModel.CustomTermHighlights):
                _customTermRenderer.Highlights = _vm.CustomTermHighlights;
                Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
                break;

            case nameof(LogViewModel.SelectedLineNumbers):
                _selectedLinesRenderer.SelectedLines = _vm.SelectedLineNumbers;
                Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
                _selectionMargin.InvalidateVisual();
                break;

            case nameof(LogViewModel.ScrollToOffset):
                if (_vm.ScrollToOffset >= 0)
                    ScrollToDocumentOffset(_vm.ScrollToOffset);
                break;
        }
    }

    private void UpdateLevelColorizer()
    {
        _levelColorizer.Parser = _vm.ActiveParser;
        var levels = new HashSet<LogLevel>();
        if (_vm.HighlightFatal)   levels.Add(LogLevel.Fatal);
        if (_vm.HighlightError)   levels.Add(LogLevel.Error);
        if (_vm.HighlightWarning) levels.Add(LogLevel.Warning);
        if (_vm.HighlightInfo)    levels.Add(LogLevel.Info);
        if (_vm.HighlightDebug)   levels.Add(LogLevel.Debug);
        _levelColorizer.EnabledLevels = levels;
        Editor.TextArea.TextView.Redraw();
    }

    private void ScrollToDocumentOffset(int offset)
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

            var lineHeight = Editor.TextArea.TextView.DefaultLineHeight;
            var targetY    = (line.LineNumber - 1) * lineHeight;
            var centred    = Math.Max(0, targetY - sv.ViewportHeight / 2.0);
            sv.ScrollToVerticalOffset(centred);
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.Dispose();
    }

    private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool>? predicate = null)
        where T : DependencyObject
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
}
