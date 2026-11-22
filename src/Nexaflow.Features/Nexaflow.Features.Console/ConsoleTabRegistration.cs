using Nexaflow.Features.Common;
using Nexaflow.Features.Console.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.IO.Terminal;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.Shells;
using Nexaflow.Visuals.Terminal.Views;

namespace Nexaflow.Features.Console;

/// <summary>
/// Registers the Console page with the feature manager. A console opened on a folder runs the shell that reaches it: the
/// location's own on a location with a shell, cmd.exe on this computer anywhere else
/// (<see cref="TerminalShellRoute.ForFolder"/>).
/// </summary>
public sealed class ConsoleTabRegistration : IPageRegistration
{
    private readonly ConsoleConfig      _config;
    private readonly IShellServices     _shellServices;
    private readonly IAIService         _aiService;
    private readonly IVirtualFileSystem _files;

    public ConsoleTabRegistration(ConsoleConfig config, IShellServices shellServices, IAIService aiService,
                                  IVirtualFileSystem files)
    {
        _config        = config;
        _shellServices = shellServices;
        _aiService     = aiService;
        _files         = files;
    }

    public static string StaticPageKind => "Console";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Full path of the folder to start the terminal in — on this computer, or on a location (a ::id\\… path), " +
                    "where the location's own shell runs.", Required: false, OnDisk: true),
        new("env",  "Name of a configured console environment to use.", Required: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var initialPath = pageParams?.GetValueOrDefault("path");
        var envName     = pageParams?.GetValueOrDefault("env");

        // Decide which environment to use, or whether to ask. The picker appears when a console is opened
        // for a location (Cmd Here) that hasn't pinned an environment AND there's a real choice (>1
        // configured) — there is no implicit default. An explicitly named env, a location that has pinned
        // one, or a single configured environment all skip the picker.
        TerminalEnvironment? env;
        bool pickerPending = false;

        if (envName is not null)
            env = _config.FindEnvByName(envName) ?? _config.Environments.FirstOrDefault();
        else if (initialPath is not null
                 && _config.FindBoundEnvName(initialPath, _files.GetNameComparison(initialPath)) is { } boundName)
            env = _config.FindEnvByName(boundName) ?? _config.Environments.FirstOrDefault();
        else if (initialPath is not null && _config.Environments.Count > 1)
            { env = null; pickerPending = true; }
        else
            env = _config.Environments.FirstOrDefault();

        var route = TerminalShellRoute.ForFolder(_files, initialPath, new PseudoConsoleLauncher(), new CmdShellDialect(_files));
        var title = env?.TabTitle is { Length: > 0 } t ? t : Str.Get("Console.Page.Title");
        var vm    = new CmdTerminalViewModel(_config, route, _files, _shellServices, _aiService, env, pickerPending);

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
