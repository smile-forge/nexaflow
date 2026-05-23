using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.AIChat.Views;
using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Nexaflow.Features.AIChat
{
    public sealed class AIChatTabRegistration : IPageRegistration
    {
        public string PageKind => "AIChat";

        private readonly IAIService _aiService;

        public AIChatTabRegistration(IAIService aiService)
        {
            _aiService = aiService;
        }

        public Page CreatePage(Dictionary<string, string>? pageParams = null)
        {
            var tab = new Page
            {
                Title = "AI Chat",
                Icon = "💬",
                Breadcrumbs = {new BreadcrumbSegment { Label = "AI Chat" }}
            };
            tab.ContentFactory = () =>
            {
                var page = new AiChatPage(_aiService);
                page.TitleChanged += title =>
                {
                    tab.Title = title;
                    tab.Breadcrumbs.Clear();
                    tab.Breadcrumbs.Add(new BreadcrumbSegment { Label = title });
                };
                if (pageParams is not null)
                    page.Reinitialize(pageParams);
                return page;
            };
            return tab;
        }
    }
}
