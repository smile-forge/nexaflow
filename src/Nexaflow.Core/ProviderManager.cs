using Nexaflow.Core.AI;
using Nexaflow.Providers.Common;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Nexaflow.Core;

/// <summary>
/// Loads LLM provider plugin assemblies at runtime and records the provider/config TYPES they
/// expose. Provider assemblies are never referenced at compile time; they are discovered and
/// loaded by file name so that new providers can be dropped alongside the executable.
/// <para>
/// Live provider instances are deduplicated process-wide and ref-counted by <see cref="AcquireProviderSet"/>
/// / <see cref="ReleaseProviderSet"/>. There are two kinds:
/// <list type="bullet">
/// <item><b>Capability</b> — one per (provider type + config payload), model-agnostic, used only to
/// enumerate models for the options UI.</item>
/// <item><b>Execution</b> — one per (provider type + config payload + model), bound to that model via
/// an injected <see cref="ProviderModel"/>. Each ability-grid assignment maps to one of these. An
/// execution instance is <see cref="ILlmProvider.WarmupAsync">warmed</see> when first created (first
/// owner) and <see cref="ILlmProvider.CooldownAsync">cooled</see> when its last ref drops — so model
/// lifetime and instance lifetime are one and the same.</item>
/// </list>
/// </para>
/// Provider <em>configs</em> live on the owning <see cref="Models.Profile"/>.
/// </summary>
public sealed class ProviderManager
{
    public static ProviderManager Instance { get; } = new();

    private IBackgroundActivityManager? _activityManager;
    private IHostCapabilityService?     _hostCapabilities;
    private readonly HashSet<string>          _loadedAssemblies = [];
    private readonly List<AssemblyDescriptor> _descriptors      = [];

    // ── Global ref-counted provider instance pool ─────────────────────────────
    private sealed class PoolEntry
    {
        public required ILlmProvider Provider;
        public required string       AssemblyFile;
        /// <summary>The bound model, or empty for a model-agnostic capability instance.</summary>
        public required string       Model;
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
    public void Initialize(IBackgroundActivityManager activityManager, IHostCapabilityService hostCapabilities)
    {
        _activityManager  = activityManager;
        _hostCapabilities = hostCapabilities;
    }

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
    /// Acquires the live provider instances for a Workspace: a model-agnostic <b>capability</b> instance
    /// per discovered provider type (for the options UI's model enumeration), plus one model-bound
    /// <b>execution</b> instance per assigned <paramref name="columns"/> entry. All are deduplicated
    /// process-wide and ref-counted; an execution instance created here for the first time is warmed up.
    /// Acquire the fresh set BEFORE releasing the old one so unchanged instances never drop to zero refs.
    /// Call <see cref="ReleaseProviderSet"/> when the Workspace is reconfigured or disposed.
    /// </summary>
    public ProviderSet AcquireProviderSet(
        IReadOnlyList<IProviderConfig> profileConfigs,
        IReadOnlyList<ProviderModelPair> columns)
    {
        var capability  = new Dictionary<string, ILlmProvider>(StringComparer.OrdinalIgnoreCase);
        var execution   = new Dictionary<string, ILlmProvider>();
        var assemblyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var poolKeys    = new List<string>();
        var toWarm      = new List<ILlmProvider>();

        // Provider Name → the type/ctor that produced its capability instance, so columns can be mapped
        // to the right provider type to build an execution instance from.
        var byName = new Dictionary<string, (ConstructorInfo Ctor, string ConfigKey, string AssemblyFile)>(
            StringComparer.OrdinalIgnoreCase);

        lock (_poolLock)
        {
            // 1. Capability instance per provider type (model unbound).
            foreach (var desc in _descriptors)
                foreach (var (type, ctor) in desc.Providers)
                {
                    var cfgKey = ConfigKey(ctor, profileConfigs);
                    var key    = $"{type.FullName}|cap|{cfgKey}";
                    var entry  = Acquire(key, ctor, profileConfigs, model: "", desc.FileName, toWarm);

                    capability[entry.Provider.Name]  = entry.Provider;
                    assemblyMap[entry.Provider.Name] = desc.FileName;
                    byName[entry.Provider.Name]      = (ctor, cfgKey, desc.FileName);
                    poolKeys.Add(key);
                }

            // 2. Execution instance per assigned (provider, model) column.
            foreach (var col in columns)
            {
                if (string.IsNullOrEmpty(col.Model)) continue;
                if (!byName.TryGetValue(col.ProviderName, out var t)) continue;   // provider not available

                var key   = $"{t.Ctor.DeclaringType!.FullName}|exec|{col.Model}|{t.ConfigKey}";
                var entry = Acquire(key, t.Ctor, profileConfigs, col.Model, t.AssemblyFile, toWarm);

                execution[col.Id] = entry.Provider;
                poolKeys.Add(key);
            }
        }

        foreach (var p in toWarm) _ = WarmCoolSafe(p.WarmupAsync());

        return new ProviderSet(capability, execution, profileConfigs, assemblyMap, poolKeys);
    }

