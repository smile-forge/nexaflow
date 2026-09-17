using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Nexaflow.Core.Services;

namespace Nexaflow.Core;

/// <summary>
/// Singleton registry that loads and persists config POCOs to
/// %AppData%\Smile\nexaflow\{configName}\config_{version}.json.
/// A file that cannot be read or is not JSON throws; a single stored value that no longer reads as its property's
/// type keeps the property's default and is recorded in the crash log, so one stale setting never stops startup.
/// </summary>
public sealed class ConfigManager
{
    public static ConfigManager Instance { get; } = new();

    private readonly List<object>    _configs         = [];
    private readonly HashSet<string> _seen            = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _defaultedConfigs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _migratedConfigs  = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _unreadableConfigs = new(StringComparer.OrdinalIgnoreCase);

    // Guards all mutable state: feature-assembly activation registers configs on the background warm-up
    // thread, while the UI thread registers/saves/reads concurrently. Reentrant (Monitor) so a method that
    // calls another locked member on the same thread is safe.
    private readonly object _gate = new();

    private bool _suppressWrites;

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    // ── [Secret] properties: DPAPI at rest ────────────────────────────────
    // String properties marked [Secret] (Features.Common or Providers.Common — matched by name, the
    // IConfigMigration mirroring precedent) are written as "enc:<base64>" (DPAPI, current user) and
    // decrypted on load. Legacy plaintext passes through and becomes encrypted on the next save.

    private const string EncPrefix = "enc:";

    private static bool IsSecret(System.Reflection.PropertyInfo pi)
        => pi.PropertyType == typeof(string)
           && pi.GetCustomAttributes(inherit: true).Any(a => a.GetType().Name == nameof(Nexaflow.Features.Common.SecretAttribute));

