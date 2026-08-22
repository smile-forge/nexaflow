using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// A long file may earn its length; a long file that is also a grab-bag of unrelated types has not.
///
/// <para>
/// The rule is deliberately narrow: past <see cref="MaxLinesForMultipleTypes"/> lines, a file may declare
/// exactly one <b>top-level</b> type. It says nothing about files below that, and nothing about how big one
/// type is allowed to get — those are judgement calls a test would only get wrong. What it does catch is the
/// specific decay where a file grows past the point anyone reads it whole and then accumulates neighbours.
/// </para>
/// <para>
/// <b>Nested types do not count</b>, and that exemption is the point rather than a loophole: a private record
/// or enum inside the class that uses it is cohesion, and forcing it into its own file to satisfy a
/// line-count rule would make the codebase worse. Only siblings at file scope count.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class FileSizeRulesTests
{
    /// <summary>
    /// Past this, a file gets one top-level type. Chosen for readers rather than compilers: comprehension
    /// drops off well before this, and an assistant reading the file has a context budget that a
    /// multi-thousand-line file eats outright. It is a backstop, not a target — plenty of files here should
    /// be far smaller.
    /// </summary>
    private const int MaxLinesForMultipleTypes = 800;

    /// <summary>
    /// Files that are data rather than code, where "one type per file" would be meaningless. The sample
    /// corpora are generated constants; the syntax-test corpus is other languages' source kept verbatim.
    /// </summary>
    private static bool IsGeneratedOrCorpus(string relative) =>
        relative.Contains("/syntax-tests/", StringComparison.OrdinalIgnoreCase)
        || relative.EndsWith("/MarkdownSamples.cs", StringComparison.OrdinalIgnoreCase)
        || relative.EndsWith("/CodeSamples.cs", StringComparison.OrdinalIgnoreCase);

    [TestMethod]
    public void ALongFileDeclaresAtMostOneTopLevelType()
    {
        var repo = RepoRoot.Locate();
        var extractor = new CodeStructureExtractor();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            var relative = Path.GetRelativePath(repo, file).Replace('\\', '/');
            if (IsGeneratedOrCorpus(relative)) continue;

            var text = File.ReadAllText(file);
            var lines = text.AsSpan().Count('\n') + 1;
            if (lines <= MaxLinesForMultipleTypes) continue;

            // A top-level type is one whose AST path has no parent segment; nested types are exempt.
            // `file`-scoped types are exempt too, and not by courtesy: the language scopes them to this file,
            // so extracting one is not a refactor the rule can be asking for. They are the C# way of writing
            // a private helper that happens not to fit inside the class, which is the same cohesion argument
            // that exempts nested types.
            var sourceLines = text.Replace("\r\n", "\n").Split('\n');
            bool IsFileScoped(OutlineType t) =>
                t.Line >= 1 && t.Line <= sourceLines.Length
                && sourceLines[t.Line - 1].TrimStart().StartsWith("file ", StringComparison.Ordinal);

            var topLevel = extractor.Extract("c-sharp", text).Types
                                    .Where(t => !t.AstPath.Contains('/') && !IsFileScoped(t))
                                    .Select(t => t.Name)
                                    .ToList();

            if (topLevel.Count > 1)
                offenders.Add($"{relative} — {lines} lines, {topLevel.Count} top-level types: {string.Join(", ", topLevel)}");
        }

        Assert.AreEqual(0, offenders.Count,
            $"a file over {MaxLinesForMultipleTypes} lines must declare a single top-level type, so that its "
            + $"size is at least the cost of one coherent thing:{Environment.NewLine}  "
            + string.Join(Environment.NewLine + "  ", offenders));
    }
}
