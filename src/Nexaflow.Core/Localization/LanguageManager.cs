using System.Collections.Frozen;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Nexaflow.Core.Services;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Localization;

/// <summary>
/// The app's language: which pack is active, and the content it serves — the UI string table behind
/// <see cref="Str"/>, and the resources (help showcases and their pictures) the help pane reads.
/// <para>
/// A pack is a resource-only assembly, <c>Languages\Nexaflow.Language.&lt;code&gt;.dll</c>, gathered at build from
/// every project's own <c>Localization/&lt;code&gt;/</c> folder (<c>src/Nexaflow.Languages</c>). Discovery reads file
/// names only. A pack is loaded the first time something asks it for content, and only two ever are: the active
/// language, and English beneath it as the fallback, key by key and file by file. Most users only ever run in one
/// language, so after that first read none of this costs anything — a switch works like a theme switch (the window
/// restarts), never by live re-binding.
/// </para>
/// Central, one per process, like <see cref="ThemeManager"/>. See docs/localization.md.
/// </summary>
internal sealed class LanguageManager : ILocalizedStringSource
{
    public const string PackFilePrefix = "Nexaflow.Language.";
    public const string FallbackCode   = "en";

    private static LanguageManager? _instance;

    /// <summary>The process's language manager, set by <see cref="Initialize"/> at startup.</summary>
    public static LanguageManager Instance =>
        _instance ?? throw new InvalidOperationException("LanguageManager.Initialize has not run.");

    /// <summary><see cref="Instance"/>, or null before startup has created it (design time, tests).</summary>
    public static LanguageManager? TryInstance => _instance;

    /// <summary>Creates the process's manager over <paramref name="languagesDir"/>, selects
    /// <paramref name="configured"/> and points <see cref="Str"/> at it. Loads nothing.</summary>
    public static void Initialize(string languagesDir, string? configured)
    {
        var manager = new LanguageManager(languagesDir, AssemblyLanguagePack.Load);
        manager.Select(configured);
        _instance  = manager;
        Str.Source = manager;
    }

    private static readonly JsonDocumentOptions JsonOptions =
        new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private const string StringTableName = "/strings.json";

    private readonly string _dir;
    private readonly Func<string, string, ILanguagePack> _load;
    private readonly Dictionary<string, ILanguagePack?> _packs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private IReadOnlyList<LanguageInfo>? _available;
    private Lazy<FrozenDictionary<string, string>> _strings;

    /// <param name="languagesDir">The folder the packs are dropped into.</param>
    /// <param name="load">Opens the pack for (code, path); throws for a file that is not one.</param>
    internal LanguageManager(string languagesDir, Func<string, string, ILanguagePack> load)
    {
        _dir     = languagesDir;
        _load    = load;
        _strings = NewStringTable();
    }

    /// <summary>The active language's code — one with a pack, or <see cref="FallbackCode"/>.</summary>
    public string Current { get; private set; } = FallbackCode;

    /// <summary>Raised after <see cref="Current"/> changes, for caches built from pack content.</summary>
    public event Action? Changed;

    /// <summary>The installed languages, for the Options picker — from file names alone, no pack loaded. English is
    /// always offered: it is the fallback even when its pack is missing.</summary>
    public IReadOnlyList<LanguageInfo> Available => _available ??= Discover();

    /// <summary>A configured value as a culture code: <c>"FR"</c> → <c>"fr"</c>. Null, blank or not a culture — which
    /// includes the <c>"English"</c> an earlier build stored, back when this setting was an enum — is English.</summary>
    public static string Normalize(string? configured)
        => TryCulture(configured) is { } culture ? culture.Name : FallbackCode;

    /// <summary>Whether <paramref name="configured"/> would select what is already active. A language whose pack is
    /// missing resolves to English, so saving it again is no change.</summary>
    public bool IsCurrent(string? configured)
        => string.Equals(Resolve(configured), Current, StringComparison.OrdinalIgnoreCase);

    /// <summary>Makes <paramref name="configured"/> the active language, or English when it has no pack. Loads
    /// nothing: the next lookup reads whichever pack is now active.</summary>
    public void Select(string? configured)
    {
        var code = Resolve(configured);
        lock (_gate)
        {
            if (string.Equals(code, Current, StringComparison.OrdinalIgnoreCase)) return;
            Current  = code;
            _strings = NewStringTable();
        }
        Changed?.Invoke();
    }

    /// <summary>A fresh stream over <paramref name="logicalName"/> from the active language, else English; null when
    /// neither pack holds it.</summary>
    public Stream? OpenResource(string logicalName)
    {
        foreach (var pack in Chain())
            if (pack.Open(logicalName) is { } stream) return stream;
        return null;
    }

