using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nexaflow.Tests.Features.Architecture;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Features.Localization;

/// <summary>
/// English read straight from every project's <c>Localization/en/strings.json</c>, so a test that compares with
/// <c>Str.Format(key, n)</c> sees the sentence and its numbers rather than the bare key. The suites have no Core,
/// so no <c>LanguageManager</c>; each sets this from its <c>[AssemblyInitialize]</c>.
/// </summary>
public sealed class EnglishStrings : ILocalizedStringSource
{
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling     = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, string> _table = new(StringComparer.Ordinal);

    private EnglishStrings()
    {
        var src = Path.Combine(RepoRoot.Locate(), "src");
        var tables = Directory.EnumerateDirectories(src)
            .Concat(Directory.EnumerateDirectories(src).SelectMany(Directory.EnumerateDirectories))
            .Select(project => Path.Combine(project, "Localization", "en", "strings.json"))
            .Where(File.Exists);

        foreach (var table in tables)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(table), Options);
            foreach (var entry in json.RootElement.EnumerateObject())
                _table[entry.Name] = entry.Value.GetString() ?? "";
        }
    }

    public static void Use() => Str.Source = new EnglishStrings();

    public string? Find(string key) => _table.GetValueOrDefault(key);
}
