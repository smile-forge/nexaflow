using System.IO;
using System.Text.Json;
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
    private readonly HashSet<string> _defaultedConfigs = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    private ConfigManager() { }

    /// <summary>
    /// True if no persisted config files existed for any registered config (first run).
    /// Set to false as soon as any config file is found on disk.
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

    private string GetConfigDir(string configName) => Path.Combine(BaseDir, configName);

    private string GetPath(string configName, Version version) =>
        Path.Combine(GetConfigDir(configName), $"config_{version}.json");

    /// <summary>
    /// Registers a config POCO and populates its properties from disk.
    /// Duplicate <paramref name="configName"/> values are silently ignored (first wins).
    /// If a config file exists for a different assembly version it is deleted and the config
    /// defaults. Throws <see cref="IOException"/> or <see cref="JsonException"/> if the
    /// matching file exists but is unreadable.
    /// </summary>
    public void Register(object config, string configName)
    {
        if (!_seen.Add(configName)) return;

        var version      = config.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        var expectedPath = GetPath(configName, version);
        var dir          = GetConfigDir(configName);

        if (File.Exists(expectedPath))
        {
            IsFirstRun = false;
            Load(config, expectedPath);
        }
        else
        {
            // Delete any stale versioned files left by a previous assembly version
            if (Directory.Exists(dir))
            {
                foreach (var stale in Directory.GetFiles(dir, "config_*.json"))
                    File.Delete(stale);
            }
            _defaultedConfigs.Add(configName);
        }

        _configs.Add(config);
    }

    /// <summary>All registered config POCOs in registration order.</summary>
    public IReadOnlyList<object> GetAll() => _configs.AsReadOnly();

    /// <summary>
    /// Config names whose config file was absent or version-mismatched on load,
    /// causing the config to be initialised with default values.
    /// An entry is removed once the config is successfully saved.
    /// </summary>
    public IReadOnlyList<string> GetDefaultedConfigs() =>
        _defaultedConfigs.ToList().AsReadOnly();

    /// <summary>
    /// Persists <paramref name="config"/> to its versioned JSON file.
    /// Throws <see cref="IOException"/> on write failure.
    /// </summary>
    public void Save(object config, string configName)
    {
        var version = config.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        var path    = GetPath(configName, version);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, config.GetType(), _opts));
        _defaultedConfigs.Remove(configName);
    }

    /// <summary>
    /// Persists <paramref name="config"/> under an arbitrary <paramref name="directory"/> (e.g. a
    /// per-work-context folder) using the same versioned-file layout as <see cref="Save"/>. Used
    /// for per-context configs that are not part of the global registry.
    /// </summary>
    public void SaveTo(string directory, object config, string configName)
    {
        var version = config.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        var path    = Path.Combine(directory, configName, $"config_{version}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, config.GetType(), _opts));
    }

    /// <summary>
    /// Populates <paramref name="config"/> from its versioned file under <paramref name="directory"/>,
    /// if present. No-op when the file doesn't exist (config keeps its current/default values).
    /// </summary>
    public void LoadFrom(string directory, object config, string configName)
    {
        var version = config.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        var path    = Path.Combine(directory, configName, $"config_{version}.json");
        if (File.Exists(path)) Load(config, path);
    }

    /// <summary>Creates a deep clone of a config POCO via JSON round-trip.</summary>
    public static object Clone(object config)
    {
        var json = JsonSerializer.Serialize(config, config.GetType(), _opts);
        return JsonSerializer.Deserialize(json, config.GetType(), _opts)!;
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
