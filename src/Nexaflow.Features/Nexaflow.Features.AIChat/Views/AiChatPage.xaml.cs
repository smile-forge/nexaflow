using System.Windows.Controls;
using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat.Views;

/// <summary>
/// Conversation browser page: a list of conversations plus the structured analysis summary of the
/// selected one, with an Open button that launches a ConversationView tab.
/// </summary>
public partial class AiChatPage : UserControl, IPageView
{
    public AiChatViewModel ViewModel { get; }
    IPageViewModel? IPageView.ViewModel => ViewModel;

    public AiChatPage(IAIService aiService, IShellServices shell, AiChatConfig config, Nexaflow.Features.Common.Page ownerPage)
    {
        InitializeComponent();
        ViewModel   = new AiChatViewModel(aiService, shell, config, ownerPage);
        DataContext = ViewModel;
    }

    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        // Re-resolve the list and selected analysis so completed background analyses appear.
        _ = ViewModel.RefreshAsync();
    }
}
