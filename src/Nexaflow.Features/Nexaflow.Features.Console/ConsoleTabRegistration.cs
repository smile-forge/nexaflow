using Nexaflow.Features.Common;
using Nexaflow.Features.Console.ViewModels;
using ConsoleView = Nexaflow.Features.Console.Views.ConsoleView;

namespace Nexaflow.Features.Console;

/// <summary>
/// Registers the Console terminal page with <see cref="FeatureManager"/>.
/// </summary>
public sealed class ConsoleTabRegistration : ITabRegistration
{
    public string PageKind => "Console";

    public TabEntry CreateTab(Dictionary<string, string>? pageParams = null)
    {
        var vm = new ConsoleViewModel();
        return new TabEntry
        {
            Title       = "Console",
            Icon        = "⌨",
            Breadcrumbs = [new BreadcrumbSegment { Label = "Console" }],
            PageFactory = () => new ConsoleView(vm)
        };
    }
}
