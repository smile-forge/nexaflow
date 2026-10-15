using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Prose.Corpus;

/// <summary>
/// Every document the reading is held to, each with a name that survives the corpus growing.
///
/// <para>
/// Four sets, each speaking for something the others do not: <see cref="Samples"/>, everything the app knows how to render;
/// the specifications' examples (<see cref="Core"/> and <see cref="Extensions"/>), every corner somebody thought to write
/// down; and <see cref="Pages"/>, the help pages and the documentation, which nobody wrote as tests. A fifth is made from
/// them (<see cref="Cuts"/>): what an editor holds all day, a block stopped part-way through.
/// </para>
/// <para>
/// Every document is read with its line endings made <c>\n</c>, so what it is called and what it says are the same on
/// a machine that checks files out with <c>\r\n</c>.
/// </para>
/// </summary>
internal static class MarkdownCorpus
{
    public const string Samples = "samples";
    public const string Core = "commonmark";
    public const string Extensions = "extensions";
    public const string Pages = "pages";
    public const string Cuts = "half-written";

    private static readonly Lazy<IReadOnlyList<MarkdownInput>> WholeLazy = new(Gathered);

    /// <summary>Every whole document, set by set.</summary>
    public static IReadOnlyList<MarkdownInput> Whole => WholeLazy.Value;

    /// <summary>
    /// Each block of <paramref name="document"/> that spans more than one line, as a document of its own — from the start of the
    /// line it starts on to the end of the line it ends on: a block part-way written is the last one in what an editor holds,
    /// and every block before it is finished.
    /// </summary>
    public static IEnumerable<MarkdownInput> Blocks(MarkdownInput document)
    {
        var source = document.Source;

        foreach (var block in ContentPart.Of(MarkdownParser.Read(source)).Children)
        {
            if (block.Derived || block.Role == Roles.Trivia || block.Length == 0) continue;

            var from = block.Start == 0 ? 0 : source.LastIndexOf('\n', block.Start - 1) + 1;
            var to = source.IndexOf('\n', Math.Clamp(block.End - 1, from, Math.Max(source.Length - 1, 0)));
            to = to < 0 ? source.Length : to + 1;

            var text = source[from..to];
            var first = text.IndexOf('\n');
            if (first < 0 || first + 1 >= text.Length) continue;

            yield return new MarkdownInput(Cuts, $"{document.Id}@{source.AsSpan(0, from).Count('\n') + 1}", text);
        }
    }

    /// <summary>
    /// <paramref name="block"/> stopped at the end of a line, at as many as <paramref name="most"/> places spread evenly
    /// through it — never at its end, which is the whole of it.
    /// </summary>
    public static IEnumerable<MarkdownInput> Cut(MarkdownInput block, int most)
    {
        var ends = new List<int>();
        for (var at = block.Source.IndexOf('\n'); at >= 0 && at + 1 < block.Source.Length; at = block.Source.IndexOf('\n', at + 1))
            ends.Add(at + 1);

        var step = Math.Max(1, (ends.Count + most - 1) / Math.Max(most, 1));

        for (var line = step - 1; line < ends.Count; line += step)
            yield return block with { Id = $"{block.Id}+{line + 1}", Source = block.Source[..ends[line]] };
    }

    private static List<MarkdownInput> Gathered()
    {
        var inputs = new List<MarkdownInput>();

        foreach (var path in TestSampleData.Files("markdown").Order(StringComparer.Ordinal))
            inputs.Add(new MarkdownInput(Samples, $"samples/{Path.GetFileName(path)}", Read(path)));

        foreach (var name in MarkdownSpecs.Names)
            foreach (var example in MarkdownSpecs.Examples(name))
                inputs.Add(new MarkdownInput(name == MarkdownSpecs.Core ? Core : Extensions, example.Id, example.Markdown));

        var root = TestSampleData.RepoRoot;

        foreach (var path in PagesUnder(root).Order(StringComparer.Ordinal))
            inputs.Add(new MarkdownInput(Pages, $"pages/{Path.GetRelativePath(root, path).Replace('\\', '/')}", Read(path)));

        return inputs;
    }

    /// <summary>The documentation, and every help page every language pack ships.</summary>
    private static IEnumerable<string> PagesUnder(string root)
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories))
            yield return path;

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.md", SearchOption.AllDirectories))
        {
            var folders = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);

            if (folders.Contains("bin") || folders.Contains("obj")) continue;
            if (folders.Contains("Localization") && folders.Contains("help")) yield return path;
        }
    }

    private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");
}

/// <summary>One document of the corpus.</summary>
/// <param name="Set">Which set it comes from.</param>
/// <param name="Id">What it is called wherever it is named — <c>samples/mixed-content.md</c>, <c>CommonMark#312</c>.</param>
internal sealed record MarkdownInput(string Set, string Id, string Source);
