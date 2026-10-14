using System.Text;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Prose.Differential;

/// <summary>
/// The examples of the specifications Markdig is tested against — CommonMark's, and one for each extension the reading
/// enables — each as the markdown it was written as and the HTML the specification says it renders to.
///
/// <para>
/// Copied from Markdig's repository at the tag the package was built from (<c>Specs/LICENSE.md</c>), and read in the
/// format they are written in: an example is a fence of thirty-two backticks named <c>example</c> holding the markdown, a
/// line that is only a full stop, and the HTML. A tab is written as <c>→</c> so it can be seen, and is put back here.
/// </para>
/// </summary>
internal static class MarkdownSpecs
{
    /// <summary>The specification of the core syntax; every other file is an extension's.</summary>
    public const string Core = "CommonMark";

    /// <summary>Every specification copied in, by its file's name — one for each extension the reading enables.</summary>
    public static readonly string[] Names =
    [
        Core,
        "AbbreviationSpecs",
        "AlertBlockSpecs",
        "AutoLinks",
        "DefinitionListSpecs",
        "EmphasisExtraSpecs",
        "FigureFooterAndCiteSpecs",
        "GridTableSpecs",
        "ListExtraSpecs",
        "MathSpecs",
        "PipeTableSpecs",
        "PipeTableGfmSpecs",
        "TaskListSpecs",
        "YamlSpecs",
    ];

    private const string Fence = "````````````````````````````````";

    /// <summary>Where the specifications are, in the tree.</summary>
    public static string Folder => Path.Combine(
        TestSampleData.RepoRoot, "src", "Nexaflow.Tests", "Nexaflow.Tests.Markdown", "Prose", "Differential", "Specs");

    /// <summary>Every example one specification gives, in the order it gives them.</summary>
    public static IReadOnlyList<SpecExample> Examples(string name)
    {
        var lines = File.ReadAllText(Path.Combine(Folder, name + ".md")).Replace("\r\n", "\n").Split('\n');
        var examples = new List<SpecExample>();
        var section = string.Empty;

        for (var at = 0; at < lines.Length; at++)
        {
            if (lines[at].StartsWith('#'))
            {
                section = lines[at].TrimStart('#').Trim();
                continue;
            }

            if (lines[at] != Fence + " example") continue;

            var markdown = new StringBuilder();
            for (at++; at < lines.Length && lines[at] != "."; at++) markdown.Append(lines[at]).Append('\n');

            var html = new StringBuilder();
            for (at++; at < lines.Length && lines[at] != Fence; at++) html.Append(lines[at]).Append('\n');

            examples.Add(new SpecExample(name, examples.Count + 1, section, markdown.ToString().Replace('→', '\t'), html.ToString()));
        }

        return examples;
    }
}

/// <summary>One example of a specification: what is written, and the HTML the specification says it comes to.</summary>
/// <param name="Spec">The specification it is from, by its file's name.</param>
/// <param name="Number">Its place among that specification's examples, counting from one — the number the specification's own tests use.</param>
/// <param name="Section">The heading it stands under.</param>
internal sealed record SpecExample(string Spec, int Number, string Section, string Markdown, string Html)
{
    /// <summary>What the example is called wherever it is named: <c>CommonMark#312</c>.</summary>
    public string Id => $"{this.Spec}#{this.Number}";
}
