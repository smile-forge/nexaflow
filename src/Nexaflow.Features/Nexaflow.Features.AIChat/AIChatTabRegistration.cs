using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.AIChat.Views;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;
using System;
using System.Collections.Generic;
using System.Text;

namespace Nexaflow.Features.AIChat
{
    public sealed class AIChatTabRegistration : IPageRegistration
    {
        public static string StaticPageKind => "AIChat";
        public string PageKind => StaticPageKind;

        private readonly IAIService     _aiService;
        private readonly IShellServices _shell;
        private readonly AiChatConfig   _config;

        public AIChatTabRegistration(IAIService aiService, IShellServices shell, AiChatConfig config)
        {
            _aiService = aiService;
            _shell     = shell;
            _config    = config;
        }

        public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
        {
            var tab = new Page
            {
                Title = Str.Get("AIChat.Page.Title"),
                Icon = "💬",
                Breadcrumbs = {new BreadcrumbSegment { Label = Str.Get("AIChat.Page.Title") }}
            };
            tab.ContentFactory = () =>
            {
                var page = new AiChatPage(_aiService, _shell, _config, tab);
                if (pageParams is not null)
                    page.Reinitialize(pageParams);
                return page;
            };
            return tab;
        }
    }
}
