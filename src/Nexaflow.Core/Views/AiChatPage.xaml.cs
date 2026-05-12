using System.Windows;
using System.Windows.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Core.Views;

public partial class AiChatPage : UserControl, IRefreshable
{
    public AiChatViewModel ViewModel { get; }

    public AiChatPage(AiChatViewModel vm)
    {
        InitializeComponent();
        ViewModel   = vm;
        DataContext = vm;

        vm.ScrollRequested += (_, _) => ScrollToBottom();
        vm.Messages.CollectionChanged += (_, _) => UpdateEmptyState();

        // Update breadcrumb title when active conversation changes
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AiChatViewModel.ActiveConversation))
                UpdateBreadcrumbTitle();
        };

        UpdateEmptyState();
    }

    // Called by the shell to update breadcrumb when this tab is active
    public event Action<string>? TitleChanged;

    private void UpdateBreadcrumbTitle()
    {
        var title = ViewModel.ActiveConversation?.Title ?? "AI Chat";
        TitleChanged?.Invoke(title);
    }

    private void UpdateEmptyState()
    {
        bool empty = ViewModel.Messages.Count == 0;
        EmptyState.Visibility  = empty ? Visibility.Visible  : Visibility.Collapsed;
        MessageList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ScrollToBottom()
    {
        Dispatcher.InvokeAsync(() => MessageScroller.ScrollToEnd(),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ── IRefreshable ──────────────────────────────────────────────────────
    public void Refresh() => ScrollToBottom();
}
