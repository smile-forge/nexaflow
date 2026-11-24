using Nexaflow.Core.Controls;
using Nexaflow.Core.Localization;
using Nexaflow.Core.Services;

namespace Nexaflow.Core.ViewModels;

/// <summary>
/// What a <c>[CustomControl]</c> settings page is bound to. The page is made by its config's attribute, through its parameterless
/// constructor (<see cref="ConfigEditViewModel"/>), so nothing hands it a service and it reaches none itself: what it shows of the
/// session comes through its <c>DataContext</c>. A feature's page is bound to its config. A page of Core's that shows the session
/// is bound to a view-model instead, made here from the config and the session services this was built with — by the window for
/// its Options, by the app for the setup wizard. A Core page that comes to need a session service takes it on its view-model, and
/// the view-model is made here.
/// </summary>
/// <param name="dependencies">The third-party components the features declare, which the About page lists.</param>
/// <param name="language">The UI language, whose installed packs the Shell page offers.</param>
public sealed class ConfigPageModels(ExternalDependencyRegistry dependencies, LanguageManager language)
{
    /// <summary>What the page for <paramref name="config"/> is bound to: its view-model where it shows the session, else the config.</summary>
    public object ModelFor(object config) => config switch
    {
        AboutConfig       => new AboutViewModel(dependencies),
        ShellConfig shell => new ShellConfigViewModel(shell, language),
        _                 => config,
    };
}
