using Nexaflow.Features.Common;
using Nexaflow.Features.Hex.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Nexaflow.Features.Hex.Views;

public partial class HexView : UserControl, IPageView
{
    private readonly HexViewModel _vm;

    IPageViewModel? IPageView.ViewModel => _vm;

    public HexView(HexViewModel vm)
    {
        InitializeComponent();
        _vm         = vm;
        DataContext = vm;

        // Wire up panels
        HexPanel.Attach(vm);
        EvalPanel.Attach(vm);

        // Scrollbar wiring
        vm.PropertyChanged   += OnVmPropertyChanged;
        MainScrollBar.Scroll += OnScrollBarScroll;

        // Keyboard shortcuts
        InputBindings.Add(new KeyBinding(vm.UndoCommand, Key.Z, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.RedoCommand, Key.Y, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.SaveCommand, Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => GotoBox.Focus()),
            Key.G, ModifierKeys.Control));

        // Encoding combo wiring (not data-bound to avoid converter complexity)
        EncodingCombo.SelectionChanged += OnEncodingChanged;

        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SyncScrollBar();
        HexPanel.Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged   -= OnVmPropertyChanged;
        MainScrollBar.Scroll  -= OnScrollBarScroll;
        EncodingCombo.SelectionChanged -= OnEncodingChanged;
        _vm.Dispose();
    }

    // ── Scrollbar sync ────────────────────────────────────────────────────────

    private void SyncScrollBar()
    {
        long max      = Math.Max(0, _vm.TotalRows - _vm.VisibleRowCount);
        MainScrollBar.Maximum   = max;
        MainScrollBar.LargeChange = _vm.VisibleRowCount;
        MainScrollBar.ViewportSize = _vm.VisibleRowCount;
        MainScrollBar.Value     = Math.Min(_vm.TopRow, max);
    }

    private void OnScrollBarScroll(object sender, ScrollEventArgs e)
    {
        _vm.TopRow = (long)e.NewValue;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HexViewModel.TopRow):
                if (Math.Abs(MainScrollBar.Value - _vm.TopRow) > 0.5)
                    MainScrollBar.Value = _vm.TopRow;
                break;

            case nameof(HexViewModel.TotalRows):
            case nameof(HexViewModel.VisibleRowCount):
                SyncScrollBar();
                break;

            case nameof(HexViewModel.ShowEvaluatePane):
                EvalColumn.Width = _vm.ShowEvaluatePane
                    ? new GridLength(200)
                    : new GridLength(0);
                break;
        }
    }

    // ── Encoding combo ────────────────────────────────────────────────────────

    private void OnEncodingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        _vm.Encoding = EncodingCombo.SelectedIndex switch
        {
            1 => HexEncoding.Ascii,
            2 => HexEncoding.Utf8,
            3 => HexEncoding.Utf16LE,
            _ => HexEncoding.Auto,
        };
    }

    // ── IPageView ─────────────────────────────────────────────────────────────

    void IPageView.Reinitialize(Dictionary<string, string> pageParams) { }

    // ── Simple relay command helper for keybindings ───────────────────────────

    private sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? _) => true;
        public void Execute(object? _)    => execute();
    }
}
