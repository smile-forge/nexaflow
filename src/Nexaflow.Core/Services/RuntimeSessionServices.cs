using Nexaflow.Core.Localization;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services.Elevation;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Core.Services;

/// <summary>What every workspace runtime shares with the session it runs in, handed to <see cref="WorkspaceRuntimeFactory"/>.</summary>
/// <param name="Config">The session's configuration, which a feature's global config is saved through.</param>
/// <param name="Shell">The shell's settings: whether Enhanced AI Protection Mode frames every runtime's prompts.</param>
/// <param name="Security">The security settings: whether a runtime's assistant may see the open windows.</param>
/// <param name="Providers">The pool every runtime acquires its provider instances from.</param>
/// <param name="SavedWorkspaces">The saved workspaces, which a runtime writes back when its workspace's tabs or placement change.</param>
/// <param name="Features">The feature system: builds each runtime's feature instances, and the pages it opens.</param>
/// <param name="Tasks">Where each runtime's assistant runs its slow work; null where nothing runs in the background.</param>
/// <param name="Notify">How a runtime's background work tells the user it ended.</param>
/// <param name="FileSystem">The session's file system, under each runtime's own.</param>
/// <param name="LocationProviders">The backends a runtime's connections are reached through.</param>
/// <param name="Discovered">The locations found on this machine, mounted in every runtime.</param>
/// <param name="Credentials">What signs a runtime's connections in.</param>
/// <param name="FirstDiscovery">The session's first look for the locations on this machine, which a runtime lets finish, up to a
/// point, before it mounts.</param>
/// <param name="Ui">The thread the windows run on.</param>
/// <param name="Folders">The session's file and folder watches, and its busy folders.</param>
/// <param name="Elevation">How an admin-only request runs, out of process.</param>
/// <param name="Clipboard">The system clipboard.</param>
/// <param name="Dependencies">The third-party components the features declare, and what the last probe found of each.</param>
/// <param name="Language">The UI language, which a window restarting for a new one selects.</param>
/// <param name="Views">The view each open page is drawn with, let go when its tab closes.</param>
/// <param name="HelpTools">The tools that read the help the app ships, which each runtime's assistant offers on every page.</param>
public sealed record RuntimeSessionServices(
    ConfigManager Config,
    ShellConfig Shell,
    SecurityConfig Security,
    ProviderManager Providers,
    WorkspaceManager SavedWorkspaces,
    FeatureManager Features,
    BackgroundActivityManager? Tasks,
    Action<NotificationItem> Notify,
    IVirtualFileSystem FileSystem,
    LocationProviderRegistry LocationProviders,
    DiscoveredLocations Discovered,
    ICredentialSource Credentials,
    Func<Task> FirstDiscovery,
    IUiThread Ui,
    FolderActivity Folders,
    ElevatedBridgeLauncher Elevation,
    IShellClipboard Clipboard,
    ExternalDependencyRegistry Dependencies,
    LanguageManager Language,
    PageViews Views,
    IReadOnlyList<IClientTool> HelpTools);
