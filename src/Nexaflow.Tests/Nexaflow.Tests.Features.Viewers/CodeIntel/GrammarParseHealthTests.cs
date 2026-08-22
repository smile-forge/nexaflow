using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.CodeIntel;

/// <summary>
/// Guards the thing that has no other alarm: a grammar too old for the language it is parsing.
///
/// <para>
/// A stale C# grammar turned one slice pattern into a root-level parse error, and the whole of
/// <c>Program.cs</c> — 1,900 lines, ~90 methods — silently contributed a single type to the knowledge
/// graph. No exception, no warning, no failing test; the file simply was not there. That is the failure
/// mode worth a permanent guard, because every other symptom of it (a dead-looking type, a missing
/// caller) reads as a fact about the code rather than a fact about the parser.
/// </para>
/// <para>
/// So this asserts the negative directly against the real repository: nothing under <c>src/</c> may fail
/// to parse. It is deliberately a corpus test rather than a fixture test — a fixture only ever contains
/// the constructs someone already thought of, and the constructs nobody thought of are exactly the ones
/// that break.
/// </para>
/// </summary>
[TestClass]
[CoversNode("syntax-native-grammars")]
public class GrammarParseHealthTests
{
    /// <summary>Files that are *not* source in the language their extension claims, and so cannot parse.
    /// The highlighter corpus stores rendered output — ANSI escape sequences, not C#.</summary>
    private static bool IsNotReallySource(string relative) =>
        relative.Contains("syntax-tests/highlighted/", StringComparison.OrdinalIgnoreCase);

    private static string Repo => Nexaflow.Tests.Features.Architecture.RepoRoot.Locate();

    private static IEnumerable<string> SourceFiles(string extension) =>
        Directory.EnumerateFiles(Path.Combine(Repo, "src"), "*" + extension, SearchOption.AllDirectories)
                 .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                          && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static void AssertEveryFileParses(string extension, string grammarId)
    {
        var extractor = new CodeStructureExtractor();
        var failures = new List<string>();
        var scanned = 0;

        foreach (var file in SourceFiles(extension))
        {
            var relative = Path.GetRelativePath(Repo, file).Replace('\\', '/');
            if (IsNotReallySource(relative)) continue;
            scanned++;

            if (extractor.Extract(grammarId, File.ReadAllText(file)).ParseFailed)
                failures.Add(relative);
        }

        Assert.AreNotEqual(0, scanned, $"found no {extension} files to check — the guard would pass vacuously");
        Assert.AreEqual(0, failures.Count,
            $"the {grammarId} grammar cannot parse {failures.Count} file(s), which are therefore absent from "
            + $"the knowledge graph and the outline pane:{Environment.NewLine}  "
            + string.Join(Environment.NewLine + "  ", failures.Take(10)));
    }

    [TestMethod]
    public void EveryCSharpFileInTheRepositoryParses() => AssertEveryFileParses(".cs", "c-sharp");

    [TestMethod]
    public void EveryXamlFileInTheRepositoryParses() => AssertEveryFileParses(".xaml", "xaml");

    [TestMethod]
    public void TheLanguageFeaturesThisRepositoryActuallyUsesAreSupported()
    {
        // Each of these was a real parse error under the grammar the NuGet package shipped, and each appears
        // in Nexaflow's own source. Named individually so a future regression says which feature was lost
        // rather than just pointing at a file.
        var required = new Dictionary<string, string>
        {
            ["empty collection expression"] = "class A { List<int> x = []; }",
            ["collection expression with elements"] = "class A { int[] x = [1, 2]; }",
            ["spread element"] = "class A { int[] F(int[] b) => [.. b]; }",
            ["slice pattern binding the rest"] = "class A { int F(string[] a) => a switch { [.. var r] => 1, _ => 2 }; }",
            ["list pattern with a leading element"] = "class A { int F(string[] a) => a switch { [\"x\", .. var r] => 1, _ => 2 }; }",
            ["primary constructor"] = "class A(int x) { int F() => x; }",
            ["required member"] = "class A { public required int X { get; init; } }",
        };

        var extractor = new CodeStructureExtractor();
        var lost = required.Where(kv => extractor.Extract("c-sharp", kv.Value).ParseFailed)
                           .Select(kv => kv.Key)
                           .ToList();

        Assert.AreEqual(0, lost.Count, "the C# grammar no longer parses: " + string.Join(", ", lost));
    }

    [TestMethod]
    public void ARootLevelParseFailureIsReported_NotSilentlyEmpty()
    {
        // The flag has to be trustworthy in both directions, or the guard above proves nothing. Note that
        // tree-sitter recovers from a great deal — whole files of the wrong language still reduce to a tree —
        // so the negative control is the one input known to defeat it: the highlighter corpus, which stores
        // rendered output with ANSI escapes rather than source. That is also exactly what the guard excludes,
        // so this pins the exclusion's justification rather than inventing a separate one.
        var corpus = Path.Combine(Repo, "src/Nexaflow.Tests/Nexaflow.Tests.Fixtures/syntax-tests/highlighted/C-Sharp/Stack.cs");
        if (!File.Exists(corpus)) Assert.Inconclusive($"the corpus fixture is missing: {corpus}");

        Assert.IsTrue(new CodeStructureExtractor().Extract("c-sharp", File.ReadAllText(corpus)).ParseFailed,
                      "an unparseable file must say so");

        var fine = new CodeStructureExtractor().Extract("c-sharp", "class A { }");
        Assert.IsFalse(fine.ParseFailed);
        Assert.IsFalse(new CodeStructureExtractor().Extract("c-sharp", "").ParseFailed,
                       "an empty file is empty, not broken");
    }
}
