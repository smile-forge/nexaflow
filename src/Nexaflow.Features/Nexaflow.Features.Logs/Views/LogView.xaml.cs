using Nexaflow.Features.Logs.ViewModels;
using Nexaflow.Visuals.Common.Viewing;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.Logs.Views;

/// <summary>
/// The log viewer's page. Thin: the surface draws the view-model's log through the view-model's editor, so what is marked on its lines,
/// where the page stands and what a search found are the page's; this only puts the line-picking margin beside it and draws it again when
/// the lines picked out change.
/// </summary>
[ViewFor(typeof(LogViewModel))]
public partial class LogView : UserControl
{
    private readonly LogViewModel        _vm;
    private readonly LineSelectionMargin _selectionMargin;

    public LogView(LogViewModel vm)
    {
        InitializeComponent();

        _vm         = vm;
        DataContext = vm;

        _selectionMargin = new LineSelectionMargin(vm);
        Surface.LeadingMargins.Add(_selectionMargin);

        vm.PropertyChanged += OnVmPropertyChanged;

        Loaded   += async (_, _) => await vm.LoadAsync(CancellationToken.None);
        Unloaded += OnUnloaded;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LogViewModel.SelectedLineNumbers) or nameof(LogViewModel.FirstLineNumber))
            _selectionMargin.InvalidateVisual();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.Dispose();
    }
}
