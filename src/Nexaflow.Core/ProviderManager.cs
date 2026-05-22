using Nexaflow.Providers.Common;
using System.IO;
using System.Reflection;

namespace Nexaflow.Core;

/// <summary>
/// Loads LLM provider plugin assemblies at runtime.
/// Provider assemblies are never referenced at compile time; they are discovered and
/// loaded by file name so that new providers can be dropped alongside the executable.
/// Loaded provider instances are collected in <see cref="LoadedProviders"/> and distributed
/// to each WorkContext's AIService by <see cref="Services.WorkContextManager"/>.
/// </summary>
public sealed class ProviderManager
{
    public static ProviderManager Instance { get; } = new();

    private IBackgroundActivityManager? _activityManager;
    private readonly HashSet<string>             _loadedAssemblies    = [];
    private readonly Dictionary<string, string>  _providerAssemblyMap = [];
    private readonly Dictionary<string, ILlmProvider> _loadedProviders = new(StringComparer.OrdinalIgnoreCase);

    private ProviderManager() { }

    /// <summary>Must be called once at startup before any Load method.</summary>
    public void Initialize(IBackgroundActivityManager activityManager)
        => _activityManager = activityManager;

    /// <summary>All provider instances loaded so far, keyed by provider name.</summary>
    public IReadOnlyDictionary<string, ILlmProvider> LoadedProviders => _loadedProviders;

    /// <summary>
    /// Loads only the assemblies listed in <paramref name="assemblyFileNames"/>.
    /// Called at startup with the union of assembly file names from all WorkContext AiConfigs.
    /// </summary>
    public void LoadConfigured(IEnumerable<string> assemblyFileNames)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        foreach (var fileName in assemblyFileNames.Where(f => !string.IsNullOrEmpty(f)).Distinct())
        {
            var path = Path.Combine(baseDir, fileName);
            if (File.Exists(path))
                LoadAssembly(path);
        }
    }

    /// <summary>
    /// Scans the application directory for all <c>Nexaflow.Providers.*.dll</c> files and loads
    /// any that have not been loaded yet. Called when the options panel is opened.
    /// </summary>
    public void DiscoverAll()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        foreach (var path in Directory.GetFiles(baseDir, "Nexaflow.Providers.*.dll"))
        {
            var fileName = Path.GetFileName(path);
            if (!_loadedAssemblies.Contains(fileName))
                LoadAssembly(path);
        }
    }

    /// <summary>Returns the plugin DLL file name for a registered provider name, or empty string if unknown.</summary>
    public string GetAssemblyFileName(string providerName)
        => _providerAssemblyMap.TryGetValue(providerName, out var asm) ? asm : string.Empty;

    private void LoadAssembly(string assemblyPath)
    {
        var fileName = Path.GetFileName(assemblyPath);
        if (_loadedAssemblies.Contains(fileName)) return;
        _loadedAssemblies.Add(fileName);

        var asm = Assembly.LoadFrom(assemblyPath);

        // Discover and instantiate all IProviderConfig types
        var configs = new Dictionary<Type, IProviderConfig>();
        foreach (var t in asm.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface
                        && typeof(IProviderConfig).IsAssignableFrom(t)))
        {
            var cfg = (IProviderConfig)Activator.CreateInstance(t)!;
            ConfigManager.Instance.Register(cfg, cfg.ConfigName);
            configs[t] = cfg;
        }

        // Discover and instantiate all ILlmProvider types, injecting known services
        foreach (var t in asm.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface
                        && typeof(ILlmProvider).IsAssignableFrom(t)))
        {
            var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            var args = ctor.GetParameters()
                .Select(p =>
                    typeof(IBackgroundActivityManager).IsAssignableFrom(p.ParameterType)
                        ? (object?)_activityManager
                        : (object?)configs.Values.FirstOrDefault(c => c.GetType() == p.ParameterType))
                .ToArray();
            var provider = (ILlmProvider)ctor.Invoke(args);
            _loadedProviders[provider.Name] = provider;
            _providerAssemblyMap[provider.Name] = fileName;
        }
    }
}
