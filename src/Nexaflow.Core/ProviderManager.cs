using Nexaflow.Providers.Common;
using System.IO;
using System.Reflection;

namespace Nexaflow.Core;

/// <summary>
/// Loads LLM provider plugin assemblies at runtime and records the provider/config TYPES they
/// expose. Provider assemblies are never referenced at compile time; they are discovered and
/// loaded by file name so that new providers can be dropped alongside the executable.
/// Instances are NOT created here — each WorkContext builds its own <see cref="ProviderSet"/> via
/// <see cref="CreateProviderSet"/> so providers and their configs are per-context.
/// </summary>
public sealed class ProviderManager
{
    public static ProviderManager Instance { get; } = new();

    private IBackgroundActivityManager? _activityManager;
    private readonly HashSet<string>          _loadedAssemblies = [];
    private readonly List<AssemblyDescriptor> _descriptors      = [];

    /// <summary>The provider/config types found in one loaded plugin assembly.</summary>
    private sealed record AssemblyDescriptor(
        string                                             FileName,
        IReadOnlyList<Type>                                ConfigTypes,
        IReadOnlyList<(Type Type, ConstructorInfo Ctor)>  Providers);

    private ProviderManager() { }

    /// <summary>Must be called once at startup before any Load method.</summary>
    public void Initialize(IBackgroundActivityManager activityManager)
        => _activityManager = activityManager;

    /// <summary>The shared background-activity manager set by <see cref="Initialize"/>, or null before startup.</summary>
    public IBackgroundActivityManager? ActivityManager => _activityManager;

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
    /// any that have not been loaded yet. Called when the AI options panel is opened.
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

    /// <summary>
    /// Builds a fresh <see cref="ProviderSet"/> for one WorkContext: instantiates each discovered
    /// config type, loads it from <paramref name="contextDir"/>, then constructs each provider with
    /// that context's configs. Providers in assemblies not yet loaded are absent (call
    /// <see cref="DiscoverAll"/> first to include them).
    /// </summary>
    public ProviderSet CreateProviderSet(string contextDir)
    {
        var providers   = new Dictionary<string, ILlmProvider>(StringComparer.OrdinalIgnoreCase);
        var configs     = new List<IProviderConfig>();
        var assemblyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var desc in _descriptors)
        {
            // One config instance per type, populated from this context's own folder.
            var byType = new Dictionary<Type, IProviderConfig>();
            foreach (var ct in desc.ConfigTypes)
            {
                var cfg = (IProviderConfig)Activator.CreateInstance(ct)!;
                ConfigManager.Instance.LoadFrom(contextDir, cfg, cfg.ConfigName);
                byType[ct] = cfg;
                configs.Add(cfg);
            }

            foreach (var (type, ctor) in desc.Providers)
            {
                var args = ctor.GetParameters()
                    .Select(p =>
                        typeof(IBackgroundActivityManager).IsAssignableFrom(p.ParameterType)
                            ? (object?)_activityManager
                            : (object?)byType.Values.FirstOrDefault(c => c.GetType() == p.ParameterType))
                    .ToArray();
                var provider = (ILlmProvider)ctor.Invoke(args);
                providers[provider.Name]   = provider;
                assemblyMap[provider.Name] = desc.FileName;
            }
        }

        return new ProviderSet(providers, configs, assemblyMap);
    }

    private void LoadAssembly(string assemblyPath)
    {
        var fileName = Path.GetFileName(assemblyPath);
        if (!_loadedAssemblies.Add(fileName)) return;

        var asm = Assembly.LoadFrom(assemblyPath);

        var configTypes = asm.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IProviderConfig).IsAssignableFrom(t))
            .ToList();

        var providerTypes = asm.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(ILlmProvider).IsAssignableFrom(t))
            .Select(t => (Type: t, Ctor: t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First()))
            .ToList();

        _descriptors.Add(new AssemblyDescriptor(fileName, configTypes, providerTypes));
    }
}
