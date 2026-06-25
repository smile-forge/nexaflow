using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Nexaflow.Core;

/// <summary>
/// Singleton registry that loads and persists config POCOs to
/// %AppData%\Smile\nexaflow\{configName}\config_{version}.json.
/// Errors are thrown rather than swallowed so the shell can surface them as toasts.
/// </summary>
public sealed class ConfigManager
{
    public static ConfigManager Instance { get; } = new();

    private readonly List<object>    _configs         = [];
    private readonly HashSet<string> _seen            = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _defaultedConfigs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _migratedConfigs  = new(StringComparer.OrdinalIgnoreCase);

    private bool _suppressWrites;

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    private ConfigManager() { }

    /// <summary>
    /// True if no persisted config files existed for any registered config (first run).
    /// Set to false as soon as any config file (current or an older, migratable version) is found.
    /// </summary>
    public bool IsFirstRun { get; private set; } = true;

    /// <summary>
    /// Application base directory — all config subdirectories are created beneath this.
    /// Defaults to <c>%AppData%\Smile\nexaflow</c>. Call <see cref="Initialize"/> from
    /// App.xaml.cs before the first <see cref="Register"/> call to override.
    /// </summary>
    public string BaseDir { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Smile", "nexaflow");

    /// <summary>
    /// Sets the application base directory. Must be called once before any
    /// <see cref="Register"/> or <see cref="Save"/> calls so the path is defined
    /// in a single place (App.xaml.cs).
    /// </summary>
    public void Initialize(string baseDir) => BaseDir = baseDir;

    /// <summary>
    /// Permanently stops all config writes for the remainder of the process. Armed just before a
    /// "Reset configuration" relaunch so nothing re-persists into the directory being deleted.
    /// </summary>
    public void SuppressWrites() => _suppressWrites = true;

    private string GetConfigDir(string configName) => Path.Combine(BaseDir, configName);

    private string GetPath(string configName, Version version) =>
        Path.Combine(GetConfigDir(configName), $"config_{version}.json");

    /// <summary>
    /// Registers a config POCO and populates its properties from disk, returning the
    /// <em>canonical</em> instance for <paramref name="configName"/>. Duplicate names keep the
    /// first registration (first wins); a later caller gets that original instance back rather than
    /// its own — so every consumer of a given config shares one object. Callers that may register a
    /// config already wired by startup (e.g. <see cref="FeatureManager"/> vs App.xaml.cs) must use
    /// the returned value, not the instance they passed in.
    /// When only an older assembly version's file exists, its data is migrated forward (see
    /// <see cref="LoadOrMigrate"/>) rather than discarded. Throws <see cref="IOException"/> or
    /// <see cref="JsonException"/> if a present file is unreadable.
    /// </summary>
    public object Register(object config, string configName)
    {
        if (!_seen.Add(configName)) return _byName[configName];

        var version = config.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        switch (LoadOrMigrate(config, BaseDir, configName, version))
        {
            case LoadOutcome.Loaded:
                IsFirstRun = false;
                break;
            case LoadOutcome.Migrated:
                IsFirstRun = false;
                _migratedConfigs.Add(configName);
                break;
            case LoadOutcome.None:
                _defaultedConfigs.Add(configName);
                break;
        }

        _configs.Add(config);
        _byName[configName] = config;
        return config;
    }

    /// <summary>All registered config POCOs in registration order.</summary>
    public IReadOnlyList<object> GetAll() => _configs.AsReadOnly();

    /// <summary>
    /// Config names that had no file on disk for any version on load (brand-new config / first run),
    /// so the config was initialised with default values. An entry is removed once the config is saved.
    /// </summary>
    public IReadOnlyList<string> GetDefaultedConfigs() =>
        _defaultedConfigs.ToList().AsReadOnly();

    /// <summary>
    /// Config names whose data was carried forward from an older on-disk version on load (migrated,
    /// not defaulted). Lets the setup wizard re-prompt only when migrated data is still incomplete.
    /// An entry is removed once the config is saved.
    /// </summary>
    public IReadOnlyList<string> GetMigratedConfigs() =>
        _migratedConfigs.ToList().AsReadOnly();

    /// <summary>
    /// Persists <paramref name="config"/> to its versioned JSON file.
    /// Throws <see cref="IOException"/> on write failure.
    /// </summary>
    public void Save(object config, string configName)
    {
        if (_suppressWrites) return;
        var version = config.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        var path    = GetPath(configName, version);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, config.GetType(), _opts));
        _defaultedConfigs.Remove(configName);
        _migratedConfigs.Remove(configName);
    }

    /// <summary>
    /// Persists <paramref name="config"/> under an arbitrary <paramref name="directory"/> (e.g. a
    /// per-work-context folder) using the same versioned-file layout as <see cref="Save"/>. Used
    /// for per-context configs that are not part of the global registry.
    /// </summary>
    public void SaveTo(string directory, object config, string configName)
    {
        if (_suppressWrites) return;
        var version = config.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        var path    = Path.Combine(directory, configName, $"config_{version}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, config.GetType(), _opts));
    }

    /// <summary>
    /// Populates <paramref name="config"/> from its versioned file under <paramref name="directory"/>,
    /// migrating an older version's file forward when the current one is absent (see
    /// <see cref="LoadOrMigrate"/>). No-op only when no file of any version exists (config keeps its
    /// current/default values).
    /// </summary>
    public void LoadFrom(string directory, object config, string configName)
    {
        var version = config.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        LoadOrMigrate(config, directory, configName, version);
    }

    /// <summary>Creates a deep clone of a config POCO via JSON round-trip.</summary>
    public static object Clone(object config)
    {
        var json = JsonSerializer.Serialize(config, config.GetType(), _opts);
        return JsonSerializer.Deserialize(json, config.GetType(), _opts)!;
    }

    private enum LoadOutcome { None, Loaded, Migrated }

    /// <summary>
    /// Loads <paramref name="config"/> from its current-version file under
    /// <c>{parentDir}\{configName}\config_{version}.json</c>. When that file is absent but an older
    /// version's file exists, its data is migrated forward: the newest older file is loaded
    /// (lenient field-by-field carry-over of every name-matching property), an optional
    /// <see cref="Nexaflow.Features.Common.IConfigMigration"/> /
    /// <see cref="Nexaflow.Providers.Common.IConfigMigration"/> hook fixes up renames/restructures,
    /// the result is written under the current version, and the stale files are removed
    /// (write-then-delete so a failed write never loses the prior data).
    /// </summary>
    private LoadOutcome LoadOrMigrate(object config, string parentDir, string configName, Version version)
    {
        var dir   = Path.Combine(parentDir, configName);
        var exact = Path.Combine(dir, $"config_{version}.json");

        if (File.Exists(exact))
        {
            Load(config, exact);
            return LoadOutcome.Loaded;
        }

        if (!Directory.Exists(dir)) return LoadOutcome.None;

        // The newest older versioned file becomes the migration source.
        var prior = Directory.GetFiles(dir, "config_*.json")
            .Select(p => (path: p, ver: ParseConfigVersion(p)))
            .Where(x => x.ver is not null)
            .OrderByDescending(x => x.ver!)
            .FirstOrDefault();
        if (prior.path is null) return LoadOutcome.None;

        var rawText = File.ReadAllText(prior.path);
        Load(config, prior.path);   // lenient carry-over of all name-matching fields

        // Optional custom upgrade path for shape changes the field copy can't express (renames etc.).
        if (JsonNode.Parse(rawText) is JsonObject previous)
        {
            if (config is Nexaflow.Features.Common.IConfigMigration fm)
                fm.MigrateFrom(previous, prior.ver!);
            else if (config is Nexaflow.Providers.Common.IConfigMigration pm)
                pm.MigrateFrom(previous, prior.ver!);
        }

        if (!_suppressWrites)
        {
            File.WriteAllText(exact, JsonSerializer.Serialize(config, config.GetType(), _opts));
            foreach (var stale in Directory.GetFiles(dir, "config_*.json"))
                if (!string.Equals(stale, exact, StringComparison.OrdinalIgnoreCase))
                    File.Delete(stale);
        }
        return LoadOutcome.Migrated;
    }

    /// <summary>Parses the version out of a <c>config_{version}.json</c> path, or null if it doesn't match.</summary>
    private static Version? ParseConfigVersion(string path)
    {
        const string prefix = "config_";
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && Version.TryParse(name[prefix.Length..], out var v) ? v : null;
    }

    private static void Load(object config, string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var props = config.GetType().GetProperties()
                          .Where(p => p.CanWrite)
                          .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var el in doc.RootElement.EnumerateObject())
        {
            if (!props.TryGetValue(el.Name, out var pi)) continue;
            var value = JsonSerializer.Deserialize(el.Value.GetRawText(), pi.PropertyType, _opts);
            pi.SetValue(config, value);
        }
    }
}
