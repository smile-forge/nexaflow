namespace Nexaflow.Markdown.Ast;

/// <summary>
/// One heading's section: where the heading stands among a reading's parts, how high it ranks, the name a link reaches it by, where
/// the section ends, and the sections under it.
///
/// <para>
/// Places are counted among the reading's own parts — the children of its root — which is where a block stands in the reading and
/// what no later stage moves. They are not offsets: a section says which blocks it holds, never where their characters are.
/// </para>
/// </summary>
/// <param name="Heading">The heading's place among the reading's parts.</param>
/// <param name="Rank">How high the heading ranks — one is the highest.</param>
/// <param name="Name">The name the heading answers to (<see cref="ContentOutline"/>).</param>
/// <param name="End">The place just past the last part the section holds.</param>
/// <param name="Sections">The sections under it, in the order written.</param>
/// <param name="Holds">Whether anything stands under the heading — a block of its own or a section of its own — for folding it to hide.</param>
public sealed record ContentSection(int Heading, int Rank, string Name, int End, IReadOnlyList<ContentSection> Sections, bool Holds)
{
    /// <summary>The place just past the section's own blocks: where the first section under it starts, or where it ends.</summary>
    public int Body => Sections.Count > 0 ? Sections[0].Heading : End;
}

/// <summary>
/// The sections a reading worked over whole comes to, as a tree: what came before the first heading, and the sections in the order
/// written, each holding the ones under it — the outline of the reading (<see cref="ContentOutline"/>) put the way whatever reads a
/// whole document section by section reads it.
///
/// <para>
/// A heading says only how high it ranks; its section runs on to the next heading as high or higher, and a heading skipping a rank
/// still stands under the nearest one above it. Only the reading's own blocks make sections — a heading written in a quote or a list
/// item heads nothing.
/// </para>
/// </summary>
/// <param name="Preamble">The place just past the parts written before the first heading.</param>
/// <param name="Sections">The outermost sections, in the order written.</param>
public sealed record ContentSections(int Preamble, IReadOnlyList<ContentSection> Sections)
{
    /// <summary>What a reading with no heading is: everything before a first heading, and no section.</summary>
    public static ContentSections None { get; } = new(0, []);

    /// <summary>The sections the reading <paramref name="root"/>, worked over whole, comes to — <see cref="None"/> where it has none.</summary>
    public static ContentSections Of(ContentNode root)
    {
        var outline = ContentOutline.Of(root);
        var entries = outline[0].Unit.Entries;

        var outermost = new List<Opened>();
        var open = new Stack<Opened>();
        var preamble = root.Children.Count;

        for (var at = 0; at < entries.Count; at++)
        {
            if (entries[at] is not { Heads: true } heading) continue;

            while (open.Count > 0 && open.Peek().Rank >= heading.Rank) open.Pop().End = heading.Block;

            var section = new Opened(heading.Block, heading.Rank, outline.Name(0, at), outline.Section(0, at)!.Holds);
            if (open.Count == 0)
            {
                if (outermost.Count == 0) preamble = heading.Block;
                outermost.Add(section);
            }
            else open.Peek().Under.Add(section);

            open.Push(section);
        }

        var end = Ended(root);
        while (open.Count > 0) open.Pop().End = end;

        return outermost.Count == 0 ? None : new ContentSections(preamble, [.. outermost.Select(section => section.Made())]);
    }

    /// <summary>Every section, each before the ones under it.</summary>
    public IEnumerable<ContentSection> All()
    {
        var waiting = new Stack<ContentSection>();
        for (var at = Sections.Count - 1; at >= 0; at--) waiting.Push(Sections[at]);

        while (waiting.Count > 0)
        {
            var section = waiting.Pop();
            yield return section;

            for (var at = section.Sections.Count - 1; at >= 0; at--) waiting.Push(section.Sections[at]);
        }
    }

    /// <summary>The place just past the reading's last part of its own — before anything hung on it.</summary>
    private static int Ended(ContentNode root)
    {
        var end = root.Children.Count;
        while (end > 0 && root.Children[end - 1].IsDerived) end--;

        return end;
    }

    /// <summary>A section while the headings after it are still being read.</summary>
    private sealed class Opened(int heading, int rank, string name, bool holds)
    {
        public int Rank => rank;

        public int End { get; set; }

        public List<Opened> Under { get; } = [];

        public ContentSection Made() => new(heading, rank, name, End, [.. Under.Select(section => section.Made())], holds);
    }
}
