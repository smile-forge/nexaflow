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

        // Hand the VM the empty host the shell injects its inline AI banner into (bottom of the console).
        vm.SetEngagementHost(EngagementHost);

        vm.ScrollRequested += (_, _) => ScrollToBottom();

        // Focus the value box when the env-var edit overlay opens; otherwise keep the terminal focused so
        // keystrokes flow to the shell.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.EnvEditVisible))
                Dispatcher.BeginInvoke(() =>
                    (vm.EnvEditVisible ? (UIElement)EnvEditValueBox : TerminalSurface).Focus());
        };

        Loaded += (_, _) => Dispatcher.BeginInvoke(() => TerminalSurface.Focus());

        // Dispose the PTY session when the tab is actually closed.
        if (vm.Tab is { } t)
            t.Closed += (_, _) => vm.Dispose();
    }

    private void ScrollToBottom()
        => Dispatcher.InvokeAsync(() => ConsoleScroller.ScrollToEnd(),
            System.Windows.Threading.DispatcherPriority.Loaded);

    // ── Terminal input: keystrokes go straight to the shell ───────────────

    private void Terminal_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        => TerminalSurface.Focus();

    private void Terminal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Overlays own the keyboard while shown.
        if (ViewModel.EnvEditVisible || ViewModel.EnvPickerVisible) return;

        // Ctrl+Shift+C copies (Ctrl+C alone is the shell interrupt, like every terminal).
        if (e.Key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            CopyTerminalText();
            e.Handled = true;
            return;
        }

        // Ctrl+Break is the forceful escalation: kill the wedged foreground command's process tree (keeping
        // the shell) when Ctrl+C didn't take. WPF surfaces Break as Key.Cancel.
        if (e.Key == Key.Cancel && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ViewModel.HardStop();
            e.Handled = true;
            return;
        }

        // Ctrl+V (and Ctrl+Shift+V) pastes, as modern terminals do, rather than sending a literal ^V.
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (Clipboard.ContainsText()) ViewModel.SendText(Clipboard.GetText());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            ViewModel.HandleEnter();
            e.Handled = true;
            return;
        }

        // Forward special keys (arrows, Backspace, Tab, Ctrl-combos…); printable keys fall through to
        // PreviewTextInput so the keyboard layout / IME is honoured.
        if (ViewModel.SendKey(e.Key, Keyboard.Modifiers))
            e.Handled = true;
    }

    private void Terminal_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (ViewModel.EnvEditVisible || ViewModel.EnvPickerVisible) return;
        if (string.IsNullOrEmpty(e.Text)) return;

        ViewModel.SendText(e.Text);
        e.Handled = true;
    }

    private void Terminal_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Terminal_Drop(object sender, DragEventArgs e)
    {
        var text = TerminalDropLogic.BuildInsertText(e.Data);
        if (text is not null)
        {
            ViewModel.SendText(text);
            e.Handled = true;
        }
    }

    private void CopyTerminal_Click(object sender, RoutedEventArgs e) => CopyTerminalText();

    private void CopyTerminalText()
    {
        var text = (string.Join("\n", ViewModel.Scrollback) + "\n" + ViewModel.ScreenText).Trim();
        try { Clipboard.SetText(text); } catch { /* clipboard busy */ }
    }

    private void PasteTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText()) ViewModel.SendText(Clipboard.GetText());
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
