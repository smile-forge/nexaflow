using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Features.Tabular.Streaming;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Tabular.Templates;

/// <summary>
/// Pure matching/capture logic for tabular templates (no UI, no I/O beyond path parsing) — the
/// unit-test target. Decides whether a template is compatible with a freshly-detected file shape,
/// whether its scope claims a given path, and builds a template from a live <see cref="DataShape"/>.
/// </summary>
public static class TemplateMatcher
{
    /// <summary>
    /// A template is compatible with <paramref name="raw"/> when the structural signature matches:
    /// same separator, same field count, same header presence, and — when both have a header — the
    /// same header names (trimmed, case-insensitive). Quote is intentionally ignored.
    /// </summary>
    public static bool IsCompatible(TabularTemplate t, CsvShape raw, string[]? rawHeaders)
    {
        if (t.Separator != raw.Separator?.ToString()) return false;
        if (t.FieldCount != raw.FieldCount)            return false;
        if (t.HasHeader  != raw.HasHeader)             return false;
        if (raw.HasHeader)
            return Normalize(t.OriginalHeaders).SequenceEqual(Normalize(rawHeaders));
        return true;
    }

    /// <summary>True when an auto-apply template's scope claims <paramref name="filePath"/>:
    /// Folder = exact containing folder; Glob = file-name pattern. Manual never matches.</summary>
    public static bool ScopeMatches(TabularTemplate t, string filePath) => t.Scope switch
    {
        TemplateScope.Folder => Path.GetDirectoryName(filePath) is { } dir && PathsEqual(dir, t.FolderPath),
        TemplateScope.Glob   => !string.IsNullOrEmpty(t.Glob) && Glob.IsMatch(Path.GetFileName(filePath), t.Glob),
        _                    => false,
    };

    /// <summary>Every auto-apply (Folder/Glob) template whose scope claims the file AND whose original
    /// shape is compatible with the detected shape. More than one → the caller prompts the user.</summary>
    public static IReadOnlyList<TabularTemplate> FindAutoApply(
        IEnumerable<TabularTemplate> templates, string filePath, CsvShape raw, string[]? headers)
        => templates
            .Where(t => t.Scope is TemplateScope.Folder or TemplateScope.Glob
                        && ScopeMatches(t, filePath)
                        && IsCompatible(t, raw, headers))
            .ToList();

    /// <summary>Builds a template capturing both the original shape and the current transform chain.</summary>
    public static TabularTemplate Capture(
        string name, TemplateScope scope, string folderPath, string glob, DataShape data)
    {
        var raw = data.RawShape;
        return new TabularTemplate
        {
            Id              = Guid.NewGuid().ToString("N"),
            Name            = name.Trim(),
            Scope           = scope,
            FolderPath      = scope == TemplateScope.Folder ? folderPath : string.Empty,
            Glob            = scope == TemplateScope.Glob    ? glob       : string.Empty,
            Separator       = raw.Separator?.ToString(),
            Quote           = raw.Quote?.ToString(),
            HasHeader       = raw.HasHeader,
            FieldCount      = raw.FieldCount,
            OriginalHeaders = data.RawHeaderCells?.Select(h => h.Trim()).ToArray(),
            TransformsJson  = TransformSerializer.Serialize(data.Transforms),
            FinalHeaders    = data.EffectiveColumns.Select(c => c.Header).ToArray(),
        };
    }

    private static string[] Normalize(string[]? headers) =>
        headers is null ? Array.Empty<string>()
                        : headers.Select(h => (h ?? string.Empty).Trim().ToLowerInvariant()).ToArray();

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(a.Trim()),
            Path.TrimEndingDirectorySeparator(b.Trim()),
            StringComparison.OrdinalIgnoreCase);
}
