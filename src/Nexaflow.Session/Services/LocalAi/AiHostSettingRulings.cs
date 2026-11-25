using Nexaflow.AiHost.Contracts;
using Nexaflow.Localization;

namespace Nexaflow.Session.Services.LocalAi;

/// <summary>
/// The settings the machine's AI host service decides while this app is joined to it. The share of memory models on the CPU may hold is
/// the service's, set by its administrator in its policy file for every app on the machine; Settings shows it as the service holds it,
/// and the app's own share applies again to a host it starts for itself.
/// </summary>
/// <param name="serviceMemory">The service's share, where this app is joined to it; null where it runs a host of its own.</param>
public sealed class AiHostSettingRulings(Func<HostMemory?> serviceMemory) : IConfigRulings
{
    public ConfigRuling? RulingFor(object config, string propertyName)
        => config is ModelPreloadConfig && propertyName == nameof(ModelPreloadConfig.MemoryForModelsPercent)
           && serviceMemory() is { TotalBytes: >= 100 } memory
            ? new ConfigRuling((int)(memory.BudgetBytes / (memory.TotalBytes / 100)), Str.Get("Shell.Config.ModelPreload.MemorySetByService"))
            : null;
}
