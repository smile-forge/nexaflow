using Nexaflow.Core.AI;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Localization;
using Nexaflow.Core.Services;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// The page a config is edited on, where it has one of its own rather than rows of its settings. A page is the view marked as
/// drawing the config's model (<c>[ViewFor]</c>), made through its constructor taking that model, so nothing hands it a service
/// and it reaches none itself: what it shows of the session comes through its model. A feature's page draws its config. A page of
/// Core's that shows the session draws a view-model instead, made here from the config and the session services this was built
/// with — by the window for its Options and Configure panels, by the app for the setup wizard. A Core page that comes to need a
/// session service takes it on its view-model, and the view-model is made here.
/// </summary>
/// <param name="pageOf">The page drawing a model, or null when nothing draws it.</param>
/// <param name="session">What the window — or, for the setup wizard, the app — has of the session: the components the About page
/// lists and the update it offers, the language whose installed packs the Shell page offers and the session log it turns on and
/// off, and the saved workspaces and live runtimes the Workspaces page edits, with what a workspace it adds is set up with.
/// Without it, every config is its own page's model.</param>
public sealed class ConfigPageModels(Func<object, object?> pageOf, WindowServices? session)
{
    /// <summary>Pages drawn by the views <paramref name="views"/> names, the session's pages bound to <paramref name="session"/>.</summary>
    public static ConfigPageModels Drawn(ViewRegistry views, WindowServices session)
        => new(model => views.Draws(model.GetType()) ? views.Create(model) : null, session);

    /// <summary>What a page for a config of <paramref name="configType"/> draws: a view-model of the session for a Core page that
    /// shows it, else the config.</summary>
    public static Type ModelTypeOf(Type configType)
        => configType == typeof(AboutConfig)      ? typeof(AboutViewModel)
         : configType == typeof(ShellConfig)      ? typeof(ShellConfigViewModel)
         : configType == typeof(WorkspacesConfig) ? typeof(WorkspacesConfigViewModel)
         : configType;

    /// <summary>What the page for <paramref name="config"/> is bound to: its view-model where it shows the session, else the config.</summary>
    public object ModelFor(object config) => (config, session) switch
    {
        (AboutConfig, { } s)           => new AboutViewModel(s.Dependencies, s.Updates, s.Workspaces),
        (ShellConfig shell, { } s)     => new ShellConfigViewModel(shell, s.Language, s.Logging.Session),
        (WorkspacesConfig list, { } s) => new WorkspacesConfigViewModel(list, s, this),
        _                              => config,
    };

    /// <summary>The page <paramref name="config"/> is edited on, or null when it has none and its settings show as rows.</summary>
    public object? PageFor(object config) => pageOf(ModelFor(config));
}
