using Nexaflow.Providers.Common;

namespace Nexaflow.Core;

/// <summary>
/// A bundle of live provider instances handed to one <see cref="Models.Workspace"/>, together with
/// the profile-owned configs they were built from. The instances are NOT owned outright — they come
/// from <see cref="ProviderManager"/>'s global ref-counted pool (so an identical provider config is
/// instantiated only once process-wide). <see cref="PoolKeys"/> records the pool entries this set
/// acquired so <see cref="ProviderManager.ReleaseProviderSet"/> can decrement them.
/// </summary>
public sealed class ProviderSet
{
    private readonly Dictionary<string, string> _assemblyMap;

    /// <summary>Provider instances keyed by provider name.</summary>
    public IReadOnlyDictionary<string, ILlmProvider> Providers { get; }

    /// <summary>The profile's provider configs (one per discovered type), for editing/saving.</summary>
    public IReadOnlyList<IProviderConfig> Configs { get; }

    /// <summary>Pool keys this set acquired, in acquisition order — used to release them.</summary>
    internal IReadOnlyList<string> PoolKeys { get; }

    public ProviderSet(
        IReadOnlyDictionary<string, ILlmProvider> providers,
        IReadOnlyList<IProviderConfig>            configs,
        Dictionary<string, string>                assemblyMap,
        IReadOnlyList<string>                     poolKeys)
    {
        Providers    = providers;
        Configs      = configs;
        _assemblyMap = assemblyMap;
        PoolKeys     = poolKeys;
    }

    /// <summary>Plugin DLL file name for a provider name, or empty string if unknown.</summary>
    public string GetAssemblyFileName(string providerName)
        => _assemblyMap.TryGetValue(providerName, out var asm) ? asm : string.Empty;
}
