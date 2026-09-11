using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Keeps the language packs' sources honest where the build cannot. Every help page names a page kind its own project
/// registers — a typo, or a page kind that moved feature, would otherwise leave a help page nothing ever opens. Core
/// alone owns the index. Every picture a help page shows is in the pack. Every UI string the code asks for exists in
/// English, under the area of the project asking. And no translation carries a key or a page English lacks.
/// </summary>
[TestClass]
[NoCoverage("repo guard over the language-pack sources, not a product behaviour")]
public partial class LocalizationContentGuardTests
{
    private const string CoreProject = "Nexaflow.Core";

    [TestMethod]
    public void EveryHelpPage_NamesAPageKindItsOwnProjectRegisters()
    {
        var registered = RegisteredPageKinds();
        var problems = HelpPages()
            .Where(p => p.Topic != "index")
            .Where(p => !registered.TryGetValue(p.Project, out var kinds) || !kinds.Contains(p.Topic))
            .Select(p => $"{p.Path}: '{p.Topic}' is not a page kind {p.Project} registers " +
                         $"(it registers: {string.Join(", ", registered.GetValueOrDefault(p.Project) ?? [])})")
            .ToList();

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    [TestMethod]
    public void OnlyCoreShipsAnIndex_AndNoLanguageHasATopicTwice()
    {
        var pages = HelpPages().ToList();

        var strayIndex = pages.Where(p => p.Topic == "index" && p.Project != CoreProject).Select(p => p.Path).ToList();
        Assert.AreEqual(0, strayIndex.Count, "only Core ships help/index.md:\n" + string.Join("\n", strayIndex));

        var duplicates = pages.GroupBy(p => (p.Language, Topic: p.Topic.ToLowerInvariant()))
                              .Where(g => g.Count() > 1)
                              .Select(g => $"{g.Key.Language}/{g.Key.Topic}: {string.Join(", ", g.Select(p => p.Project))}")
                              .ToList();
        Assert.AreEqual(0, duplicates.Count, "one help page per page kind:\n" + string.Join("\n", duplicates));
    }

    [TestMethod]
    public void EveryPictureAHelpPageShows_IsInItsProjectsPack()
    {
        var problems = new List<string>();
        foreach (var page in HelpPages())
        {
            var root = Path.GetFullPath(Path.Combine(page.Project == CoreProject ? CoreRoot() : ProjectRoot(page), "Localization", page.Language))
                       + Path.DirectorySeparatorChar;
            foreach (Match m in ImageRef().Matches(OutsideCode(File.ReadAllText(page.Path))))
            {
                var src = m.Groups[1].Value;
                if (src.Contains(':')) continue;   // remote: never loaded, shows its alt text by design
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(page.Path)!, Uri.UnescapeDataString(src)));
                if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    problems.Add($"{page.Path}: '{src}' reaches outside its project's Localization/{page.Language}/");
                else if (!File.Exists(resolved))
                    problems.Add($"{page.Path}: '{src}' does not exist");
            }
        }
        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    [TestMethod]
    public void EveryStringKeyInUse_ExistsInEnglish_UnderItsProjectsArea()
    {
        var tables = StringTables().Where(t => t.Language == "en").ToDictionary(t => t.Project, t => t.Keys);
        var problems = new List<string>();

        foreach (var (project, file, key) in KeysInUse())
        {
            if (!AreasOf(project).Contains(key.Split('.')[0]))
                problems.Add($"{file}: '{key}' is not in {project}'s area ({string.Join("/", AreasOf(project))})");
            else if (!tables.TryGetValue(project, out var keys) || !keys.Contains(key))
                problems.Add($"{file}: '{key}' is missing from {project}/Localization/en/strings.json");
        }

        foreach (var table in StringTables())
            foreach (var key in table.Keys.Where(k => !AreasOf(table.Project).Contains(k.Split('.')[0])))
                problems.Add($"{table.Path}: '{key}' is outside {table.Project}'s area");

        var twice = StringTables().Where(t => t.Language == "en")
            .SelectMany(t => t.Keys.Select(k => (Key: k, t.Project)))
            .GroupBy(x => x.Key).Where(g => g.Count() > 1)
            .Select(g => $"'{g.Key}' is defined by {string.Join(" and ", g.Select(x => x.Project))}");
        problems.AddRange(twice);

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    [TestMethod]
    public void NoTranslationHasAKeyOrAPageEnglishLacks()
    {
        var english = StringTables().Where(t => t.Language == "en").ToDictionary(t => t.Project, t => t.Keys);
        var englishPages = HelpPages().Where(p => p.Language == "en").Select(p => (p.Project, p.Topic)).ToHashSet();

        var problems = StringTables()
            .Where(t => t.Language != "en")
            .SelectMany(t => t.Keys.Where(k => !english.GetValueOrDefault(t.Project, []).Contains(k))
                                   .Select(k => $"{t.Path}: '{k}' has no English original"))
            .Concat(HelpPages().Where(p => p.Language != "en" && !englishPages.Contains((p.Project, p.Topic)))
                               .Select(p => $"{p.Path}: no English help page to translate"))
            .ToList();

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    // ── What the source holds ─────────────────────────────────────────────

    private sealed record HelpPage(string Project, string Language, string Topic, string Path, string ProjectDir);

    private sealed record StringTable(string Project, string Language, string Path, HashSet<string> Keys);

    // Every project directory: src/<name>/ and src/<group>/<name>/, test projects excepted — the same two depths
    // LanguagePack.targets gathers from.
    private static IEnumerable<string> ProjectDirs()
    {
        var src = Path.Combine(RepoRoot(), "src");
        return Directory.GetDirectories(src)
            .Where(d => Path.GetFileName(d) != "Nexaflow.Tests")
            .SelectMany(d => Directory.GetDirectories(d).Prepend(d));
    }

    private static IEnumerable<HelpPage> HelpPages()
    {
        foreach (var project in ProjectDirs())
        {
            var localization = Path.Combine(project, "Localization");
            if (!Directory.Exists(localization)) continue;
            foreach (var language in Directory.GetDirectories(localization))
            {
                var help = Path.Combine(language, "help");
                if (!Directory.Exists(help)) continue;
                foreach (var file in Directory.GetFiles(help, "*.md"))
                    yield return new HelpPage(Path.GetFileName(project), Path.GetFileName(language),
                                              Path.GetFileNameWithoutExtension(file), file, project);
            }
        }
    }

    private static IEnumerable<StringTable> StringTables()
    {
        foreach (var project in ProjectDirs())
        {
            var localization = Path.Combine(project, "Localization");
            if (!Directory.Exists(localization)) continue;
            foreach (var language in Directory.GetDirectories(localization))
            {
                var path = Path.Combine(language, "strings.json");
                if (!File.Exists(path)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(path),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                yield return new StringTable(Path.GetFileName(project), Path.GetFileName(language), path,
                                             doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal));
            }
        }
    }

    // Every literal key the code asks for: {loc:Str Key} in XAML, Str.Get("Key") / Str.Format("Key", …) in C#.
    // Comments are skipped, so a doc comment showing the syntax is not a use.
    private static IEnumerable<(string Project, string File, string Key)> KeysInUse()
    {
        foreach (var project in ProjectDirs())
        {
            foreach (var file in SourceFiles(project, "*.xaml"))
                foreach (Match m in XamlKey().Matches(XmlComment().Replace(File.ReadAllText(file), "")))
                    yield return (Path.GetFileName(project), file, m.Groups[1].Value);

            foreach (var file in SourceFiles(project, "*.cs"))
                foreach (Match m in CodeKey().Matches(LineComment().Replace(File.ReadAllText(file), "")))
                    yield return (Path.GetFileName(project), file, m.Groups[1].Value);
        }
    }

    // A project's own sources: not its build output, and not a nested project's (src/<group>/ holds several).
    private static IEnumerable<string> SourceFiles(string project, string pattern)
    {
        var nested = Directory.GetDirectories(project).Where(d => Directory.GetFiles(d, "*.*proj").Length > 0).ToHashSet();
        if (Directory.GetFiles(project, "*.*proj").Length == 0) yield break;   // a group folder, not a project
        foreach (var file in Directory.EnumerateFiles(project, pattern, SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(project, file);
            var first = rel.Split(Path.DirectorySeparatorChar)[0];
            if (first is "bin" or "obj" || nested.Contains(Path.Combine(project, first))) continue;
            yield return file;
        }
    }

    // The areas a project's keys live under: its short name (Nexaflow.Features.Markdown → Markdown); Core owns the
    // shell's and the help pane's.
    private static HashSet<string> AreasOf(string project)
        => project == CoreProject
            ? new(StringComparer.Ordinal) { "Shell", "Help" }
            : new(StringComparer.Ordinal) { project.Split('.')[^1] };

    // Page kinds each project registers, by reflection over the assemblies beside this test — a const-backed
    // StaticPageKind (FileSystem's) is invisible to a text search.
    private static Dictionary<string, HashSet<string>> RegisteredPageKinds()
    {
        var kinds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var files = Directory.GetFiles(AppContext.BaseDirectory, "Nexaflow.Features.*.dll")
                             .Append(Path.Combine(AppContext.BaseDirectory, "Nexaflow.dll"));
        foreach (var file in files)
        {
            var assembly = Assembly.LoadFrom(file);
            var project  = assembly.GetName().Name == "Nexaflow" ? CoreProject : assembly.GetName().Name!;
            Type?[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }

            foreach (var type in types)
            {
                if (type is null || type.IsAbstract || !typeof(IPageRegistration).IsAssignableFrom(type)) continue;
                if (type.GetProperty("StaticPageKind", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is string kind)
                    (kinds.TryGetValue(project, out var set) ? set : kinds[project] = new(StringComparer.Ordinal)).Add(kind);
            }
        }
        return kinds;
    }

    private static string ProjectRoot(HelpPage page) => page.ProjectDir;

    private static string CoreRoot() => Path.Combine(RepoRoot(), "src", CoreProject);

    // Fenced code and inline code are samples, not the page's own pictures — the Markdown showcase is full of them.
    private static string OutsideCode(string markdown)
    {
        var kept = new List<string>();
        string? fence = null;
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.TrimStart();
            var run = FenceRun().Match(trimmed);
            if (fence is null && run.Success) { fence = run.Value; continue; }
            if (fence is not null)
            {
                if (run.Success && run.Value[0] == fence[0] && run.Value.Length >= fence.Length && trimmed.Trim() == run.Value)
                    fence = null;
                continue;
            }
            kept.Add(InlineCode().Replace(line, ""));
        }
        return string.Join('\n', kept);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Nexaflow.slnx")))
                return dir.FullName;

        throw new InvalidOperationException(
            $"Could not locate the repo root (no Nexaflow.slnx above '{AppContext.BaseDirectory}').");
    }

    [GeneratedRegex(@"!\[[^\]]*\]\(\s*<?([^)\s>]+)")]
    private static partial Regex ImageRef();

    [GeneratedRegex(@"\{loc:Str\s+([\w.]+)\s*\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"\bStr\.(?:Get|Format)\(\s*""([\w.]+)""")]
    private static partial Regex CodeKey();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlComment();

    [GeneratedRegex(@"//.*$", RegexOptions.Multiline)]
    private static partial Regex LineComment();

    [GeneratedRegex(@"^(`{3,}|~{3,})")]
    private static partial Regex FenceRun();

    [GeneratedRegex(@"`[^`]*`")]
    private static partial Regex InlineCode();
}
