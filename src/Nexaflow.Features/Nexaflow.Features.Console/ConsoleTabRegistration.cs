using Nexaflow.Features.Common;
using Nexaflow.Features.Console.ViewModels;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.Views;

namespace Nexaflow.Features.Console;

/// <summary>
/// Registers the cmd terminal page with the feature manager.
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

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Working directory to start the terminal in.", Required: false),
        new("env",  "Name of a configured console environment to use.", Required: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var initialPath = pageParams?.GetValueOrDefault("path");
        var envName     = pageParams?.GetValueOrDefault("env");

        // Decide which environment to use, or whether to ask. The launch picker only appears when opened
        // for a folder (Cmd Here), there's a real choice (>1 environment), no explicit env was named, and
        // no "always use here" binding has been remembered for that folder.
        TerminalEnvironment? env;
        bool pickerPending = false;

        if (envName is not null)
            env = _config.FindEnvByName(envName) ?? _config.GetDefaultEnv();
        else if (initialPath is not null && _config.FindBoundEnvName(initialPath) is { } boundName)
            env = _config.FindEnvByName(boundName) ?? _config.GetDefaultEnv();
        else if (initialPath is not null && _config.Environments.Count > 1)
            { env = null; pickerPending = true; }
        else
            env = _config.GetDefaultEnv();

        var title = env?.TabTitle is { Length: > 0 } t ? t : "Console";
        var vm    = new CmdTerminalViewModel(_config, _shellServices, initialPath, env, pickerPending);

        var tab = new Page
        {
            Title       = title,
            Icon        = "⌨",
            Breadcrumbs = {new BreadcrumbSegment { Label = title }}
        };

        tab.ContentFactory = () =>
        {
            vm.Tab = tab;          // wire so the VM can mutate its own page title/breadcrumbs
            var view = new TerminalView(vm);
            if (pickerPending && initialPath is not null)
                vm.ShowEnvironmentPicker(_config.Environments, initialPath);
            return view;
        };

        return tab;
    }
}
