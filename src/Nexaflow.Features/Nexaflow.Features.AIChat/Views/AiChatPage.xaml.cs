using System.Windows.Controls;
using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.AIChat.Views;

/// <summary>
/// Conversation browser page: a list of conversations plus the structured analysis summary of the
/// selected one, with an Open button that launches a ConversationView tab.
/// </summary>
[ViewFor(typeof(AiChatViewModel))]
public partial class AiChatPage : UserControl
{
    public AiChatPage(AiChatViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
