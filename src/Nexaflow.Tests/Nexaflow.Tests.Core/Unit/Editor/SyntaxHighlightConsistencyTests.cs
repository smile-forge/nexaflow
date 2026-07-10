using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Nexaflow.Syntax;
using Nexaflow.Visuals.Text.Editor.Highlighting;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Editor;

/// <summary>
/// Data-driven consistency check against the bundled syntect/bat reference corpus
/// (Tests.Fixtures/syntax-tests). For each supported language file it parses the ANSI-coloured
/// "highlighted" output into (text, colour) runs and confirms that tokens the reference paints with the
/// SAME text + colour resolve to a SINGLE one of our syntax roles (uncaptured counts as a role).
///
/// It deliberately keys on text+colour rather than colour alone: the reference theme groups categories
/// differently from ours (e.g. Monokai paints `class` and `int` the same), so a pure colour→role map
/// would have false positives. Same-text+colour, however, catches real bugs — e.g. one occurrence of a
/// type coloured and another left plain — without penalising our different categorisation.
/// </summary>
[TestClass]
[CoversNode("syntax-queries")]
public class SyntaxHighlightConsistencyTests
{
    private const string PlainRole = "·plain·";

    public static IEnumerable<object[]> Cases() =>
        SupportedFiles().Select(f => new object[] { f.source, f.highlighted, f.grammar });

    public static string CaseName(MethodInfo _, object[] data) =>
        $"{Path.GetFileName(Path.GetDirectoryName((string)data[0]))}/{Path.GetFileName((string)data[0])}";

    [TestMethod]
    [DynamicData(nameof(Cases), DynamicDataDisplayName = nameof(CaseName))]
    public void Highlighting_ConsistentWithReference(string source, string highlighted, string grammar)
    {
        var result = Analyze(source, highlighted, grammar);

        Assert.IsFalse(result.Misaligned,
            $"{Path.GetFileName(source)}: the de-ANSI'd reference did not align to the source (parser issue).");
        Assert.IsTrue(result.Tested > 0, $"{Path.GetFileName(source)}: no tokens were checked.");
        Assert.AreEqual(0, result.Violations.Count,
            $"{Path.GetFileName(source)}: tokens the reference colours identically resolve to multiple of our roles:\n" +
            string.Join("\n", result.Violations.Select(v => $"  '{v.Text}' ({v.Color}) -> {{{string.Join(", ", v.Roles)}}}")));
    }

    // ── Corpus discovery ─────────────────────────────────────────────────────

    private static IEnumerable<(string source, string highlighted, string grammar)> SupportedFiles()
    {
        var root = SyntaxTestsRoot();
        var sourceRoot = Path.Combine(root, "source");
        var highlightedRoot = Path.Combine(root, "highlighted");

        foreach (var langDir in new[] { "C-Sharp", "JavaScript", "TypeScript", "Python", "JSON", "Ruby" })
        {
            var dir = Path.Combine(sourceRoot, langDir);
            if (!Directory.Exists(dir)) continue;
            foreach (var src in Directory.EnumerateFiles(dir))
            {
                var resolution = HighlightingRegistry.Resolve(Path.GetFileName(src));
                if (resolution.TreeSitterLanguage is not { } grammar) continue; // skip files we don't grammar-highlight
                var highlighted = Path.Combine(highlightedRoot, langDir, Path.GetFileName(src));
                if (File.Exists(highlighted))
                    yield return (src, highlighted, grammar);
            }
        }
    }

    private static string SyntaxTestsRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Nexaflow.slnx")))
                return Path.Combine(dir.FullName, "src", "Nexaflow.Tests", "Nexaflow.Tests.Fixtures", "syntax-tests");
        throw new InvalidOperationException("Could not locate the repo root.");
    }

    // ── Analysis ─────────────────────────────────────────────────────────────

    private sealed record Violation(string Text, string Color, string[] Roles);
    private sealed record Result(int Runs, int Tested, bool Misaligned, List<Violation> Violations);

    private static Result Analyze(string sourcePath, string highlightedPath, string grammar)
    {
        var source = Lf(File.ReadAllText(sourcePath));
        var runs = ParseAnsi(Lf(File.ReadAllText(highlightedPath)));

        var defaultColor = runs.Where(r => r.Color != null && string.IsNullOrWhiteSpace(r.Text))
                               .GroupBy(r => r.Color)
                               .OrderByDescending(g => g.Count())
                               .FirstOrDefault()?.Key;

        var roleAt = new string?[source.Length];
        using (var highlighter = CodeHighlighter.TryCreate(grammar))
        {
            Assert.IsNotNull(highlighter, $"grammar '{grammar}' unavailable");
            foreach (var span in highlighter!.Highlight(source))
                for (var c = span.Start; c < span.Start + span.Length && c < roleAt.Length; c++)
                    roleAt[c] = span.Capture;
        }

        var map = new Dictionary<(string, string), HashSet<string>>();
        var misaligned = false;
        var tested = 0;
        var offset = 0;
        foreach (var run in runs)
        {
            var start = offset;
            offset += run.Text.Length;
            if (start + run.Text.Length > source.Length || source.AsSpan(start, run.Text.Length).ToString() != run.Text)
            {
                misaligned = true;
                break;
            }
            if (run.Color is null || run.Color == defaultColor || string.IsNullOrWhiteSpace(run.Text)) continue;

            var idx = start;
            while (idx < start + run.Text.Length && char.IsWhiteSpace(source[idx])) idx++;
            var role = roleAt[idx] ?? PlainRole;

            var key = (run.Text, run.Color);
            if (!map.TryGetValue(key, out var set)) map[key] = set = [];
            set.Add(role);
            tested++;
        }

        var violations = map.Where(kv => kv.Value.Count > 1)
                            .Select(kv => new Violation(kv.Key.Item1, kv.Key.Item2, kv.Value.OrderBy(r => r).ToArray()))
                            .OrderBy(v => v.Text)
                            .ToList();
        return new Result(runs.Count, tested, misaligned, violations);
    }

    // ── ANSI parsing ─────────────────────────────────────────────────────────

    private sealed record Run(string Text, string? Color);

    private static List<Run> ParseAnsi(string ansi)
    {
        var runs = new List<Run>();
        string? current = null;
        var sb = new StringBuilder();
        var i = 0;
        while (i < ansi.Length)
        {
            if (ansi[i] == '\x1b' && i + 1 < ansi.Length && ansi[i + 1] == '[')
            {
                if (sb.Length > 0) { runs.Add(new Run(sb.ToString(), current)); sb.Clear(); }
                var m = ansi.IndexOf('m', i);
                if (m < 0) break;
                current = ColorFromCodes(ansi.Substring(i + 2, m - (i + 2)), current);
                i = m + 1;
            }
            else { sb.Append(ansi[i]); i++; }
        }
        if (sb.Length > 0) runs.Add(new Run(sb.ToString(), current));
        return runs;
    }

    // "38;2;R;G;B" → "R;G;B"; "0" (reset) → null; attribute-only (e.g. "3"/"4") keeps the current colour.
    private static string? ColorFromCodes(string codes, string? current)
    {
        var p = codes.Split(';');
        for (var k = 0; k + 4 < p.Length + 1 && k + 4 < p.Length; k++)
            if (p[k] == "38" && p[k + 1] == "2")
                return $"{p[k + 2]};{p[k + 3]};{p[k + 4]}";
        return codes == "0" || Array.IndexOf(p, "0") >= 0 ? null : current;
    }

    private static string Lf(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");
}
