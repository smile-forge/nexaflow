using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.AIChat.Views;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.AIChat;

/// <summary>
/// Page registration for the single-conversation page. Distinct from the AIChat
/// page (which lists conversations). Opened by "Continue as Conversation" on the
/// response overlay and by clicking a row in the AIChat list.
/// </summary>
public sealed class ConversationTabRegistration : IPageRegistration
{
    public static string StaticPageKind => "Conversation";
    public string PageKind => StaticPageKind;

    private readonly IAIService     _aiService;
    private readonly IShellServices _shell;
    private readonly AiChatConfig   _config;

    public ConversationTabRegistration(IAIService aiService, IShellServices shell, AiChatConfig config)
    {
        _aiService = aiService;
        _shell     = shell;
        _config    = config;
    }

    public Page CreatePage(Dictionary<string, string>? pageParams = null)
    {
        var tab = new Page
        {
            Title      = "Conversation",
            Icon       = "💬",
            PageKind   = StaticPageKind,
            PageParams = pageParams,
        };

        tab.ContentFactory = () =>
        {
            var vm = new ConversationViewModel(_aiService, _shell, _config, tab);
            var view = new ConversationView(vm);
            if (pageParams is not null &&
                pageParams.TryGetValue("conversationId", out var convId))
            {
                _ = vm.LoadAsync(convId);
            }
            return view;
        };

        return tab;
    }
}
