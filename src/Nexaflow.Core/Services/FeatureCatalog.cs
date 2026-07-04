using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.Core.Services;

/// <summary>
/// Persistent discovery index for feature assemblies. Replaces the old eager "load every
/// <c>Nexaflow.Features.*.dll</c> and reflect over it at startup" scan: the discovered type metadata
/// is cached to disk (<c>discovery/catalog.json</c>) so a normal launch builds the index from JSON with
/// <b>no assembly loads</b>. Feature assemblies are loaded lazily — only when a type they own is actually
/// resolved (the first page, a handler query, or the post-paint background warm-up). The cache is stamped
/// with the app version and trusted whenever the stamp matches; an app update (the only thing that changes
/// the DLLs — the same signal that drives the What's New popup) bumps the version and forces one full rescan.
///
/// The catalog is a pure discovery/loading engine: the side-effects of "activating" an assembly (registering
/// its global configs / archive handlers / theme contributions) are performed by a callback supplied at
/// <see cref="Initialize"/> (see <see cref="FeatureManager"/>), so Core's "what to do with a discovered type"
/// logic stays in one place.
/// </summary>
public sealed class FeatureCatalog
{
    public static FeatureCatalog Instance { get; } = new();

    // ── Serialized index model (public for System.Text.Json) ──────────────────

    public sealed class TypeEntry
    {
        public string Name { get; set; } = "";                 // full type name
        public List<string> Contracts { get; set; } = [];      // implemented Nexaflow.* interface full names
        public string? PageKind { get; set; }                  // IPageRegistration.StaticPageKind
        public List<string>? ConfigDeps { get; set; }          // page reg: ctor IFeatureConfig param type names
        public string? ConfigName { get; set; }                // IFeatureConfig.ConfigName
        public bool Scoped { get; set; }                       // [WorkspaceScopedConfig]
        public string? ExperienceId { get; set; }              // IFileAction.StaticExperienceId
        public List<string>? ThemeUris { get; set; }           // IThemeContribution.ResourceDictionaryUris
    }

    public sealed class AssemblyEntry
    {
        public string File { get; set; } = "";                 // "Nexaflow.Features.X.dll"
        public string Version { get; set; } = "";
        public List<TypeEntry> Types { get; set; } = [];
    }

    public sealed class CatalogIndex
    {
        public string Version { get; set; } = "";              // app-version stamp
        public List<AssemblyEntry> Assemblies { get; set; } = [];
    }

    // ── Runtime state ─────────────────────────────────────────────────────────

