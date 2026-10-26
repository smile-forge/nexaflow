using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.IO.Documents.Processing;

/// <summary>
/// A markdown document read for whole-document work: its reading — parsed and worked over, with the sections its headings make
/// hung on it (<see cref="MarkdownSections"/>) — and what every pass asks of it: the blocks a section holds, the words a heading
/// says, an outline that fits what a model can be handed, and the document cut into stretches a model can read.
/// </summary>
public sealed class DocumentReading
{
    private DocumentReading(string source, ContentNode root)
    {
        Source   = source;
        Root     = ContentPart.Of(root);
        Sections = MarkdownSections.Of(root);
    }

    /// <summary><paramref name="source"/> read as a markdown document is.</summary>
    public static DocumentReading Of(string source) => new(source, MarkdownParser.Worked(source));

    /// <summary>The document as written.</summary>
    public string Source { get; }

    /// <summary>The document's reading, from its root.</summary>
    public ContentPart Root { get; }

    /// <summary>The sections its headings make.</summary>
    public MarkdownSections Sections { get; }

    /// <summary>The place just past what comes before the first heading — the whole document, where it has none.</summary>
    public int Preamble => Sections.Sections.Count == 0 ? Root.Children.Count : Sections.Preamble;

    /// <summary>The document's part at <paramref name="at"/>.</summary>
    public ContentPart Part(int at) => Root.Children[at];

    /// <summary>
    /// The blocks a reader reads from place <paramref name="from"/> up to <paramref name="to"/>: not the space between blocks,
    /// nothing hung on the document, and not what a document says about itself or where its links go — its front matter and its
    /// link definitions.
    /// </summary>
    public IEnumerable<ContentPart> Blocks(int from, int to)
    {
        for (var at = Math.Max(0, from); at < Math.Min(to, Root.Children.Count); at++)
        {
            var part = Root.Children[at];
            if (part.Derived || part.Role == Roles.Trivia || part.Kind is MarkdownKinds.FrontMatter or MarkdownKinds.Reference) continue;

            yield return part;
        }
    }

    /// <summary>What the blocks from <paramref name="from"/> up to <paramref name="to"/> say, as written, a blank line apart.</summary>
    public string Text(int from, int to) => string.Join("\n\n", Blocks(from, to).Select(Written));

    /// <summary>What a block says, as written, without the line break closing it.</summary>
    public static string Written(ContentPart block) => block.Print().TrimEnd('\r', '\n');

    /// <summary>
    /// The words of <paramref name="part"/>, on one line: what reads as words, with a break between lines read as the space it is
    /// between two words — the lines a block was wrapped at are not the words', and two words either side of one are not one word.
    /// </summary>
    public static string Words(ContentPart part) =>
        string.Concat(part.SelfAndDescendants()
                          .Where(leaf => leaf.Children.Count == 0)
                          .Select(leaf => MarkdownParser.ReadsAsWords(leaf) ? leaf.Print()
                                        : leaf.Print() is var said && said.Contains('\n') && string.IsNullOrWhiteSpace(said) ? " "
                                        : string.Empty))
              .Trim();

    /// <summary>The words of the document's first heading of the first rank, or null where it has none.</summary>
    public string? Title => Sections.All().FirstOrDefault(section => section.Rank == 1) is { } top ? Words(Part(top.Heading)) : null;

    /// <summary>What the document says about itself, as written in its front matter, or null where it has none.</summary>
    public string? FrontMatter => Root.Children.FirstOrDefault(part => part.Kind == MarkdownKinds.FrontMatter)?.Print();

    /// <summary>The document cut into stretches of at most <paramref name="room"/> characters, at the ends of blocks (<see cref="DocumentCuts.Pack"/>).</summary>
    public IReadOnlyList<string> Stretches(int room) => DocumentCuts.Pack(Blocks(0, Root.Children.Count).Select(Written), room);

    /// <summary>
    /// The document's words cut into stretches the same way: each block's words alone (<see cref="Words"/>), without the markup round
    /// them, the code or the formulas among them — what a reader reads as words, and so all that a translation of it translates.
    /// </summary>
    public IReadOnlyList<string> WordStretches(int room) =>
        DocumentCuts.Pack(Blocks(0, Root.Children.Count).Select(Words).Where(words => words.Length > 0), room);

    /// <summary>
    /// The document's headings as an indented outline in at most <paramref name="room"/> characters: the deepest are left out
    /// first, and where even the outermost do not fit, the outline is cut short and says so.
    /// </summary>
    public string Outline(int room)
    {
        var all = Nested(Sections.Sections, 1).ToList();
        if (all.Count == 0) return string.Empty;

        for (var deepest = all.Max(entry => entry.Depth); deepest >= 1; deepest--)
        {
            var lines = all.Where(entry => entry.Depth <= deepest).Select(Line).ToList();
            var said = string.Join('\n', lines);
            if (said.Length <= room) return said;

            if (deepest > 1) continue;

            var kept = new List<string>();
            var length = 0;
            foreach (var line in lines)
            {
                if (length + line.Length + 4 > room) break;
                kept.Add(line);
                length += line.Length + 1;
            }

            kept.Add("- …");
            return string.Join('\n', kept);
        }

        return string.Empty;

        string Line((MarkdownSection Section, int Depth) entry) => $"{new string(' ', (entry.Depth - 1) * 2)}- {Words(Part(entry.Section.Heading))}";
    }

    /// <summary>Every section with how deep it stands among the others, each before the ones under it.</summary>
    private static IEnumerable<(MarkdownSection Section, int Depth)> Nested(IReadOnlyList<MarkdownSection> sections, int depth)
    {
        foreach (var section in sections)
        {
            yield return (section, depth);

            foreach (var under in Nested(section.Sections, depth + 1)) yield return under;
        }
    }
}
