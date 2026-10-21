using System.Text.Json;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// English as the source tree holds it: every project's <c>Localization/en/strings.json</c> merged into one table, so a
/// suite with no <c>LanguageManager</c> can stand it behind <c>Str</c> and a test comparing with <c>Str.Format(key, n)</c>
/// sees the sentence and its numbers rather than the bare key. WPF-free, so every suite can read it whatever it targets.
/// </summary>
public static class EnglishStringTable
{
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling     = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Every English key and its text, read afresh from the source tree.</summary>
    public static IReadOnlyDictionary<string, string> Load()
    {
        var src = System.IO.Path.Combine(TestSampleData.RepoRoot, "src");
        var tables = Directory.EnumerateDirectories(src)
            .Concat(Directory.EnumerateDirectories(src).SelectMany(Directory.EnumerateDirectories))
            .Select(project => System.IO.Path.Combine(project, "Localization", "en", "strings.json"))
            .Where(File.Exists);

        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in tables)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path), Options);
            foreach (var entry in json.RootElement.EnumerateObject())
                table[entry.Name] = entry.Value.GetString() ?? "";
        }
        return table;
    }
}
