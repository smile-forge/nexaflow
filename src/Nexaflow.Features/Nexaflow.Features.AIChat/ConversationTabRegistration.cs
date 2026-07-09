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

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("conversationId", "Id of an existing conversation to open. Omit to start a new one.", Required: false),
        new("initialPrompt", "A first user message to auto-send once the conversation (and its context) is ready.", Required: false),
    ];

    private readonly IAIService     _aiService;
    private readonly IShellServices _shell;
    private readonly AiChatConfig   _config;

    public ConversationTabRegistration(IAIService aiService, IShellServices shell, AiChatConfig config)
    {
        _aiService = aiService;
        _shell     = shell;
        _config    = config;
    }

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
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

            var initialPrompt = pageParams?.GetValueOrDefault("initialPrompt");
            var load = pageParams is not null && pageParams.TryGetValue("conversationId", out var convId)
                ? vm.LoadAsync(convId)
                : vm.StartNew();
            _ = InitAsync(load, vm, initialPrompt);
            return view;
        };

        return tab;
    }

    /// <summary>Awaits the load/new-conversation init (so any pinned context is realized), then auto-sends
    /// the initial prompt if one was supplied.</summary>
    private static async Task InitAsync(Task load, ConversationViewModel vm, string? initialPrompt)
    {
        await load;
        if (!string.IsNullOrWhiteSpace(initialPrompt))
            await vm.SendSeedAsync(initialPrompt);
    }
}
