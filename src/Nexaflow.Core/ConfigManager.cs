using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexaflow.Core;

/// <summary>
/// Singleton registry that loads and persists config POCOs to
/// %AppData%\Smile\nexaflow\{configName}\config.json.
/// Errors are thrown rather than swallowed so the shell can surface them as toasts.
/// </summary>
public sealed class ConfigManager
{
    public static ConfigManager Instance { get; } = new();

    private readonly List<object>    _configs = [];
    private readonly HashSet<string> _seen    = new(StringComparer.OrdinalIgnoreCase);

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

    private static string GetPath(string configName) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Smile", "nexaflow", configName, "config.json");

    /// <summary>
    /// Registers a config POCO and populates its properties from disk.
    /// Duplicate <paramref name="configName"/> values are silently ignored (first wins).
    /// Throws <see cref="IOException"/> or <see cref="JsonException"/> if the file exists but is unreadable.
    /// </summary>
    public void Register(object config, string configName)
    {
        if (!_seen.Add(configName)) return;

        var path = GetPath(configName);
        if (File.Exists(path))
        {
            IsFirstRun = false;
            Load(config, path);
        }

        _configs.Add(config);
    }

    /// <summary>All registered config POCOs in registration order.</summary>
    public IReadOnlyList<object> GetAll() => _configs.AsReadOnly();

    /// <summary>
    /// Persists <paramref name="config"/> to its JSON file.
    /// Throws <see cref="IOException"/> on write failure.
    /// </summary>
    public void Save(object config, string configName)
    {
        var path = GetPath(configName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, config.GetType(), _opts));
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
