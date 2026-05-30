using Nexaflow.Providers.Common;

namespace Nexaflow.Core;

/// <summary>
/// The provider instances and configs owned by a single WorkContext. Each context gets its own
/// set so it can hold, e.g., a different Claude subscription from the next context. Created by
/// <see cref="ProviderManager.CreateProviderSet"/> from the currently-loaded provider assemblies;
/// configs are loaded from (and saved to) the context's own folder.
/// </summary>
public sealed class ProviderSet
{
    private readonly Dictionary<string, string> _assemblyMap;

    /// <summary>Provider instances keyed by provider name.</summary>
    public IReadOnlyDictionary<string, ILlmProvider> Providers { get; }

    /// <summary>One config instance per discovered <see cref="IProviderConfig"/> type, for editing/saving.</summary>
    public IReadOnlyList<IProviderConfig> Configs { get; }

    public ProviderSet(
        IReadOnlyDictionary<string, ILlmProvider> providers,
        IReadOnlyList<IProviderConfig>            configs,
        Dictionary<string, string>                assemblyMap)
    {
        Providers    = providers;
        Configs      = configs;
        _assemblyMap = assemblyMap;
    }

    /// <summary>Plugin DLL file name for a provider name, or empty string if unknown.</summary>
    public string GetAssemblyFileName(string providerName)
        => _assemblyMap.TryGetValue(providerName, out var asm) ? asm : string.Empty;
}
