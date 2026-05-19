using Nexaflow.Core.Services;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core;

/// <summary>
/// Singleton registry of all LLM provider factories.
/// Call <see cref="Register(Type, IBackgroundActivityManager)"/> at startup with any type
/// from the provider assembly; the manager scans that assembly for <see cref="ILlmProvider"/>
/// and <see cref="IProviderConfig"/> implementations, wires configs as constructor dependencies,
/// and registers everything with <see cref="AIService.Instance"/> automatically.
/// </summary>
public sealed class ProviderManager
{
    public static ProviderManager Instance { get; } = new();

    private ProviderManager() { }

    /// <summary>
    /// Scans the assembly of <paramref name="providerType"/> for all concrete
    /// <see cref="IProviderConfig"/> and <see cref="ILlmProvider"/> types.
    /// Creates one <see cref="IProviderConfig"/> instance per discovered type, registers it
    /// with <see cref="ConfigManager"/>, then instantiates each <see cref="ILlmProvider"/>
    /// by injecting <paramref name="activityManager"/> and any matching config.
    /// </summary>
    public void Register(Type providerType, IBackgroundActivityManager activityManager)
    {
        var asm = providerType.Assembly;

        // 1. Discover and instantiate all IProviderConfig types
        var configs = new Dictionary<Type, IProviderConfig>();
        foreach (var t in asm.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface
                        && typeof(IProviderConfig).IsAssignableFrom(t)))
        {
            var cfg = (IProviderConfig)Activator.CreateInstance(t)!;
            ConfigManager.Instance.Register(cfg, cfg.ConfigName);
            configs[t] = cfg;
        }

        // 2. Discover and instantiate all ILlmProvider types, injecting known services
        foreach (var t in asm.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface
                        && typeof(ILlmProvider).IsAssignableFrom(t)))
        {
            var ctor = t.GetConstructors()
                        .OrderByDescending(c => c.GetParameters().Length)
                        .First();
            var args = ctor.GetParameters()
                           .Select(p =>
                               typeof(IBackgroundActivityManager).IsAssignableFrom(p.ParameterType)
                                   ? (object?)activityManager
                                   : (object?)configs.Values
                                       .FirstOrDefault(c => c.GetType() == p.ParameterType))
                           .ToArray();
            var provider = (ILlmProvider)ctor.Invoke(args);
            AIService.Instance.Register(provider.Name, provider);
        }
    }
}