    /// <summary>Gets or creates a pooled instance for <paramref name="key"/>, incrementing its ref-count.
    /// A newly-created model-bound (execution) instance is queued for warm-up. Caller holds the lock.</summary>
    private PoolEntry Acquire(
        string key, ConstructorInfo ctor, IReadOnlyList<IProviderConfig> configs,
        string model, string assemblyFile, List<ILlmProvider> toWarm)
    {
        if (!_pool.TryGetValue(key, out var entry))
        {
            entry = new PoolEntry
            {
                Provider     = Instantiate(ctor, configs, model),
                AssemblyFile = assemblyFile,
                Model        = model
            };
            _pool[key] = entry;
            if (!string.IsNullOrEmpty(model)) toWarm.Add(entry.Provider);
        }
        entry.RefCount++;
        return entry;
    }

    /// <summary>Builds a provider, injecting the activity manager, the bound <see cref="ProviderModel"/>,
    /// and any config the constructor declares (matched by type).</summary>
    private ILlmProvider Instantiate(ConstructorInfo ctor, IReadOnlyList<IProviderConfig> configs, string model)
    {
        var args = ctor.GetParameters().Select(p =>
            typeof(IBackgroundActivityManager).IsAssignableFrom(p.ParameterType) ? (object?)_activityManager
            : typeof(IHostCapabilityService).IsAssignableFrom(p.ParameterType)    ? _hostCapabilities
            : p.ParameterType == typeof(ProviderModel)                           ? new ProviderModel(model)
            : configs.FirstOrDefault(c => c.GetType() == p.ParameterType))
            .ToArray();
        return (ILlmProvider)ctor.Invoke(args);
    }

    /// <summary>Stable key fragment for the configs feeding a provider's constructor (model-independent).</summary>
    private static string ConfigKey(ConstructorInfo ctor, IReadOnlyList<IProviderConfig> configs)
    {
        var feeding = ctor.GetParameters()
            .Select(p => configs.FirstOrDefault(c => c.GetType() == p.ParameterType))
            .Where(c => c is not null)
            .OrderBy(c => c!.GetType().FullName, StringComparer.Ordinal);
        return string.Join("|", feeding.Select(c => JsonSerializer.Serialize(c, c!.GetType(), _keyOpts)));
    }

    /// <summary>
    /// Releases the pool references held by <paramref name="set"/>. Any instance whose ref-count reaches
    /// zero is removed and disposed (if <see cref="IDisposable"/>) — "unloaded"; a model-bound execution
    /// instance is cooled down first so its model is unloaded from the backend.
    /// </summary>
    public void ReleaseProviderSet(ProviderSet? set)
    {
        if (set is null) return;
        var toCool = new List<ILlmProvider>();
        lock (_poolLock)
        {
            foreach (var key in set.PoolKeys)
            {
                if (!_pool.TryGetValue(key, out var entry)) continue;
                if (--entry.RefCount > 0) continue;
                _pool.Remove(key);
                if (!string.IsNullOrEmpty(entry.Model)) toCool.Add(entry.Provider);
                (entry.Provider as IDisposable)?.Dispose();
            }
        }
        foreach (var p in toCool) _ = WarmCoolSafe(p.CooldownAsync());
    }

    private static async Task WarmCoolSafe(Task t)
    {
        try { await t; } catch { /* warm/cool are best-effort; providers must not throw */ }
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
