using Microsoft.Win32;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Owns the experience→criteria mappings, the HKCR background scan, and the fast
/// reverse index used to resolve which experiences apply to a given file.
/// Accessed by both <see cref="FileActionManager"/> (for filtering) and the
/// File Type Actions Options control (for editing).
/// </summary>
public sealed class FileMapManager
{
    // ── Singleton ────────────────────────────────────────────────────────────

    public static FileMapManager Instance { get; } = new();
    private FileMapManager() { }

    // ── Persistence paths ────────────────────────────────────────────────────

    // Set in Initialize from the app config base dir (ConfigManager.BaseDir) so the file map
    // lives under the active config root — identical to the old %AppData%\Smile\nexaflow\filemap
    // for a normal run, but correctly isolated for a NEXAFLOW_CONFIG_DIR run (e.g. UI tests).
    private string _filemapDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Smile", "nexaflow", "filemap");

    private string IndexPath           => Path.Combine(_filemapDir, "_index.bin");
    private string DefaultsManifestPath => Path.Combine(_filemapDir, "_defaults.json");

    // ── In-memory state ──────────────────────────────────────────────────────

    private readonly Dictionary<string, ExperienceMapping> _mappings = new(StringComparer.OrdinalIgnoreCase);
    private volatile ReverseIndex _index = new();
    private bool _useRegistryMapping;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented            = true,
        PropertyNameCaseInsensitive = true,
        Converters               = { new JsonStringEnumConverter() },
    };

    // ── Public API ───────────────────────────────────────────────────────────

    public void Initialize(bool useRegistryMapping, string baseDir)
    {
        _useRegistryMapping = useRegistryMapping;
        _filemapDir = Path.Combine(baseDir, "filemap");

        Directory.CreateDirectory(_filemapDir);

        // Seed / merge bundled default mappings, preserving user customizations.
        bool defaultsChanged = SyncBundledDefaults();

        // Load all per-mapping JSON files
        LoadMappings();

        // Fast startup: reuse the pre-built index unless the defaults just changed.
        if (defaultsChanged || !TryLoadIndex())
            RebuildIndex();

        // Background: refresh registry-derived entries and update index
        if (useRegistryMapping)
            Task.Run(ScanHkcrAsync);
    }

    /// <summary>
    /// Ensures every registered action's ExperienceId has at least an empty
    /// mapping entry so it appears in the Options tree.
    /// </summary>
    public void RegisterKnownExperiences(IEnumerable<string> experienceIds)
    {
        bool dirty = false;
        foreach (var id in experienceIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (!_mappings.ContainsKey(id))
            {
                _mappings[id] = new ExperienceMapping { ExperienceId = id, Source = MappingSource.User };
                dirty = true;
            }
        }
        if (dirty) RebuildIndex();
    }

    /// <summary>
    /// Returns all User-source experience IDs for the Options tree view.
    /// Registry-source entries (auto-generated from HKCR scan) are excluded —
    /// they are index implementation details, not user-editable categories.
    /// </summary>
    public IReadOnlyList<string> GetAllExperienceIds()
    {
        lock (_mappings)
            return [.. _mappings.Values
                .Where(m => m.Source == MappingSource.User)
                .Select(m => m.ExperienceId)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Returns the <see cref="ExperienceMapping"/> for a given ID, or null.
    /// </summary>
    public ExperienceMapping? GetMapping(string experienceId)
    {
        lock (_mappings)
            return _mappings.TryGetValue(experienceId, out var m) ? m : null;
    }

    /// <summary>
    /// Saves (or creates) a mapping and triggers an index rebuild.
    /// </summary>
    public void SaveMapping(ExperienceMapping mapping)
    {
        lock (_mappings)
            _mappings[mapping.ExperienceId] = mapping;

        WriteMapping(mapping);
        RebuildIndex();
    }

    /// <summary>
    /// Deletes a mapping file and removes it from the in-memory state.
    /// </summary>
    public void DeleteMapping(string experienceId)
    {
        lock (_mappings)
            _mappings.Remove(experienceId);

        string path = MappingFilePath(experienceId);
        if (File.Exists(path)) File.Delete(path);
        RebuildIndex();
    }

    /// <summary>
    /// Converts all Registry-source mappings to User-source (called when the
    /// user turns off registry-based mapping).
    /// </summary>
    public void ConvertRegistryMappingsToUser()
    {
        List<ExperienceMapping> toUpdate;
        lock (_mappings)
            toUpdate = [.. _mappings.Values.Where(m => m.Source == MappingSource.Registry)];

        foreach (var m in toUpdate)
        {
            m.Source = MappingSource.User;
            WriteMapping(m);
        }
        RebuildIndex();
    }

    /// <summary>Candidate extensions for a file name, longest (most-specific) first:
    /// <c>foo.tar.gz</c> → <c>.tar.gz</c>, <c>.gz</c>. Lets compound extensions match.</summary>
    private static IEnumerable<string> ExtensionCandidates(string fileName)
    {
        var name = Path.GetFileName(fileName).ToLowerInvariant();
        for (int i = name.IndexOf('.'); i >= 0; i = name.IndexOf('.', i + 1))
            yield return name[i..];
    }

    /// <summary>
    /// Returns all experience IDs that apply to the given file, including
    /// all ancestor IDs (hierarchical propagation).
    /// </summary>
    public IReadOnlyList<string> GetExperiencesForFile(FileInfo file)
    {
        var idx = _index;
        var ext = file.Extension.ToLowerInvariant();

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Extension lookup — try compound extensions (.tar.gz) as well as the bare one (.gz).
        foreach (var cand in ExtensionCandidates(file.Name))
            if (idx.ByExtension.TryGetValue(cand, out var byExt))
                foreach (var id in byExt) matched.Add(id);
        if (idx.ByExtension.TryGetValue("*", out var universal))
            foreach (var id in universal) matched.Add(id);

        // 2. PerceivedType
        if (!string.IsNullOrEmpty(ext))
        {
            var info = ShellTypeResolver.Resolve(ext);
            if (info is not null)
            {
                if (!string.IsNullOrEmpty(info.PerceivedType) &&
                    idx.ByPerceivedType.TryGetValue(info.PerceivedType.ToLowerInvariant(), out var byPt))
                    foreach (var id in byPt) matched.Add(id);

                // 3. ContentType
                if (!string.IsNullOrEmpty(info.ContentType))
                    foreach (var (pattern, id) in idx.ContentTypeRules)
                        if (ContentTypeMatches(pattern, info.ContentType))
                            matched.Add(id);
            }
        }

        // 4. MagicNumber (lazy — only when worth checking)
        if (idx.MagicExtensions.Contains(ext) && file.Exists && file.Length > 0)
        {
            try
            {
                var typeName = DetectMagicType(file);
                if (typeName is not null &&
                    idx.ByMagicNumber.TryGetValue(typeName, out var byMagic))
                    foreach (var id in byMagic) matched.Add(id);
            }
            catch { }
        }

        // 5. PathPattern
        foreach (var (pattern, id) in idx.PathPatternRules)
            if (GlobMatch(file.FullName, pattern))
                matched.Add(id);

        // 6. Hierarchical expansion: /binary/installer → also /binary, /
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in matched)
        {
            expanded.Add(id);
            var parts = id.TrimStart('/').Split('/');
            for (int i = 1; i < parts.Length; i++)
                expanded.Add("/" + string.Join("/", parts[..i]));
            expanded.Add("/");
        }

        return [.. expanded];
    }

    // ── Persistence helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Tracks the pristine default content per experience so an app update can refresh the bundled
    /// defaults a user hasn't customized without clobbering the ones they have. <see cref="BundleHash"/>
    /// is the hash of the whole bundle so an unchanged bundle is a fast no-op; <see cref="Mappings"/>
    /// records, per experience id, the hash of the default we last wrote — an on-disk file still equal
    /// to it is "unmodified" and safe to update.
    /// </summary>
    private sealed class DefaultsManifest
    {
        public string BundleHash { get; set; } = string.Empty;
        public Dictionary<string, string> Mappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Seeds the bundled default mappings on first run and, on later runs, merges changed defaults:
    /// brand-new defaults are added, defaults the user hasn't touched are refreshed, and
    /// user-customized mappings are preserved. Returns true when any mapping file was written/deleted
    /// (so the caller rebuilds the index).
    ///
    /// First-merge note: the very first release shipping this should NOT change default-filemap.json,
    /// so the baseline is captured from today's pristine files — without a recorded baseline an
    /// old-pristine default is indistinguishable from a user edit and is conservatively preserved.
    /// </summary>
    private bool SyncBundledDefaults()
    {
        string bundled = Path.Combine(
            AppContext.BaseDirectory, "FileActions", "default-filemap.json");
        if (!File.Exists(bundled)) return false;

        string bundleText = File.ReadAllText(bundled);
        var defaults = JsonSerializer.Deserialize<List<ExperienceMapping>>(bundleText, _jsonOpts);
        return defaults is not null && ApplyBundledDefaults(defaults, Hash(bundleText));
    }

    /// <summary>
    /// Core of <see cref="SyncBundledDefaults"/>, split out for testing: merges <paramref name="defaults"/>
    /// (already deserialized) keyed by <paramref name="bundleHash"/> against the on-disk mappings and the
    /// hash manifest. Returns true when any mapping file was written/deleted.
    /// </summary>
    internal bool ApplyBundledDefaults(List<ExperienceMapping> defaults, string bundleHash)
    {
        var manifest = LoadDefaultsManifest();
        if (manifest is not null && manifest.BundleHash == bundleHash)
            return false;   // bundle unchanged since last sync — nothing to do

        manifest ??= new DefaultsManifest();
        bool changed = false;

        var bundledIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in defaults)
        {
            if (string.IsNullOrEmpty(d.ExperienceId)) continue;
            bundledIds.Add(d.ExperienceId);

            string defaultHash = HashMapping(d);
            string path        = MappingFilePath(d.ExperienceId);

            if (!File.Exists(path))
            {
                // A default the user doesn't have yet — add it.
                WriteMapping(d);
                manifest.Mappings[d.ExperienceId] = defaultHash;
                changed = true;
                continue;
            }

            string onDiskHash = HashOnDisk(path);
            string baseline   = manifest.Mappings.TryGetValue(d.ExperienceId, out var b) ? b : defaultHash;
            if (onDiskHash == baseline)
            {
                // Unmodified (still equals the default we last wrote / the current default) — refresh.
                if (onDiskHash != defaultHash) { WriteMapping(d); changed = true; }
                manifest.Mappings[d.ExperienceId] = defaultHash;
            }
            else if (!manifest.Mappings.ContainsKey(d.ExperienceId))
            {
                // First sync after upgrade with no recorded baseline: assume the existing file is the
                // user's and record it, so future bundle changes never clobber it.
                manifest.Mappings[d.ExperienceId] = onDiskHash;
            }
            // else: recorded baseline exists and the file diverged → user-customized → preserve.
        }

        // A default removed from the bundle and still pristine on disk → clean it up.
        foreach (var (id, recordedHash) in manifest.Mappings.ToList())
        {
            if (bundledIds.Contains(id)) continue;
            string path = MappingFilePath(id);
            if (File.Exists(path) && HashOnDisk(path) == recordedHash)
            {
                File.Delete(path);
                changed = true;
            }
            manifest.Mappings.Remove(id);
        }

        manifest.BundleHash = bundleHash;
        SaveDefaultsManifest(manifest);
        return changed;
    }

    private DefaultsManifest? LoadDefaultsManifest()
    {
        try
        {
            return File.Exists(DefaultsManifestPath)
                ? JsonSerializer.Deserialize<DefaultsManifest>(File.ReadAllText(DefaultsManifestPath), _jsonOpts)
                : null;
        }
        catch { return null; }
    }

    private void SaveDefaultsManifest(DefaultsManifest manifest)
    {
        try { File.WriteAllText(DefaultsManifestPath, JsonSerializer.Serialize(manifest, _jsonOpts)); }
        catch { }
    }

    /// <summary>Canonical content hash of a mapping (serialized through <see cref="_jsonOpts"/>).</summary>
    private string HashMapping(ExperienceMapping mapping) =>
        Hash(JsonSerializer.Serialize(mapping, _jsonOpts));

    /// <summary>Canonical content hash of an on-disk mapping file (whitespace/order insensitive).</summary>
    private string HashOnDisk(string path)
    {
        try
        {
            string text = File.ReadAllText(path);
            var m = JsonSerializer.Deserialize<ExperienceMapping>(text, _jsonOpts);
            return m is not null ? HashMapping(m) : Hash(text);
        }
        catch { return string.Empty; }
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private void LoadMappings()
    {
        lock (_mappings)
        {
            _mappings.Clear();
            foreach (var file in Directory.GetFiles(_filemapDir, "*.json"))
            {
                if (Path.GetFileName(file).StartsWith('_')) continue;
                try
                {
                    var m = JsonSerializer.Deserialize<ExperienceMapping>(
                        File.ReadAllText(file), _jsonOpts);
                    if (m is not null && !string.IsNullOrEmpty(m.ExperienceId))
                        _mappings[m.ExperienceId] = m;
                }
                catch { }
            }
        }
    }

    private void WriteMapping(ExperienceMapping mapping)
    {
        string path = MappingFilePath(mapping.ExperienceId);
        File.WriteAllText(path, JsonSerializer.Serialize(mapping, _jsonOpts));
    }

    private string MappingFilePath(string experienceId)
    {
        string safe = experienceId.TrimStart('/').Replace('/', '_');
        if (string.IsNullOrEmpty(safe)) safe = "root";
        return Path.Combine(_filemapDir, safe + ".json");
    }

    // ── Index ─────────────────────────────────────────────────────────────────

    private sealed class ReverseIndex
    {
        public Dictionary<string, List<string>> ByExtension      { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> ByPerceivedType  { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Pattern, string Id)> ContentTypeRules { get; init; } = [];
        public List<(string Pattern, string Id)> PathPatternRules { get; init; } = [];
        public HashSet<string>                   MagicExtensions  { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> ByMagicNumber    { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private void RebuildIndex()
    {
        var idx = new ReverseIndex();

        IReadOnlyList<ExperienceMapping> snapshot;
        lock (_mappings) snapshot = [.. _mappings.Values];

        foreach (var mapping in snapshot)
        {
            var id = mapping.ExperienceId;
            foreach (var c in mapping.Criteria)
            {
                switch (c.Type)
                {
                    case CriteriaType.Extension:
                    {
                        string key = c.Value == "*.*"         ? "*"
                                   : c.Value.StartsWith("*.") ? c.Value[1..].ToLowerInvariant()
                                   : c.Value.ToLowerInvariant();
                        idx.ByExtension.TryGetValue(key, out var list);
                        if (list is null) { list = []; idx.ByExtension[key] = list; }
                        list.Add(id);
                        break;
                    }
                    case CriteriaType.PerceivedType:
                        AddToDict(idx.ByPerceivedType, c.Value.ToLowerInvariant(), id);
                        break;
                    case CriteriaType.ContentType:
                        idx.ContentTypeRules.Add((c.Value, id));
                        break;
                    case CriteriaType.MagicNumber:
                        AddToDict(idx.ByMagicNumber, c.Value, id);
                        // Track which extensions need magic-number checking
                        // (populated further during HKCR scan; for now flag all)
                        break;
                    case CriteriaType.PathPattern:
                        idx.PathPatternRules.Add((c.Value, id));
                        break;
                }
            }
        }

        Interlocked.Exchange(ref _index, idx);
        WriteIndex(idx);
    }

    private static void AddToDict(Dictionary<string, List<string>> dict, string key, string value)
    {
        if (!dict.TryGetValue(key, out var list)) { list = []; dict[key] = list; }
        list.Add(value);
    }

    // ── Index serialisation ───────────────────────────────────────────────────

    private sealed class IndexDto
    {
        public Dictionary<string, List<string>>     ByExtension      { get; set; } = [];
        public Dictionary<string, List<string>>     ByPerceivedType  { get; set; } = [];
        public List<string[]>                        ContentTypeRules { get; set; } = [];
        public List<string[]>                        PathPatternRules { get; set; } = [];
        public List<string>                          MagicExtensions  { get; set; } = [];
        public Dictionary<string, List<string>>     ByMagicNumber    { get; set; } = [];
    }

    private void WriteIndex(ReverseIndex idx)
    {
        try
        {
            var dto = new IndexDto
            {
                ByExtension     = new(idx.ByExtension,     StringComparer.OrdinalIgnoreCase),
                ByPerceivedType = new(idx.ByPerceivedType, StringComparer.OrdinalIgnoreCase),
                ContentTypeRules = [.. idx.ContentTypeRules.Select(r => new[] { r.Pattern, r.Id })],
                PathPatternRules = [.. idx.PathPatternRules.Select(r => new[] { r.Pattern, r.Id })],
                MagicExtensions  = [.. idx.MagicExtensions],
                ByMagicNumber    = new(idx.ByMagicNumber, StringComparer.OrdinalIgnoreCase),
            };
            File.WriteAllText(IndexPath, JsonSerializer.Serialize(dto, _jsonOpts));
        }
        catch { }
    }

    private bool TryLoadIndex()
    {
        try
        {
            if (!File.Exists(IndexPath)) return false;
            var dto = JsonSerializer.Deserialize<IndexDto>(File.ReadAllText(IndexPath), _jsonOpts);
            if (dto is null) return false;

            var idx = new ReverseIndex
            {
                ByExtension      = new(dto.ByExtension,     StringComparer.OrdinalIgnoreCase),
                ByPerceivedType  = new(dto.ByPerceivedType, StringComparer.OrdinalIgnoreCase),
                ContentTypeRules = [.. dto.ContentTypeRules.Select(r => (r[0], r[1]))],
                PathPatternRules = [.. dto.PathPatternRules.Select(r => (r[0], r[1]))],
                MagicExtensions  = new(dto.MagicExtensions, StringComparer.OrdinalIgnoreCase),
                ByMagicNumber    = new(dto.ByMagicNumber, StringComparer.OrdinalIgnoreCase),
            };
            Interlocked.Exchange(ref _index, idx);
            return true;
        }
        catch { return false; }
    }

    // ── Background HKCR scan ──────────────────────────────────────────────────

    private async Task ScanHkcrAsync()
    {
        await Task.Yield();   // ensure we're truly off the UI thread

        bool dirty = false;
        try
        {
            using var hkcr = Registry.ClassesRoot;
            foreach (var name in hkcr.GetSubKeyNames())
            {
                if (!name.StartsWith('.')) continue;

                var info = ShellTypeResolver.Resolve(name);
                if (info is null) continue;

                bool hasContent   = !string.IsNullOrEmpty(info.ContentType);
                bool hasPerceived = !string.IsNullOrEmpty(info.PerceivedType);
                if (!hasContent && !hasPerceived) continue;

                // Find the experience IDs that already cover this extension via User entries
                var covered = GetExperiencesForFile(new FileInfo($"dummy{name}"));

                // Determine which registry-derived criteria would add new coverage
                var newCriteria = new List<FileSelectionCriteria>();
                if (hasPerceived && !covered.Any(id => _mappings.TryGetValue(id, out var m) &&
                        m.Source == MappingSource.User &&
                        m.Criteria.Any(c => c.Type == CriteriaType.PerceivedType &&
                            string.Equals(c.Value, info.PerceivedType, StringComparison.OrdinalIgnoreCase))))
                {
                    newCriteria.Add(new FileSelectionCriteria { Type = CriteriaType.PerceivedType, Value = info.PerceivedType });
                }
                if (hasContent && !covered.Any(id => _mappings.TryGetValue(id, out var m) &&
                        m.Source == MappingSource.User &&
                        m.Criteria.Any(c => c.Type == CriteriaType.ContentType &&
                            string.Equals(c.Value, info.ContentType, StringComparison.OrdinalIgnoreCase))))
                {
                    newCriteria.Add(new FileSelectionCriteria { Type = CriteriaType.ContentType, Value = info.ContentType });
                }

                if (newCriteria.Count == 0) continue;

                // Upsert into a Registry-source mapping for this extension
                string expId = $"/registry{name}";    // e.g. /registry/.docx
                lock (_mappings)
                {
                    if (_mappings.TryGetValue(expId, out var existing) && existing.Source == MappingSource.User)
                        continue;   // user has taken over this entry

                    var m = existing ?? new ExperienceMapping { ExperienceId = expId, Source = MappingSource.Registry };
                    foreach (var c in newCriteria)
                        if (!m.Criteria.Any(x => x.Type == c.Type && x.Value == c.Value))
                            m.Criteria.Add(c);

                    _mappings[expId] = m;
                }
                dirty = true;
            }
        }
        catch { }

        if (dirty)
        {
            IReadOnlyList<ExperienceMapping> registryMappings;
            lock (_mappings)
                registryMappings = [.. _mappings.Values.Where(m => m.Source == MappingSource.Registry)];

            foreach (var m in registryMappings)
                WriteMapping(m);

            RebuildIndex();
        }
    }

    // ── Default-action specificity ────────────────────────────────────────────

    /// <summary>
    /// Returns the highest-specificity match level for <paramref name="experienceId"/>
    /// against <paramref name="file"/>:
    /// 5 = PathPattern-specific, 4 = Extension-specific, 3 = MagicNumber,
    /// 2 = PerceivedType, 1 = ContentType, 0 = Universal (*.*), -1 = not matched.
    /// A PathPattern scope (an explicit file/folder/glob target) is the most specific
    /// user intent, so it outranks an extension match and wins the default-open decision.
    /// An experience ID is considered matched at level N when it is equal to, or an
    /// ancestor of, any ID directly indexed at that level for this file.
    /// The "/" root ID is always Universal (0) — never elevated by more specific criteria.
    /// </summary>
    public int GetMatchSpecificity(FileInfo file, string experienceId)
    {
        if (string.IsNullOrEmpty(experienceId)) return -1;

        // "/" is always Universal; don't let it inherit a higher specificity.
        bool isRootId = experienceId == "/";

        var idx = _index;
        var ext = file.Extension.ToLowerInvariant();

        if (!isRootId)
        {
            // PathPattern-specific (level 5) — an explicit path/glob scope beats everything.
            foreach (var (pattern, id) in idx.PathPatternRules)
                if (GlobMatch(file.FullName, pattern) && IsAncestorOrSelf([id], experienceId)) return 5;

            // Extension-specific (level 4) — compound extensions (.tar.gz) count, longest first.
            foreach (var cand in ExtensionCandidates(file.Name))
                if (idx.ByExtension.TryGetValue(cand, out var byExt) &&
                    IsAncestorOrSelf(byExt, experienceId)) return 4;

            // MagicNumber (level 3)
            if (idx.MagicExtensions.Contains(ext) && file.Exists && file.Length > 0)
            {
                try
                {
                    var typeName = DetectMagicType(file);
                    if (typeName is not null &&
                        idx.ByMagicNumber.TryGetValue(typeName, out var byMagic) &&
                        IsAncestorOrSelf(byMagic, experienceId)) return 3;
                }
                catch { }
            }

            // PerceivedType (level 2)
            if (!string.IsNullOrEmpty(ext))
            {
                var info = ShellTypeResolver.Resolve(ext);
                if (info is not null)
                {
                    if (!string.IsNullOrEmpty(info.PerceivedType) &&
                        idx.ByPerceivedType.TryGetValue(info.PerceivedType.ToLowerInvariant(), out var byPt) &&
                        IsAncestorOrSelf(byPt, experienceId)) return 2;

                    // ContentType (level 1)
                    if (!string.IsNullOrEmpty(info.ContentType))
                        foreach (var (pattern, id) in idx.ContentTypeRules)
                            if (ContentTypeMatches(pattern, info.ContentType) &&
                                IsAncestorOrSelf([id], experienceId)) return 1;
                }
            }
        }

        // Universal "*" (level 0)
        if (idx.ByExtension.TryGetValue("*", out var universal) &&
            IsAncestorOrSelf(universal, experienceId)) return 0;

        // "/" is always in the expanded set for any matched file
        if (isRootId) return 0;

        return -1;
    }

    /// <summary>
    /// Returns true when <paramref name="targetId"/> is equal to or an ancestor of
    /// any ID in <paramref name="directlyMatched"/> (i.e. targetId is a prefix of that ID).
    /// </summary>
    private static bool IsAncestorOrSelf(IEnumerable<string> directlyMatched, string targetId)
    {
        string prefix = targetId.TrimEnd('/') + "/";
        foreach (var id in directlyMatched)
        {
            if (id.Equals(targetId, StringComparison.OrdinalIgnoreCase)) return true;
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // ── Matching helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Reads the first 12 bytes of a file and returns a well-known type name
    /// (e.g. "PDF", "ZIP", "PNG") or null if unrecognised.
    /// The names match what users enter as <see cref="CriteriaType.MagicNumber"/> values.
    /// </summary>
    private static string? DetectMagicType(FileInfo file)
    {
        Span<byte> buf = stackalloc byte[12];
        int read;
        using (var fs = file.OpenRead())
            read = fs.Read(buf);
        if (read < 2) return null;

        // PDF: %PDF
        if (read >= 4 && buf[0] == 0x25 && buf[1] == 0x50 && buf[2] == 0x44 && buf[3] == 0x46)
            return "PDF";
        // PNG: \x89PNG
        if (read >= 4 && buf[0] == 0x89 && buf[1] == 0x50 && buf[2] == 0x4E && buf[3] == 0x47)
            return "PNG";
        // JPEG: \xFF\xD8\xFF
        if (read >= 3 && buf[0] == 0xFF && buf[1] == 0xD8 && buf[2] == 0xFF)
            return "JPEG";
        // GIF87a / GIF89a
        if (read >= 6 && buf[0] == 0x47 && buf[1] == 0x49 && buf[2] == 0x46 && buf[3] == 0x38)
            return "GIF";
        // ZIP: PK\x03\x04
        if (read >= 4 && buf[0] == 0x50 && buf[1] == 0x4B && buf[2] == 0x03 && buf[3] == 0x04)
            return "ZIP";
        // 7-Zip: 7z\xBC\xAF
        if (read >= 4 && buf[0] == 0x37 && buf[1] == 0x7A && buf[2] == 0xBC && buf[3] == 0xAF)
            return "7ZIP";
        // RAR: Rar!\x1A\x07
        if (read >= 6 && buf[0] == 0x52 && buf[1] == 0x61 && buf[2] == 0x72 && buf[3] == 0x21 && buf[4] == 0x1A && buf[5] == 0x07)
            return "RAR";
        // BMP: BM
        if (read >= 2 && buf[0] == 0x42 && buf[1] == 0x4D)
            return "BMP";
        // WEBP: RIFF....WEBP (bytes 0-3 = RIFF, bytes 8-11 = WEBP)
        if (read >= 12 &&
            buf[0] == 0x52 && buf[1] == 0x49 && buf[2] == 0x46 && buf[3] == 0x46 &&
            buf[8] == 0x57 && buf[9] == 0x45 && buf[10] == 0x42 && buf[11] == 0x50)
            return "WEBP";

        return null;
    }

    private static bool ContentTypeMatches(string pattern, string value)
    {
        if (pattern is "*" or "") return true;
        if (pattern.EndsWith("/*"))
        {
            var prefix = pattern[..^2];
            return value.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool GlobMatch(string path, string pattern)
    {
        if (pattern is "*" or "*.*") return true;
        return Glob.IsMatch(path, pattern);
    }
}
