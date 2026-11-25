using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using Nexaflow.Core;
using Nexaflow.Core.Help;
using Nexaflow.Core.Localization;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.Services.Elevation;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using NSubstitute;

namespace Nexaflow.Tests.Core;

/// <summary>
/// The suite's workspace runtimes, built as the app builds them — through a <see cref="WorkspaceRuntimeFactory"/> — but
/// unstarted: wired with their assistant, shell services, file system and feature scope, with no provider acquired, nothing
/// mounted and nothing run in the background. Their conversations go under the suite's own config root
/// (<see cref="TestConfigIsolation"/>). Their session ships the language packs but no help, so their assistants offer no help tools.
/// </summary>
internal static class TestRuntimes
{
    /// <summary>A factory whose runtimes run their assistant's slow work on <paramref name="tasks"/>, telling
    /// <paramref name="notify"/> as each piece ends — their UI thread the calling thread — and write their workspace back to
    /// <paramref name="savedWorkspaces"/> (a list of one default workspace when none is given). Nobody holds a secret for their
    /// connections, and none are made.</summary>
    public static WorkspaceRuntimeFactory FactoryWith(BackgroundActivityManager? tasks, Action<NotificationItem> notify,
                                                      WorkspaceManager? savedWorkspaces = null)
    {
        var ui        = new WpfUiThread(Dispatcher.CurrentDispatcher);
        var locations = new LocationProviderRegistry();
        var dependencies = new ExternalDependencyRegistry(ui, Features.Catalog, [new AiHostReading(holds: null)]);
        return new WorkspaceRuntimeFactory(new RuntimeSessionServices(
            Providers, savedWorkspaces ?? SavedListOf(), Features, tasks, notify, VirtualFileSystem.Instance, locations,
            new DiscoveredLocations(locations), Substitute.For<ICredentialSource>(), FirstDiscovery: () => Task.CompletedTask,
            ui, new FolderActivity(ui, () => new ShellFolderWatcher(ui.Dispatcher)), new ElevatedBridgeLauncher(), new ShellClipboard(),
            dependencies, new LanguageManager(Path.Combine(AppContext.BaseDirectory, "Languages"), AssemblyLanguagePack.Load),
            PageViews.FromCatalog(Features.Catalog, dependencies, showComponents: () => { }), HelpTools: []));
    }

    /// <summary>
    /// The suite's provider pool, made as the session makes it but over no AI host: no model files, nothing loaded ahead and no
    /// app-wide service — shared by every runtime and saved list the suite builds, as the session's is.
    /// </summary>
    public static ProviderManager Providers { get; } = new(activityManager: null, assets: null, residency: null, services: []);

    /// <summary>A saved list holding <paramref name="workspaces"/> — one default workspace when none is given — read through the
    /// suite's provider pool and written, when saved, under the suite's config root (<see cref="TestConfigIsolation"/>).</summary>
    public static WorkspaceManager SavedListOf(params Workspace[] workspaces)
        => new(new WorkspacesConfig { Contexts = [.. workspaces] }, Providers);

    /// <summary>
    /// The suite's feature system, made as the session makes it — over a catalogue of the suite's output, its features activated
    /// into the suite's config root (<see cref="TestConfigIsolation"/>), and given the help pages the Help page's registration is
    /// built with — the first time a test asks, and shared by every runtime the suite builds, as the session's is.
    /// </summary>
    public static FeatureManager Features => _features.Value;

    private static readonly Lazy<FeatureManager> _features = new(() =>
    {
        var features = new FeatureManager(new FeatureCatalog(), new WpfUiThread(Dispatcher.CurrentDispatcher), new LocationProviderRegistry());
        features.ProvideTool(new HelpLibrary(new LanguageManager(Path.Combine(AppContext.BaseDirectory, "Languages"), AssemblyLanguagePack.Load)));
        return features;
    });

    /// <summary>A wired, unstarted runtime for <paramref name="workspace"/> (a fresh default one when none is given), saved in a list
    /// of its own, standing ready for a launch that has not come yet when <paramref name="standby"/>.</summary>
    public static WorkspaceRuntime Unstarted(Workspace? workspace = null, bool standby = false)
    {
        var subject = workspace ?? new Workspace();
        var runtime = FactoryWith(tasks: null, notify: _ => { }, SavedListOf(subject)).CreateUnstarted(subject);
        runtime.IsStandby = standby;
        return runtime;
    }
}
