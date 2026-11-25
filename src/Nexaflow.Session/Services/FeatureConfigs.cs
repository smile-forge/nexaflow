using System.Collections.Concurrent;

namespace Nexaflow.Session.Services;

/// <summary>
/// The features' configs as the session holds them: one instance of each global config, registered as its feature activates and
/// saved through the session's configuration; which configs are a workspace's own, read from the catalogue's index without loading
/// anything; and the live view a feature's registry reads them through.
/// </summary>
/// <param name="catalog">The feature index: which config types are workspace-scoped, and the assembly that owns each.</param>
/// <param name="configuration">The session's configuration, which every global config is registered in and saved through.</param>
public sealed class FeatureConfigs(FeatureCatalog catalog, ConfigManager configuration)
{
    private readonly ConcurrentDictionary<Type, IFeatureConfig> _global = new();

    /// <summary>The global configs registered so far, one instance of each, growing as features activate.</summary>
    public IReadOnlyDictionary<Type, IFeatureConfig> Global => _global;

    /// <summary>
    /// Workspace-scoped config types (marked <see cref="WorkspaceScopedConfigAttribute"/>), resolved from the catalogue. Asking
    /// loads every owning assembly, so only the Configure panel and a workspace's setup, which need every scoped config, ask; the
    /// lazy <see cref="Workspace.FindWorkspaceConfig"/> serves everything else.
    /// </summary>
    public IReadOnlyList<Type> WorkspaceScopedTypes => catalog.ResolveWorkspaceScopedConfigTypes();

    /// <summary>True if <paramref name="configType"/> is a <c>[WorkspaceScopedConfig]</c> — index-only, no load.</summary>
    public bool IsWorkspaceScoped(Type configType)
        => configType.FullName is { } name && catalog.IsWorkspaceScopedConfig(name);

    /// <summary>
    /// Registers the global config of <paramref name="configType"/>, made through its parameterless constructor, in the session's
    /// configuration. The first registration wins and is the canonical instance, so every consumer shares it. False when it is
    /// registered already or cannot be made.
    /// </summary>
    internal bool Register(Type configType)
    {
        if (_global.ContainsKey(configType)) return false;
        try
        {
            var config = (IFeatureConfig)Activator.CreateInstance(configType)!;
            _global[configType] = (IFeatureConfig)configuration.Register(config, config.ConfigName);
            return true;
        }
        catch (Exception) { return false; }   // a config that cannot be made is reported where it is asked for
    }

    /// <summary>The global config of <paramref name="configType"/>, activating the assembly that owns it first when it has not
    /// activated; null when that assembly registers none.</summary>
    internal IFeatureConfig? Find(Type configType)
    {
        if (!_global.ContainsKey(configType) && configType.FullName is { } name)
            catalog.ActivateAssemblyOwning(name);
        return _global.GetValueOrDefault(configType);
    }

    /// <summary>The configs a feature's registry is handed for <paramref name="workspace"/>: live — every global config registered
    /// by the time it is read, and the workspace's own (none for null).</summary>
    internal IReadOnlyDictionary<Type, IFeatureConfig> ViewFor(WorkspaceRuntime? workspace) => new LiveFeatureConfigView(_global, workspace);

    /// <summary>What a feature's setup is given of the session: the global configs, the data folder, and saving a config.</summary>
    internal FeatureStartupContext StartupContext()
        => new(_global, configuration.BaseDir, config => configuration.Save(config, config.ConfigName));
}