    private readonly object _lock = new();
    private CatalogIndex _index = new();
    private readonly Dictionary<string, AssemblyEntry> _byFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Assembly> _loaded      = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activated                = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string File, string Type)> _pageKinds = new(StringComparer.OrdinalIgnoreCase);

    private Action<Assembly, AssemblyEntry>? _onActivated;
    private string _exeDir = "";
    private string _coreFile = "";

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Initialization ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the discovery index (from the on-disk cache when its version matches the running app,
    /// otherwise a one-time full scan that rewrites the cache) and activates Core. No feature assemblies
    /// are loaded on the cache-hit path. <paramref name="onActivated"/> is invoked the first time each
    /// assembly is loaded, on whatever thread triggered the load.
    /// </summary>
    public void Initialize(Action<Assembly, AssemblyEntry> onActivated)
    {
        _onActivated = onActivated;
        _exeDir   = Path.GetDirectoryName(typeof(FeatureCatalog).Assembly.Location)!;
        _coreFile = Path.GetFileName(typeof(FeatureCatalog).Assembly.Location);

        var version = SetupWizardViewModel.CurrentVersion();
        var cached  = TryLoadCache();
        bool rebuilt;

        if (cached is not null && string.Equals(cached.Version, version, StringComparison.Ordinal))
        {
            _index = cached;
            rebuilt = false;
            Debug.WriteLine($"[FeatureCatalog] cache hit ({_index.Assemblies.Count} assemblies); no feature DLLs scanned.");
        }
        else
        {
            var sw = Stopwatch.StartNew();
            _index = BuildIndex(version);
            SaveCache(_index);
            rebuilt = true;
            Debug.WriteLine($"[FeatureCatalog] full scan of {_index.Assemblies.Count} assemblies in {sw.ElapsedMilliseconds} ms (cache rebuilt).");
        }

        lock (_lock)
        {
            foreach (var a in _index.Assemblies)
            {
                _byFile[a.File] = a;
                foreach (var t in a.Types)
                    if (t.PageKind is { } pk) _pageKinds[pk] = (a.File, t.Name);
            }
        }

        // Core is already loaded and owns registrations/handlers/configs — activate it now.
        EnsureLoaded(_coreFile);

        // A full scan happens on first run / post-update — exactly when the setup wizard runs and needs
        // every feature's global config registered (its [MandatorySetup] steps read ConfigManager.GetAll).
        // The scan already loaded every assembly, so activate them all now to match the old eager behavior;
        // the common cache-hit launch stays lazy (assemblies load on demand / via the background warm-up).
        if (rebuilt) EnsureAllActivated();
    }

    /// <summary>Loads + activates every indexed assembly synchronously (idempotent). Used where the
    /// complete set must be present now — the first-run/update scan, and the Options panel (which lists
    /// every feature's global config).</summary>
    public void EnsureAllActivated()
    {
        List<string> files;
        lock (_lock) files = _index.Assemblies.Select(a => a.File).ToList();
        foreach (var f in files)
            try { EnsureLoaded(f); } catch { }
    }

    // ── Full scan (post-update / first run only) ──────────────────────────────

    private CatalogIndex BuildIndex(string version)
    {
        var idx = new CatalogIndex { Version = version };

        var files = Directory.GetFiles(_exeDir, "Nexaflow.Features.*.dll")
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();
        if (!files.Contains(_coreFile, StringComparer.OrdinalIgnoreCase)) files.Add(_coreFile);

        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var asm = LoadOrFind(file);
            if (asm is null) continue;
            var entry = ScanAssembly(asm, file);
            if (entry.Types.Count > 0) idx.Assemblies.Add(entry);
        }
        return idx;
    }

    private static AssemblyEntry ScanAssembly(Assembly asm, string file)
    {
        var entry = new AssemblyEntry { File = file, Version = asm.GetName().Version?.ToString() ?? "" };

        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

        foreach (var t in types)
        {
            if (t is null || t.IsAbstract || t.IsInterface) continue;

            var contracts = t.GetInterfaces()
                .Where(i => i.FullName is { } fn && fn.StartsWith("Nexaflow.", StringComparison.Ordinal))
                .Select(i => i.FullName!)
                .Distinct()
                .ToList();
            if (contracts.Count == 0) continue;

            var te = new TypeEntry { Name = t.FullName!, Contracts = contracts };

            if (typeof(IPageRegistration).IsAssignableFrom(t))
            {
                te.PageKind   = StaticString(t, "StaticPageKind");
                te.ConfigDeps = BestCtorConfigDeps(t);
            }
            if (typeof(IFeatureConfig).IsAssignableFrom(t))
            {
                te.Scoped     = t.GetCustomAttribute<WorkspaceScopedConfigAttribute>() is not null;
                te.ConfigName = TryRead(() => (TryInstantiate(t) as IFeatureConfig)?.ConfigName);
            }
            if (typeof(IFileAction).IsAssignableFrom(t))
                te.ExperienceId = StaticString(t, "StaticExperienceId");
            if (typeof(IThemeContribution).IsAssignableFrom(t))
                // The getter may build pack:// URIs, which throw in a headless scan (no WPF Application —
                // e.g. under unit tests / CI). The real first-run/update scan runs with WPF up, so it
                // captures them; headless just leaves them null (harmless — they're re-read at activation).
                te.ThemeUris = TryRead(() => (TryInstantiate(t) as IThemeContribution)?.ResourceDictionaryUris
                    .Select(u => u.ToString()).ToList());

            entry.Types.Add(te);
        }
        return entry;
    }

    private static string? StaticString(Type t, string propName)
        => t.GetProperty(propName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            ?.GetValue(null) as string;

    private static List<string>? BestCtorConfigDeps(Type t)
    {
        var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        if (ctor is null) return null;
        var deps = ctor.GetParameters()
            .Where(p => typeof(IFeatureConfig).IsAssignableFrom(p.ParameterType))
            .Select(p => p.ParameterType.FullName!)
            .Where(n => n is not null)
            .ToList();
        return deps.Count > 0 ? deps : null;
    }

    private static object? TryInstantiate(Type t)
    {
        try { return Activator.CreateInstance(t); }
        catch { return null; }
    }

    // Reads metadata that requires instantiating the type (config name, theme URIs); a getter that throws
    // (e.g. a pack:// URI built in a headless scan) just yields null instead of aborting the whole index.
    private static T? TryRead<T>(Func<T?> read)
    {
        try { return read(); }
        catch { return default; }
    }

    // ── Lazy load + activation ────────────────────────────────────────────────

    /// <summary>
    /// Ensures the assembly behind <paramref name="file"/> is loaded and activated (its first load runs the
    /// activation callback). Returns the loaded assembly, or null if it could not be found/loaded.
    /// </summary>
    public Assembly? EnsureLoaded(string file)
    {
        // Monitor is reentrant, so the activation callback may resolve more types on this thread safely.
        lock (_lock)
        {
            if (!_loaded.TryGetValue(file, out var asm))
            {
                asm = LoadOrFind(file);
                if (asm is null) return null;
                _loaded[file] = asm;
            }

            if (_activated.Add(file) && _byFile.TryGetValue(file, out var entry) && _onActivated is { } cb)
            {
                try { cb(asm, entry); }
                catch (Exception ex) { Debug.WriteLine($"[FeatureCatalog] activate {file} failed: {ex.Message}"); }
            }
            return asm;
        }
    }

    private Assembly? LoadOrFind(string file)
    {
        var simpleName = Path.GetFileNameWithoutExtension(file);
        var existing = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        try { return Assembly.LoadFrom(Path.Combine(_exeDir, file)); }
        catch (Exception ex) { Debug.WriteLine($"[FeatureCatalog] load {file} failed: {ex.Message}"); return null; }
    }

    /// <summary>Resolves a type by (assembly file, full name), loading + activating the assembly if needed.</summary>
    public Type? ResolveType(string file, string typeName) => EnsureLoaded(file)?.GetType(typeName);

    // ── Query API ─────────────────────────────────────────────────────────────

    /// <summary>Every concrete type implementing <typeparamref name="T"/>, resolving (loading + activating)
    /// the owning assemblies. Index-backed replacement for the old AppDomain <c>GetTypes()</c> walk, so the
    /// result is complete regardless of whether the warm-up has finished.</summary>
    public IReadOnlyList<Type> TypesImplementing<T>() => TypesImplementing(typeof(T));

    public IReadOnlyList<Type> TypesImplementing(Type contract)
    {
        var key = contract.FullName;
        if (key is null) return [];

        var refs = SnapshotRefs(t => t.Contracts.Contains(key));
        return ResolveAll(refs);
    }

    /// <summary>Resolves the registration type for <paramref name="pageKind"/> (loading its assembly), or null.</summary>
    public bool TryGetPageRegistrationType(string pageKind, out Type? type)
    {
        type = null;
        (string File, string Type) r;
        lock (_lock) { if (!_pageKinds.TryGetValue(pageKind, out r)) return false; }
        type = ResolveType(r.File, r.Type);
        return type is not null;
    }

    public bool HasPageKind(string pageKind) { lock (_lock) return _pageKinds.ContainsKey(pageKind); }

    public IReadOnlyList<string> PageKinds() { lock (_lock) return _pageKinds.Keys.ToList(); }

    /// <summary>The version of the feature assembly that owns <paramref name="pageKind"/> — index-only, no load.</summary>
    public string? GetPageKindVersion(string pageKind)
    {
        lock (_lock)
            return _pageKinds.TryGetValue(pageKind, out var r) && _byFile.TryGetValue(r.File, out var a)
                ? a.Version : null;
    }

    /// <summary>True if <paramref name="configTypeFullName"/> is a <c>[WorkspaceScopedConfig]</c> — index-only, no load.</summary>
    public bool IsWorkspaceScopedConfig(string configTypeFullName)
    {
        lock (_lock)
            return _index.Assemblies.Any(a => a.Types.Any(
                t => t.Scoped && t.ConfigName is not null
                     && string.Equals(t.Name, configTypeFullName, StringComparison.Ordinal)));
    }

    /// <summary>All workspace-scoped config types, resolving (loading) their assemblies. Used by the
    /// Manage-AI / Configure panels, which need every scoped config materialized.</summary>
    public IReadOnlyList<Type> ResolveWorkspaceScopedConfigTypes()
        => ResolveAll(SnapshotRefs(t => t.Scoped && t.ConfigName is not null));

    /// <summary>Workspace-scoped config types whose assembly is <b>already loaded</b> — never triggers a new
    /// load. Lets the per-feature config view include a loaded feature's scoped config without forcing the
    /// not-yet-loaded ones.</summary>
    public IReadOnlyList<Type> ResolveLoadedWorkspaceScopedConfigTypes()
    {
        List<(string File, string Type)> refs = [];
        lock (_lock)
            foreach (var a in _index.Assemblies)
                if (_loaded.ContainsKey(a.File))
                    foreach (var t in a.Types)
                        if (t.Scoped && t.ConfigName is not null) refs.Add((a.File, t.Name));
        return ResolveAll(refs);
    }

    /// <summary>Page-registration types whose constructor depends on <paramref name="configTypeFullName"/>,
    /// resolving (loading) their assemblies. Backs "which pages does this config affect" on config save.</summary>
    public IReadOnlyList<Type> ResolvePageRegistrationsForConfig(string configTypeFullName)
        => ResolveAll(SnapshotRefs(t => t.PageKind is not null
                                        && t.ConfigDeps is { } d && d.Contains(configTypeFullName)));

    /// <summary>Loads + activates the assembly that defines <paramref name="configTypeFullName"/>, so its
    /// global config gets registered. Returns true if such an assembly was found.</summary>
    public bool ActivateAssemblyOwning(string configTypeFullName)
    {
        string? file = null;
        lock (_lock)
            file = _index.Assemblies.FirstOrDefault(a =>
                a.Types.Any(t => string.Equals(t.Name, configTypeFullName, StringComparison.Ordinal)))?.File;
        if (file is null) return false;
        EnsureLoaded(file);
        return true;
    }

    /// <summary>Loads + activates every feature assembly not already loaded. Run on a background thread after
    /// the first window paints; <paramref name="priorityFiles"/> (matched by filename substring) go first.</summary>
    public void WarmUpAll(IReadOnlyList<string>? priorityFiles = null)
    {
        List<string> files;
        lock (_lock) files = _index.Assemblies.Select(a => a.File).ToList();

        if (priorityFiles is { Count: > 0 })
            files = files
                .OrderByDescending(f => priorityFiles.Any(p => f.Contains(p, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        int activated = 0;
        foreach (var f in files)
        {
            bool already; lock (_lock) already = _activated.Contains(f);
            if (already) continue;
            try { EnsureLoaded(f); activated++; } catch { }
        }
        Debug.WriteLine($"[FeatureCatalog] warm-up activated {activated} feature assembly(ies).");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private List<(string File, string Type)> SnapshotRefs(Func<TypeEntry, bool> match)
    {
        List<(string File, string Type)> refs = [];
        lock (_lock)
            foreach (var a in _index.Assemblies)
                foreach (var t in a.Types)
                    if (match(t)) refs.Add((a.File, t.Name));
        return refs;
    }

    private IReadOnlyList<Type> ResolveAll(List<(string File, string Type)> refs)
    {
        var list = new List<Type>(refs.Count);
        foreach (var r in refs)
            if (ResolveType(r.File, r.Type) is { } ty) list.Add(ty);
        return list;
    }

    // ── Cache persistence ─────────────────────────────────────────────────────

    private static string CachePath => Path.Combine(ConfigManager.Instance.BaseDir, "discovery", "catalog.json");

    private static CatalogIndex? TryLoadCache()
    {
        try
        {
            if (File.Exists(CachePath))
                return JsonSerializer.Deserialize<CatalogIndex>(File.ReadAllText(CachePath), _json);
        }
        catch (Exception ex) { Debug.WriteLine($"[FeatureCatalog] cache read failed: {ex.Message}"); }
        return null;
    }

    private static void SaveCache(CatalogIndex idx)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(idx, _json));
        }
        catch (Exception ex) { Debug.WriteLine($"[FeatureCatalog] cache write failed: {ex.Message}"); }
    }
}
