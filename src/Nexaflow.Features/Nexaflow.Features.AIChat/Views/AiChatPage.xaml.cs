using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat.Views;



public partial class AiChatPage : UserControl, IPageView
{
    public AiChatViewModel ViewModel { get; }
    IPageViewModel? IPageView.ViewModel => ViewModel;

    public AiChatPage(IAIService aIService)
    {
        InitializeComponent();
        ViewModel = new AiChatViewModel(aIService);
        DataContext = ViewModel;

        ViewModel.ScrollRequested += (_, _) => ScrollToBottom();
        ViewModel.Messages.CollectionChanged += (_, _) => UpdateEmptyState();
        // Update breadcrumb title when active conversation changes
        ViewModel.PropertyChanged += (_, e) =>
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

    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        ScrollToBottom();
        if (pageParams.TryGetValue("input",  out var input) &&
            pageParams.TryGetValue("output", out var output))
            _ = ViewModel.AddExchangeAsync(input, output);
    }
}
