using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Singleton that owns the live <see cref="ExternalAppsConfig"/> snapshot and
/// produces <see cref="CustomAction"/> instances matching a selected set of files.
/// Updated by <c>ExternalAppsEditorControl</c> on Save.
/// </summary>
public sealed class ExternalAppRegistry
{
    public static ExternalAppRegistry Instance { get; } = new();
    private ExternalAppRegistry() { }

    private ExternalAppsConfig _config = new();

    public void Initialize(ExternalAppsConfig config) => _config = config;

    /// <summary>Replaces the held config and any cached projections.</summary>
    public void Update(ExternalAppsConfig config) => _config = config;

    public bool UseRegistryMapping => _config.UseRegistryMapping;

    /// <summary>Looks up a held app by its stable <see cref="ExternalAppDefinition.Id"/>, or null.</summary>
    public ExternalAppDefinition? FindById(string id) =>
        string.IsNullOrEmpty(id) ? null : _config.Apps.FirstOrDefault(a => a.Id == id);

    /// <summary>
    /// Assigns a stable <see cref="ExternalAppDefinition.Id"/> to any held app missing one. Returns true
    /// when at least one was assigned so the caller persists the config — Default Action overrides
    /// reference apps by id, so the ids must survive restarts. Idempotent.
    /// </summary>
    public bool BackfillIds()
    {
        bool changed = false;
        foreach (var app in _config.Apps)
            if (string.IsNullOrEmpty(app.Id)) { app.Id = Guid.NewGuid().ToString("N"); changed = true; }
        return changed;
    }

    /// <summary>
    /// Returns a fresh <see cref="CustomAction"/> for each definition whose
    /// extension matches every file in <paramref name="selected"/> and whose
    /// multi-file mode permits the current selection size.
    /// Safe to call from a background thread; icons are loaded via
    /// <see cref="ShellIconLoader"/> which returns frozen <c>ImageSource</c>s.
    /// </summary>
    public IReadOnlyList<CustomAction> Resolve(IReadOnlyList<FileSystemEntry> selected)
    {
        var apps = _config.Apps;
        if (apps == null || apps.Count == 0 || selected.Count == 0) return Array.Empty<CustomAction>();

        // Multi-file selection requires multi-file capable definitions.
        bool multi = selected.Count > 1;

        // All selected entries must be files (CustomAction targets files, not folders/drives).
        if (selected.Any(e => e.IsDirectory || e.IsThisPcItem)) return Array.Empty<CustomAction>();

        var exts = selected
            .Select(e => Path.GetExtension(e.Name).TrimStart('.').ToLowerInvariant())
            .ToList();

        var results = new List<CustomAction>(apps.Count);
        foreach (var def in apps)
        {
            if (multi && def.MultiFile == MultiFileMode.SingleFileOnly) continue;

            bool matches = def.Criteria is { Count: > 0 }
                ? MatchesCriteria(def.Criteria, selected)
                : MatchesAll(def.Extension, exts);
            if (!matches) continue;

            results.Add(new CustomAction(def, ResolveIcon(def)));
        }
        return results;
    }

    /// <summary>
    /// Picks an icon for <paramref name="def"/>:
    ///   1. <see cref="ExternalAppDefinition.IconPath"/> when set
    ///   2. icon extracted from <see cref="ExternalAppDefinition.ApplicationPath"/>
    ///   3. shell32.dll generic open icon as a last resort
    /// </summary>
    internal static System.Windows.Media.ImageSource? ResolveIcon(ExternalAppDefinition def)
    {
        if (!string.IsNullOrWhiteSpace(def.IconPath))
        {
            var fromIcon = ShellIconLoader.Load(def.IconPath);
            if (fromIcon is not null) return fromIcon;
        }

        if (!string.IsNullOrWhiteSpace(def.ApplicationPath))
        {
            var exe = Environment.ExpandEnvironmentVariables(def.ApplicationPath);
            var fromExe = ShellIconLoader.Load(exe);
            if (fromExe is not null) return fromExe;
        }

        // shell32.dll icon index 3 is the classic "open folder/file" glyph and is
        // present on every supported Windows version.
        return ShellIconLoader.Load(@"%SystemRoot%\System32\shell32.dll,3");
    }

    private static bool MatchesAll(string defExt, IReadOnlyList<string> selectedExts)
    {
        if (string.IsNullOrEmpty(defExt) || defExt == "*" || defExt == "*.*") return true;
        // Accept "*.tft", ".tft", or "tft" — normalize to bare extension.
        var want = defExt.StartsWith("*.") ? defExt[2..].ToLowerInvariant()
                 : defExt.TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(want) || want == "*") return true;
        foreach (var e in selectedExts)
            if (!string.Equals(e, want, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// True when every selected file satisfies at least one criterion. Only
    /// <see cref="CriteriaType.Extension"/> and <see cref="CriteriaType.PathPattern"/>
    /// are honored — the wizard never emits the others for an external app.
    /// </summary>
    private static bool MatchesCriteria(IReadOnlyList<FileSelectionCriteria> criteria, IReadOnlyList<FileSystemEntry> selected)
    {
        foreach (var entry in selected)
        {
            bool any = false;
            foreach (var c in criteria)
                if (CriterionMatches(c, entry)) { any = true; break; }
            if (!any) return false;
        }
        return true;
    }

    private static bool CriterionMatches(FileSelectionCriteria c, FileSystemEntry entry) => c.Type switch
    {
        CriteriaType.Extension   => ExtensionMatches(c.Value,
                                        Path.GetExtension(entry.Name).TrimStart('.').ToLowerInvariant()),
        CriteriaType.PathPattern => c.Value is "*" or "*.*" || Glob.IsMatch(entry.FullPath, c.Value),
        _                        => false,
    };

    private static bool ExtensionMatches(string value, string ext)
    {
        if (string.IsNullOrEmpty(value) || value == "*" || value == "*.*") return true;
        var want = value.StartsWith("*.") ? value[2..].ToLowerInvariant()
                 : value.TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(want) || want == "*") return true;
        return string.Equals(ext, want, StringComparison.OrdinalIgnoreCase);
    }
}
