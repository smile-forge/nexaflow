using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Visuals.Terminal.Views;

public partial class TerminalView : UserControl, IPageView
{
    public TerminalViewModel ViewModel { get; }

    private Point _dragStart;

    public TerminalView(TerminalViewModel vm)
    {
        InitializeComponent();
        ViewModel   = vm;
        DataContext = vm;

        vm.ScrollRequested += (_, _) => ScrollToBottom();
        vm.Entries.CollectionChanged += (_, _) => UpdateEmptyState();

        // Focus the value box when the env-var edit overlay opens.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.EnvEditVisible) && vm.EnvEditVisible)
                Dispatcher.BeginInvoke(() => EnvEditValueBox.Focus());
        };

        // Dispose the PTY session when the tab is actually closed — not on mere tab switches,
        // which also trigger Unloaded because the shell swaps CurrentPage in the content presenter.
        if (vm.Tab is { } t)
            t.Closed += (_, _) => vm.Dispose();

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        bool empty = ViewModel.Entries.Count == 0;
        EmptyHint.Visibility       = empty ? Visibility.Visible  : Visibility.Collapsed;
        ConsoleScroller.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ScrollToBottom()
    {
        Dispatcher.InvokeAsync(() => ConsoleScroller.ScrollToEnd(),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ── Files panel: navigate on double-click, drag a file out as its path ────

    private void FilesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is TerminalFsEntry entry)
            ViewModel.NavigateInto(entry);
    }

    private void FilesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _dragStart = e.GetPosition(null);

    private void FilesList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var diff = _dragStart - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if ((sender as ListBox)?.SelectedItem is not TerminalFsEntry entry) return;

        var data = new DataObject(DataFormats.FileDrop, new[] { entry.FullPath });
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
    }

    // ── Console output: accept a dropped file as an inserted path ─────────────

    private void Output_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Output_Drop(object sender, DragEventArgs e)
    {
        var text = TerminalDropLogic.BuildInsertText(e.Data);
        if (text is not null)
        {
            ViewModel.InsertIntoChatInput(text);
            e.Handled = true;
        }
    }

    // ── Env-var edit overlay: Enter saves, Escape cancels ─────────────────────

    private void EnvEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  { ViewModel.ConfirmEnvEditCommand.Execute(null); e.Handled = true; }
        if (e.Key == Key.Escape) { ViewModel.CancelEnvEditCommand.Execute(null);  e.Handled = true; }
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;

    void IPageView.Reinitialize(Dictionary<string, string> pageParams)
    {
        ScrollToBottom();
        var newPath = pageParams.GetValueOrDefault("path");
        var envName = pageParams.GetValueOrDefault("env");
        if (newPath is not null || envName is not null)
            ViewModel.ApplyParams(newPath, envName);
    }
}
