using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Diagrams move onto the shared layout tree one at a time, each built from the Mermaid kit — <c>docs/mermaid-diagrams.md</c>.
/// These rules hold that shape where whoever is converting a diagram would otherwise go by whatever code is nearest to hand:
/// a legacy renderer drawing with WPF controls, a legacy parser picking lines apart with regular expressions.
///
/// <list type="bullet">
/// <item><b>The legacy diagram code is frozen</b> (<c>src/Nexaflow.Visuals.Text/Markdown/Graphs/</c>, its handlers aside): no
/// file is added to it, none grows, and each says so on its first line. A ratchet, like
/// <see cref="HandRolledModalRatchetTests"/>: <see cref="BaselineFile"/> holds each file's length, and a file deleted — its
/// last diagram moved — has to leave the baseline too.</item>
/// <item><b>Code on the shared tree never reaches into it.</b> A layout the legacy code has that a diagram needs is moved into
/// the kit and made to take the kit's own input.</item>
/// <item><b>A diagram's own code goes through the kit</b> — its grammar through <c>MermaidLine</c>, its builder through
/// <c>DiagramWords</c>, <c>DiagramInk</c> and the kit's shapes — so what the kit decides once is not decided again, differently,
/// per diagram. The kit's own files, directly in the <c>Mermaid</c> folders, are what these rules send everything else to.</item>
/// </list>
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class MermaidDiagramRulesTests
{
    private static readonly string Root = RepoRoot.Locate();
    private const string Grammars = "src/Nexaflow.Markdown/Mermaid/";
    private const string Builders = "src/Nexaflow.Visuals.Text/Markdown/Mermaid/";

    private sealed record Rule(Regex Found, string Instead);

    /// <summary>What nothing on the shared tree names: the legacy parsers, models, layouts and renderers.</summary>
    private static readonly Rule[] Reaching =
    [
        new(new(@"\bGraphs\.(Parsers|Charts|Layout)\b"), "the legacy parsers, models and layouts stay legacy — move what is needed into the kit"),
        new(new(@"\bWpf\w+Renderer\b|\bGraphDiagramView\b|\bSugiyamaLayout\b|\bMermaid(?!Parser\b)\w+Parser\b"), "the legacy renderers and parsers stay legacy — draw it on the layout tree"),
    ];

    /// <summary>What a diagram's own reading does through the kit.</summary>
    private static readonly Rule[] Reading =
    [
        new(new(@"ContentNode\.Leaf\(\s*Kinds\.(Space|Token|Comment)\b"), "MermaidLine.Space, Room and Token — space, machinery and comments come from the line reader"),
        new(new(@"char\.IsWhiteSpace\("), "MermaidLine.Space, Room and Past"),
        new(new(@"IndexOf\(\s*('""'|""\\"""")"), "MermaidLine.Quoted, Name and Label"),
        new(new(@"\bRegex\b"), "MermaidLine — a grammar reads a line piece by piece, keeping every character"),
        new(new(@"\b(double|int|decimal|float)\.TryParse\("), "MermaidNumber.Read and MermaidParts.Number"),
        new(new(@"Section\(\s*""(config|themeVariables)""\s*\)"), "MermaidConfig.Diagram and MermaidConfig.Theme"),
    ];

    /// <summary>What a diagram's own drawing does through the kit.</summary>
    private static readonly Rule[] Drawing =
    [
        new(new(@"\bDiagramColour\b"), "DiagramInk — a builder asks for a brush that follows the theme, not for colour arithmetic"),
        new(new(@"new FormattedText\("), "MermaidBuilder.Written and Worked — every word a diagram sets is DiagramWords"),
        new(new(@"LayoutText\.(Words|Hole|Place)\("), "DiagramWords.Set"),
        new(new(@"new SolidColorBrush\(|Color\.From(Rgb|Argb)\(|\bColors\.|\bBrushes\.|""#[0-9A-Fa-f]{3,8}"""), "DiagramInk and the palette — a colour nobody wrote is the theme's"),
        new(new(@"\.Opacity\s*="), "DiagramInk.Faded"),
        new(new(@"Palette\.Series\s*\["), "DiagramInk.Series"),
        new(new(@"System\.Windows\.Controls|\bTextBlock\b|\bCanvas\b"), "the layout tree — a diagram is pieces and marks, not controls"),
        new(new(@":\s*MermaidBuilder\s*(\{|$)"), "MermaidBuilder<TDiagram> — the block read into its model, and the model drawn"),
    ];

    [TestMethod]
    [TestCategory("Unit")]
    public void A_diagrams_own_reading_goes_through_the_kit() => Holds(Files(Grammars, typeFoldersOnly: true), Reading);

    [TestMethod]
    [TestCategory("Unit")]
    public void A_diagrams_own_drawing_goes_through_the_kit() => Holds(Files(Builders, typeFoldersOnly: true), Drawing);

    // ── Reading the tree ────────────────────────────────────────────────────

    private static void Holds(IEnumerable<string> files, IReadOnlyList<Rule> rules)
    {
        var broken = new List<string>();

        foreach (var file in files)
        {
            var number = 0;
            foreach (var line in File.ReadLines(Path.Combine(Root, file)))
            {
                number++;

                // What a comment says about the legacy code is not a use of it.
                var code = line.TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal)) continue;

                foreach (var rule in rules)
                    if (rule.Found.Match(code) is { Success: true } found)
                        broken.Add($"  {file}:{number}  '{found.Value}' — instead: {rule.Instead}");
            }
        }

        Assert.AreEqual(0, broken.Count,
            "Diagram code on the shared layout tree is built from the Mermaid kit (docs/mermaid-diagrams.md):\n" + string.Join("\n", broken));
    }

    /// <summary>The C# files under a folder — or only those in the folders under it, one per diagram, leaving the kit's own out.</summary>
    private static IEnumerable<string> Files(string folder, bool typeFoldersOnly)
    {
        var path = Path.Combine(Root, folder);
        if (!Directory.Exists(path)) return [];

        return Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(Root, file).Replace('\\', '/'))
            .Where(file => !typeFoldersOnly || file[folder.Length..].Contains('/'))
            .Where(file => !IsBuildOutput(file))
            .Order(StringComparer.Ordinal);
    }

    private static bool IsBuildOutput(string file) => file.Contains("/obj/", StringComparison.Ordinal) || file.Contains("/bin/", StringComparison.Ordinal);
}
