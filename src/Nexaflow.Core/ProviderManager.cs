using Nexaflow.Providers.Common;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Nexaflow.Core;

/// <summary>
/// Loads LLM provider plugin assemblies at runtime and records the provider/config TYPES they
/// expose. Provider assemblies are never referenced at compile time; they are discovered and
/// loaded by file name so that new providers can be dropped alongside the executable.
/// Live provider instances are deduplicated process-wide: <see cref="AcquireProviderSet"/> hands
/// out one shared <see cref="ILlmProvider"/> per unique (provider + config payload), ref-counted,
/// and <see cref="ReleaseProviderSet"/> unloads it once no Workspace references it. Provider
/// <em>configs</em> live on the owning <see cref="Models.Profile"/>.
/// </summary>
public sealed class ProviderManager
{
    public static ProviderManager Instance { get; } = new();

    private IBackgroundActivityManager? _activityManager;
    private readonly HashSet<string>          _loadedAssemblies = [];
    private readonly List<AssemblyDescriptor> _descriptors      = [];

    // ── Global ref-counted provider instance pool ─────────────────────────────
    private sealed class PoolEntry
    {
        public required ILlmProvider Provider;
        public required string       AssemblyFile;
        public int                   RefCount;
    }
    private readonly Dictionary<string, PoolEntry> _pool = [];
    private readonly object _poolLock = new();

    private static readonly JsonSerializerOptions _keyOpts = new() { WriteIndented = false };

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
    /// Instantiates each discovered config type and loads it from <paramref name="dir"/>, returning
    /// one config instance per type. These live on the owning <see cref="Models.Profile"/> and are
    /// shared by all its Workspaces. Providers in assemblies not yet loaded are absent (call
    /// <see cref="DiscoverAll"/> first to include them).
    /// </summary>
    public IReadOnlyList<IProviderConfig> LoadProviderConfigs(string dir)
    {
        var configs = new List<IProviderConfig>();
        foreach (var desc in _descriptors)
            foreach (var ct in desc.ConfigTypes)
            {
                var cfg = (IProviderConfig)Activator.CreateInstance(ct)!;
                ConfigManager.Instance.LoadFrom(dir, cfg, cfg.ConfigName);
                configs.Add(cfg);
            }
        return configs;
    }

    /// <summary>
    /// Acquires the live provider instances for a Workspace from the given profile-owned
    /// <paramref name="profileConfigs"/>. Each provider is deduplicated process-wide by
    /// (provider type + serialized payload of the configs feeding its constructor), so two
    /// Workspaces with identical config share one instance. Increments the pool ref-count for each.
    /// Call <see cref="ReleaseProviderSet"/> when the Workspace is reconfigured or disposed.
    /// </summary>
    public ProviderSet AcquireProviderSet(IReadOnlyList<IProviderConfig> profileConfigs)
    {
        var providers   = new Dictionary<string, ILlmProvider>(StringComparer.OrdinalIgnoreCase);
        var assemblyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var poolKeys    = new List<string>();

        lock (_poolLock)
        {
            foreach (var desc in _descriptors)
                foreach (var (type, ctor) in desc.Providers)
                {
                    var parms = ctor.GetParameters();
                    var args  = parms
                        .Select(p => typeof(IBackgroundActivityManager).IsAssignableFrom(p.ParameterType)
                            ? (object?)_activityManager
                            : profileConfigs.FirstOrDefault(c => c.GetType() == p.ParameterType))
                        .ToArray();

                    var feeding = parms
                        .Select(p => profileConfigs.FirstOrDefault(c => c.GetType() == p.ParameterType))
                        .Where(c => c is not null)
                        .OrderBy(c => c!.GetType().FullName, StringComparer.Ordinal);
                    var key = type.FullName + "|" +
                              string.Join("|", feeding.Select(c => JsonSerializer.Serialize(c, c!.GetType(), _keyOpts)));

                    if (!_pool.TryGetValue(key, out var entry))
                    {
                        var provider = (ILlmProvider)ctor.Invoke(args);
                        entry = new PoolEntry { Provider = provider, AssemblyFile = desc.FileName };
                        _pool[key] = entry;
                    }
                    entry.RefCount++;

                    providers[entry.Provider.Name]   = entry.Provider;
                    assemblyMap[entry.Provider.Name] = entry.AssemblyFile;
                    poolKeys.Add(key);
                }
        }

        return new ProviderSet(providers, profileConfigs, assemblyMap, poolKeys);
    }

    /// <summary>
    /// Releases the pool references held by <paramref name="set"/>. Any provider whose ref-count
    /// reaches zero is removed from the pool and disposed (if <see cref="IDisposable"/>) — "unloaded".
    /// </summary>
    public void ReleaseProviderSet(ProviderSet? set)
    {
        if (set is null) return;
        lock (_poolLock)
        {
            foreach (var key in set.PoolKeys)
            {
                if (!_pool.TryGetValue(key, out var entry)) continue;
                if (--entry.RefCount > 0) continue;
                _pool.Remove(key);
                (entry.Provider as IDisposable)?.Dispose();
            }
        }
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
