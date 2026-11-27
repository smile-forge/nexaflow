using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using Nexaflow.Features.Text.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.Text.Views;

/// <summary>
/// The Text viewer's page. Thin: the surface draws the view-model's document through the view-model's editor, so the caret, what is
/// picked out, where the page stands and what a search found are the page's; this only shows or hides the line numbers, hands the
/// minimap its ticks, puts the caret in the find box when the bar opens, and zooms on Ctrl+wheel.
/// </summary>
[ViewFor(typeof(TextViewModel))]
public partial class TextView : UserControl
{
    private readonly TextViewModel _vm;

    public TextView(TextViewModel vm)
    {
        InitializeComponent();

        _vm = vm;
        DataContext = vm;

        ShowLineNumbers();
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.FindBarFocusRequested += FocusFindBox;

        Loaded += async (_, _) => await vm.LoadAsync(CancellationToken.None);
        Unloaded += OnUnloaded;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TextViewModel.MiniMapMarks):
                MiniMapCanvas.Marks = _vm.MiniMapMarks;
                break;

            case nameof(TextViewModel.ShowLineNumbers):
                ShowLineNumbers();
                break;
        }
    }

    /// <summary>The line numbers in the surface's margin, or not, as the toolbar's toggle says — the margin is the surface's, not bound.</summary>
    private void ShowLineNumbers()
    {
        var shown = Surface.LeadingMargins.Contains(LineNumbers);

        if (_vm.ShowLineNumbers && !shown) Surface.LeadingMargins.Add(LineNumbers);
        else if (!_vm.ShowLineNumbers && shown) Surface.LeadingMargins.Remove(LineNumbers);
    }

    private void FocusFindBox() =>
        Dispatcher.InvokeAsync(() => { FindBox.Focus(); FindBox.SelectAll(); }, DispatcherPriority.Input);

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                    _vm.FindPreviousCommand.Execute(null);
                else
                    _vm.FindNextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                _vm.CloseFindBarCommand.Execute(null);
                Surface.Focus();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Ctrl+wheel zooms the page rather than scrolling it.</summary>
    private void Surface_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => e.Handled = _vm.Zoom.TryWheel(e);

    private void FindMenu_Click(object sender, RoutedEventArgs e) => FindMenuPopup.IsOpen = true;

    private void FindMenuFind_Click(object sender, RoutedEventArgs e)
    {
        FindMenuPopup.IsOpen = false;
        _vm.OpenFindCommand.Execute(null);
    }

    private void FindMenuReplace_Click(object sender, RoutedEventArgs e)
    {
        FindMenuPopup.IsOpen = false;
        _vm.OpenReplaceCommand.Execute(null);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.FindBarFocusRequested -= FocusFindBox;
        _vm.Dispose();
    }
}