    /// <summary>Every logical name across the active language and English that passes <paramref name="filter"/>,
    /// once each — <see cref="OpenResource"/> then reads it from whichever pack wins.</summary>
    public IReadOnlyList<string> ResourceNames(Func<string, bool> filter)
        => Chain().SelectMany(p => p.ResourceNames).Where(filter).Distinct(StringComparer.Ordinal).ToList();

    public string? Find(string key) => _strings.Value.TryGetValue(key, out var text) ? text : null;

    // ── Packs ─────────────────────────────────────────────────────────────

    private string Resolve(string? configured)
    {
        var code = Normalize(configured);
        if (string.Equals(code, FallbackCode, StringComparison.OrdinalIgnoreCase)) return FallbackCode;
        return File.Exists(PackPath(code)) ? code : FallbackCode;
    }

    private string PackPath(string code) => Path.Combine(_dir, $"{PackFilePrefix}{code}.dll");

    /// <summary>The active language's pack, then English's — whichever exist, each loaded on first use.</summary>
    private IEnumerable<ILanguagePack> Chain()
    {
        var current = Current;
        if (Pack(current) is { } active) yield return active;
        if (!string.Equals(current, FallbackCode, StringComparison.OrdinalIgnoreCase) && Pack(FallbackCode) is { } english)
            yield return english;
    }

    private ILanguagePack? Pack(string code)
    {
        lock (_gate)
        {
            if (_packs.TryGetValue(code, out var cached)) return cached;

            ILanguagePack? pack = null;
            var path = PackPath(code);
            if (File.Exists(path))
            {
                try { pack = _load(code, path); }
                catch { pack = null; }   // not a loadable pack: its language falls back to English
            }
            _packs[code] = pack;
            return pack;
        }
    }

    private IReadOnlyList<LanguageInfo> Discover()
    {
        var found = new List<LanguageInfo>();
        if (Directory.Exists(_dir))
            foreach (var path in Directory.EnumerateFiles(_dir, $"{PackFilePrefix}*.dll"))
            {
                var code = Path.GetFileNameWithoutExtension(path)[PackFilePrefix.Length..];
                if (TryCulture(code) is { } culture && !found.Any(l => l.Code == culture.Name))
                    found.Add(new LanguageInfo(culture.Name, DisplayName(culture), path));
            }

        if (!found.Any(l => string.Equals(l.Code, FallbackCode, StringComparison.OrdinalIgnoreCase)))
            found.Add(new LanguageInfo(FallbackCode, DisplayName(CultureInfo.GetCultureInfo(FallbackCode)), ""));

        return found.OrderBy(l => l.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static CultureInfo? TryCulture(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        try
        {
            var culture = CultureInfo.GetCultureInfo(code.Trim(), predefinedOnly: true);
            return culture.Name.Length == 0 ? null : culture;   // never the invariant culture
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    // The language's own name for itself, capitalised as a list entry: "English", "Français", "Español".
    private static string DisplayName(CultureInfo culture)
    {
        var name = culture.NativeName;
        return name.Length == 0 ? culture.Name : char.ToUpper(name[0], culture) + name[1..];
    }

    // ── Strings ───────────────────────────────────────────────────────────

    private Lazy<FrozenDictionary<string, string>> NewStringTable()
        => new(BuildStrings, LazyThreadSafetyMode.ExecutionAndPublication);

    // Every project's strings.json, English first and the active language over it, so a key the translation lacks
    // keeps its English text.
    private FrozenDictionary<string, string> BuildStrings()
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pack in Chain().Reverse())
            foreach (var name in pack.ResourceNames.Where(IsStringTable))
                using (var stream = pack.Open(name))
                    if (stream is not null) Merge(table, stream);

        StartupTimings.Mark("Language.Strings");
        return table.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // "<ProjectFolder>/strings.json" — a project's table sits at the root of its Localization/<code>/.
    private static bool IsStringTable(string name)
        => name.EndsWith(StringTableName, StringComparison.Ordinal)
           && name.IndexOf('/') == name.Length - StringTableName.Length;

    private static void Merge(Dictionary<string, string> table, Stream json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, JsonOptions);
            foreach (var entry in doc.RootElement.EnumerateObject())
                if (entry.Value.ValueKind == JsonValueKind.String)
                    table[entry.Name] = entry.Value.GetString()!;
        }
        catch (JsonException)
        {
            // A broken table costs its own keys (they show as keys), never the app.
        }
    }
}
