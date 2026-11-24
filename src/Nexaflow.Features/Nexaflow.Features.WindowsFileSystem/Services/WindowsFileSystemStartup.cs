using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// The file system's setup for the session, as its assembly activates: the external apps and their stable ids, the file map,
/// the per-extension default actions, the "New" entries Windows registers, and the user's templates — each read once, for
/// every workspace.
/// </summary>
public sealed class WindowsFileSystemStartup : IFeatureStartup
{
    public void Start(FeatureStartupContext session)
    {
        var externalApps = session.Config<ExternalAppsConfig>();
        ExternalAppRegistry.Instance.Initialize(externalApps);

        // Give pre-Id apps a stable identity (referenced by Default Action overrides); persist once.
        if (ExternalAppRegistry.Instance.BackfillIds()) session.SaveConfig(externalApps);

        FileMapManager.Instance.Initialize(session.DataDirectory);

        // Per-extension double-click overrides (Options → Default Actions), consulted by DefaultFileOpener.
        DefaultActionRegistry.Instance.Initialize(session.Config<DefaultActionsConfig>());

        // ShellNew create-file entries from HKCR — gated on the registry-handlers toggle.
        ShellNewRegistry.Instance.Initialize(externalApps.UseRegistryMapping);

        // Templated-create (user templates) — global config + its appdata template store.
        TemplatedCreateRegistry.Instance.Initialize(session.Config<TemplatedCreateConfig>(), session.DataDirectory);
    }
}
