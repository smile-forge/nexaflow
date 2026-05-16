using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Features.Console.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Console.Views;

public partial class ConsoleView : UserControl, IPageView, IRefreshable
{
    public ConsoleViewModel ViewModel { get; }

    public ConsoleView(ConsoleViewModel vm)
    {
        InitializeComponent();
        ViewModel   = vm;
        DataContext = vm;

        vm.ScrollRequested += (_, _) => ScrollToBottom();
        vm.Entries.CollectionChanged += (_, _) => UpdateEmptyState();

        // Focus the prompt textbox whenever the overlay becomes visible
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.InputPromptVisible) && vm.InputPromptVisible)
                InputPromptTextBox.Focus();
        };

        // Dispose the PTY session when this control is unloaded (tab closed)
        Unloaded += (_, _) => vm.Dispose();

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

    // ── Input-prompt handlers (mirrors FileSystemView behaviour) ─────────

    private void InputPromptTextBox_GotFocus(object sender, RoutedEventArgs e)
        => InputPromptTextBox.SelectAll();

    private void InputPromptTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  { ViewModel.ConfirmInputPromptCommand.Execute(null); e.Handled = true; }
        if (e.Key == Key.Escape) { ViewModel.CancelInputPromptCommand.Execute(null);  e.Handled = true; }
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    object? IPageView.ViewModel => ViewModel;

    string IPageView.GetContext() =>
        $"Terminal: '{ViewModel.CurrentPath}'.";

    IReadOnlyList<ActionDescriptor> IPageView.GetAvailableActions() => [];

    IContext? IPageView.GetContextObject()
    {
        if (string.IsNullOrEmpty(ViewModel.CurrentPath)) return null;
        return new FileSystemContext
        {
            RootPath      = ViewModel.CurrentPath,
            CurrentPath   = ViewModel.CurrentPath,
            SelectedItems = []
        };
    }

    // ── IRefreshable ──────────────────────────────────────────────────────
    public void Refresh() => ScrollToBottom();
}
