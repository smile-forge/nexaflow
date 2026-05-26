using Nexaflow.Features.Common;
using Nexaflow.Features.Console.ViewModels;
using ConsoleView = Nexaflow.Features.Console.Views.ConsoleView;

namespace Nexaflow.Features.Console;

/// <summary>
/// Registers the Console terminal page with the feature manager.
/// </summary>
public sealed class ConsoleTabRegistration : IPageRegistration
{
    private readonly ConsoleConfig  _config;
    private readonly IShellServices _shellServices;

    public ConsoleTabRegistration(ConsoleConfig config, IShellServices shellServices)
    {
        _config        = config;
        _shellServices = shellServices;
    }

    public static string StaticPageKind => "Console";
    public string PageKind => StaticPageKind;

    public Page CreatePage(Dictionary<string, string>? pageParams = null)
    {
        var initialPath = pageParams?.GetValueOrDefault("path");
        var envName     = pageParams?.GetValueOrDefault("env");

        var env = envName is not null
            ? _config.FindEnvByName(envName) ?? _config.GetDefaultEnv()
            : _config.GetDefaultEnv();

        var title = env?.TabTitle is { Length: > 0 } t ? t : "Console";
        var vm    = new ConsoleViewModel(_config, _shellServices, initialPath, env);

        var tab = new Page
        {
            Title       = title,
            Icon        = "⌨",
            Breadcrumbs = {new BreadcrumbSegment { Label = title }}
        };

        tab.ContentFactory = () =>
        {
            vm.Tab = tab;          // wire so the VM can mutate its own page title/breadcrumbs
            return new ConsoleView(vm);
        };

        return tab;
    }
}