    /// <summary>Serializes a config, encrypting its [Secret] string properties for disk.</summary>
    private static string SerializeProtected(object config)
    {
        var node = JsonSerializer.SerializeToNode(config, config.GetType(), _opts)!.AsObject();
        foreach (var pi in config.GetType().GetProperties().Where(IsSecret))
        {
            if (node[pi.Name] is not JsonValue v || v.GetValueKind() != JsonValueKind.String) continue;
            var plain = v.GetValue<string>();
            if (string.IsNullOrEmpty(plain) || plain.StartsWith(EncPrefix, StringComparison.Ordinal)) continue;
            var protectedBytes = System.Security.Cryptography.ProtectedData.Protect(
                System.Text.Encoding.UTF8.GetBytes(plain), null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            node[pi.Name] = EncPrefix + Convert.ToBase64String(protectedBytes);
        }
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Reverses <see cref="SerializeProtected"/> for one loaded value. A value that fails to
    /// decrypt (file copied from another user/machine) loads as empty so the required-field check
    /// re-prompts instead of feeding garbage to a provider.</summary>
    private static string UnprotectValue(string stored)
    {
        if (!stored.StartsWith(EncPrefix, StringComparison.Ordinal)) return stored;   // legacy plaintext
        try
        {
            var bytes = System.Security.Cryptography.ProtectedData.Unprotect(
                Convert.FromBase64String(stored[EncPrefix.Length..]), null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch { return string.Empty; }
    }

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

    /// <summary>
    /// Where a stored value that could not be read is put on record: the crash log, so a setting that fell back to its
    /// default is traceable rather than silently gone. Recording needs no dispatcher, so it is safe under the config
    /// lock on any thread. The Core test suite points it at a log of its own.
    /// </summary>
    internal CrashLog FaultLog { get; set; } = CrashLog.Instance;

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
    /// <see cref="LoadOrMigrate"/>) rather than discarded. Throws <see cref="IOException"/> when a
    /// present file cannot be read and <see cref="JsonException"/> when it is not JSON; a single stored value that no
    /// longer reads as its property's type keeps the default instead (see <see cref="Load"/>).
    /// </summary>
    public object Register(object config, string configName)
    {
        lock (_gate)
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
                case LoadOutcome.Unreadable:
                    // Not a first run — there WAS a file. It is tracked as defaulted (that is what the
                    // config now holds, so every existing consumer stays right) and separately as
                    // unreadable, which is the signal the user is told about and the wizard re-asks on.
                    IsFirstRun = false;
                    _defaultedConfigs.Add(configName);
                    _unreadableConfigs.Add(configName);
                    break;
            }

            _configs.Add(config);
            _byName[configName] = config;
            return config;
        }
    }

    /// <summary>All registered config POCOs in registration order.</summary>
    public IReadOnlyList<object> GetAll() { lock (_gate) return _configs.ToList(); }

    /// <summary>
    /// Config names that had no file on disk for any version on load (brand-new config / first run),
    /// so the config was initialised with default values. An entry is removed once the config is saved.
    /// </summary>
    public IReadOnlyList<string> GetDefaultedConfigs()
    { lock (_gate) return _defaultedConfigs.ToList(); }

    /// <summary>
    /// Config names whose data was carried forward from an older on-disk version on load (migrated,
    /// not defaulted). Lets the setup wizard re-prompt only when migrated data is still incomplete.
    /// An entry is removed once the config is saved.
    /// </summary>
    public IReadOnlyList<string> GetMigratedConfigs()
    { lock (_gate) return _migratedConfigs.ToList(); }

    /// <summary>
    /// Config names whose file was present but could not be read at all — corrupt JSON, or a file the
    /// process could not open. Whatever it held is gone: the config runs on defaults (plus any value that
    /// was recovered before the read failed) and a corrupt file is kept beside its folder as
    /// <c>.unreadable-&lt;stamp&gt;</c>. These are reported to the user and re-verified by the setup wizard,
    /// which is what tells "your settings were lost" apart from the brand-new config
    /// <see cref="GetDefaultedConfigs"/> also lists them as. An entry is removed once the config is saved.
    /// </summary>
    public IReadOnlyList<string> GetUnreadableConfigs()
    { lock (_gate) return _unreadableConfigs.ToList(); }

    /// <summary>
    /// Persists <paramref name="config"/> to its versioned JSON file.
    /// Throws <see cref="IOException"/> on write failure.
    /// </summary>
    public void Save(object config, string configName)
    {
        lock (_gate)
        {
            if (_suppressWrites) return;
            var version = config.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            var path    = GetPath(configName, version);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, SerializeProtected(config));
            _defaultedConfigs.Remove(configName);
            _migratedConfigs.Remove(configName);
            _unreadableConfigs.Remove(configName);
        }
    }

    /// <summary>
    /// Persists <paramref name="config"/> under an arbitrary <paramref name="directory"/> (e.g. a
    /// per-work-context folder) using the same versioned-file layout as <see cref="Save"/>. Used
    /// for per-context configs that are not part of the global registry.
    /// </summary>
    public void SaveTo(string directory, object config, string configName)
    {
        lock (_gate)
        {
            if (_suppressWrites) return;
            var version = config.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            var path    = Path.Combine(directory, configName, $"config_{version}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, SerializeProtected(config));
        }
    }

    /// <summary>
    /// Populates <paramref name="config"/> from its versioned file under <paramref name="directory"/>,
    /// migrating an older version's file forward when the current one is absent (see
    /// <see cref="LoadOrMigrate"/>). No-op only when no file of any version exists (config keeps its
    /// current/default values).
    /// </summary>
    public void LoadFrom(string directory, object config, string configName)
    {
        lock (_gate)
        {
            var version = config.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            LoadOrMigrate(config, directory, configName, version);
        }
    }

    /// <summary>Creates a deep clone of a config POCO via JSON round-trip.</summary>
    public static object Clone(object config)
    {
        var json = JsonSerializer.Serialize(config, config.GetType(), _opts);
        return JsonSerializer.Deserialize(json, config.GetType(), _opts)!;
    }

    private enum LoadOutcome { None, Loaded, Migrated, Unreadable }

    /// <summary>
    /// Loads <paramref name="config"/> from its current-version file under
    /// <c>{parentDir}\{configName}\config_{version}.json</c>. When that file is absent but an older
    /// version's file exists, its data is migrated forward: the newest older file is loaded
    /// (lenient field-by-field carry-over of every name-matching property), an optional
    /// <see cref="Nexaflow.Features.Common.IConfigMigration"/> /
    /// <see cref="Nexaflow.Providers.Common.IConfigMigration"/> hook fixes up renames/restructures,
    /// the result is written under the current version, and the stale files are removed
    /// (write-then-delete so a failed write never loses the prior data).
    /// <para>
    /// A file that cannot be read at all reports <c>Unreadable</c> rather than throwing: whatever was
    /// recovered before the read failed stays, the rest keeps its defaults, and the fault is on record —
    /// one damaged file must not stop the app, which is also the only way the data it referred to (a
    /// workspace's conversations, notes and provider config) can be recovered at all.
    /// </para>
    /// </summary>
    private LoadOutcome LoadOrMigrate(object config, string parentDir, string configName, Version version)
    {
        var dir   = Path.Combine(parentDir, configName);
        var exact = Path.Combine(dir, $"config_{version}.json");

        if (File.Exists(exact))
            return TryLoad(config, exact) ? LoadOutcome.Loaded : LoadOutcome.Unreadable;

        if (!Directory.Exists(dir)) return LoadOutcome.None;

        // The newest older versioned file becomes the migration source.
        var prior = Directory.GetFiles(dir, "config_*.json")
            .Select(p => (path: p, ver: ParseConfigVersion(p)))
            .Where(x => x.ver is not null)
            .OrderByDescending(x => x.ver!)
            .FirstOrDefault();
        if (prior.path is null) return LoadOutcome.None;

        string rawText;
        try { rawText = File.ReadAllText(prior.path); }
        catch (Exception ex) when (IsUnreadable(ex)) { RecordUnreadable(prior.path, ex); return LoadOutcome.Unreadable; }

        // Lenient carry-over of all name-matching fields.
        bool hasHook = config is Nexaflow.Features.Common.IConfigMigration
                              or Nexaflow.Providers.Common.IConfigMigration;
        if (!TryLoad(config, prior.path, recordValueFaults: !hasHook)) return LoadOutcome.Unreadable;

        // Optional custom upgrade path for shape changes the field copy can't express (renames etc.). The hook has the
        // raw old JSON in hand, so a value the carry-over could not read is the hook's to recover rather than a fault.
        if (JsonNode.Parse(rawText) is JsonObject previous)
        {
            if (config is Nexaflow.Features.Common.IConfigMigration fm)
                fm.MigrateFrom(previous, prior.ver!);
            else if (config is Nexaflow.Providers.Common.IConfigMigration pm)
                pm.MigrateFrom(previous, prior.ver!);
        }

        if (!_suppressWrites)
        {
            File.WriteAllText(exact, SerializeProtected(config));
            foreach (var stale in Directory.GetFiles(dir, "config_*.json"))
                if (!string.Equals(stale, exact, StringComparison.OrdinalIgnoreCase))
                    File.Delete(stale);
        }
        return LoadOutcome.Migrated;
    }

    /// <summary>
    /// Reads <paramref name="path"/> onto <paramref name="config"/>. False when the file could not be read
    /// at all — the fault is on record and, when the content itself is corrupt, the file is set aside so the
    /// next launch starts from a clean one with the damaged bytes still on disk to recover by hand.
    /// <paramref name="recordValueFaults"/> is false for a migration whose config has an
    /// <c>IConfigMigration</c> hook: recovering a value the carry-over could not read is that hook's job,
    /// so a value it is about to fix is not a fault worth logging.
    /// </summary>
    private bool TryLoad(object config, string path, bool recordValueFaults = true)
    {
        try
        {
            var valueFaults = Load(config, path);
            if (recordValueFaults) Record(valueFaults);
            return true;
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            RecordUnreadable(path, ex);
            return false;
        }
    }

    /// <summary>A read that failed for the file rather than for one stored value.</summary>
    private static bool IsUnreadable(Exception ex)
        => ex is JsonException or IOException or UnauthorizedAccessException;

    /// <summary>
    /// Puts an unreadable file on record and, for corrupt content (as opposed to a file that could not be
    /// opened — which may just be locked, and whose data is probably still good), renames it aside so the
    /// app starts from defaults next launch instead of failing on the same bytes for ever.
    /// </summary>
    private void RecordUnreadable(string path, Exception cause)
    {
        string? kept = null;
        if (cause is JsonException && !_suppressWrites)
        {
            try
            {
                kept = $"{path}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Move(path, kept, overwrite: true);
            }
            catch { kept = null; }   // left in place; it is reported either way
        }

        FaultLog.Record(new InvalidDataException(
            $"{path} could not be read, so its settings fall back to their defaults."
          + (kept is null ? string.Empty : $" The file is kept as {kept}."), cause));
    }

    /// <summary>Parses the version out of a <c>config_{version}.json</c> path, or null if it doesn't match.</summary>
    private static Version? ParseConfigVersion(string path)
    {
        const string prefix = "config_";
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && Version.TryParse(name[prefix.Length..], out var v) ? v : null;
    }

    /// <summary>
    /// Copies every stored value whose name matches a writable property onto <paramref name="config"/>. A value that no
    /// longer reads as its property's type — written by a build where that property had another shape, such as an enum
    /// that became a string — is skipped, so the property keeps its default, and is returned as a fault rather than
    /// thrown: one stale setting must not keep a whole config, and with it the app, from loading. A file that cannot be
    /// read as JSON at all throws, for <see cref="TryLoad"/> to turn into a reported fallback to defaults.
    /// </summary>
    private static List<InvalidDataException> Load(object config, string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var props = config.GetType().GetProperties()
                          .Where(p => p.CanWrite)
                          .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var unreadable = new List<InvalidDataException>();
        foreach (var el in doc.RootElement.EnumerateObject())
        {
            if (!props.TryGetValue(el.Name, out var pi)) continue;

            // [Secret] strings are stored DPAPI-encrypted ("enc:…"); legacy plaintext passes through.
            if (el.Value.ValueKind == JsonValueKind.String && IsSecret(pi))
            {
                pi.SetValue(config, UnprotectValue(el.Value.GetString() ?? string.Empty));
                continue;
            }

            try
            {
                pi.SetValue(config, JsonSerializer.Deserialize(el.Value.GetRawText(), pi.PropertyType, _opts));
            }
            // Anything this one value can throw — it does not read as the property's type, or the setter
            // itself rejected it — leaves that property at its default and the rest of the file readable.
            catch (Exception ex)
            {
                // The stored value stays out of the message: a crash log is what a user sends with a fault report.
                unreadable.Add(new InvalidDataException(
                    $"\"{el.Name}\" in {path} does not read as {config.GetType().Name}.{pi.Name}, which keeps its default.",
                    ex));
            }
        }
        return unreadable;
    }

    /// <summary>Puts each value <see cref="Load"/> could not read on record in <see cref="FaultLog"/>.</summary>
    private void Record(List<InvalidDataException> unreadable)
    {
        foreach (var fault in unreadable) FaultLog.Record(fault);
    }
}
