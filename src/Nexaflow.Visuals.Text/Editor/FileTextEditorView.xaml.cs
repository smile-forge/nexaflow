using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;


namespace Nexaflow.Visuals.Text.Editor;

/// <summary>
/// The shared read-write editor control. Thin: the surface draws the view-model's document through the view-model's
/// <see cref="Markdown.Editing.ContentEditor"/>, so the caret, what is picked out and what a search found are the page's, and this only
/// shows or hides the line numbers, hands the minimap its ticks, and opens the command popups.
/// </summary>
public partial class FileTextEditorView : UserControl
{
    private readonly FileTextEditorViewModel _vm;

    public FileTextEditorView(FileTextEditorViewModel vm)
    {
        InitializeComponent();

        _vm = vm;
        DataContext = vm;

        ShowLineNumbers();
        vm.PropertyChanged += OnViewModelPropertyChanged;

        Loaded   += async (_, _) => await _vm.LoadAsync();
        Unloaded += (_, _) =>
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.Dispose();
        };
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FileTextEditorViewModel.MiniMapMarks):
                MiniMapCanvas.Marks = _vm.MiniMapMarks;
                break;
            case nameof(FileTextEditorViewModel.ShowLineNumbers):
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

    // Opens the footer's "Lines" command popup above the status-bar button.
    private void LineCommandsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Top;
            menu.IsOpen = true;
        }
    }

    // Opens a command group's dropdown below its toolbar button.
    private void CommandGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
}
