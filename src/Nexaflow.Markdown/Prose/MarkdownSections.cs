using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// One heading's section: where the heading stands among the document's parts, how deep it is, the name a link reaches it
/// by, where the section ends, and the sections under it.
///
/// <para>
/// Places are counted among the document's own parts — the children of its root — which is where a block stands in the
/// reading and what no later stage moves. They are not offsets: a section says which blocks it holds, never where their
/// characters are.
/// </para>
/// </summary>
/// <param name="Heading">The heading's place among the document's parts.</param>
/// <param name="Rank">How deep the heading is, one to six.</param>
/// <param name="Anchor">The name the heading answers to, where the document's working over gave it one.</param>
/// <param name="End">The place just past the last part the section holds.</param>
/// <param name="Sections">The sections under it, in the order written.</param>
/// <param name="Holds">Whether anything stands under the heading — a block of its own or a section of its own — for folding it to hide.</param>
public sealed record MarkdownSection(int Heading, int Rank, string? Anchor, int End, IReadOnlyList<MarkdownSection> Sections, bool Holds)
{
    /// <summary>The place just past the section's own blocks: where the first section under it starts, or where it ends.</summary>
    public int Body => Sections.Count > 0 ? Sections[0].Heading : End;
}

/// <summary>
/// The sections a document's headings make (<see cref="WithSections"/>): what came before the first heading, and the
/// sections in the order written, each holding the ones under it.
///
/// <para>
/// A heading says only how deep it is; its section runs on to the next heading as deep or deeper, and a heading skipping a
/// rank still stands under the nearest one above it. Only the document's own blocks make sections — a heading written in a
/// quote or a list item heads nothing.
/// </para>
/// </summary>
/// <param name="Preamble">The place just past the parts written before the first heading.</param>
/// <param name="Sections">The outermost sections, in the order written.</param>
public sealed record MarkdownSections(int Preamble, IReadOnlyList<MarkdownSection> Sections)
{
    /// <summary>What a document with no heading is: everything before a first heading, and no section.</summary>
    public static MarkdownSections None { get; } = new(0, []);

    /// <summary>The sections the document <paramref name="root"/> was read into, or <see cref="None"/> where it has none.</summary>
    public static MarkdownSections Of(ContentNode root) => Held(root) ?? None;

    /// <summary>Every section, each before the ones under it.</summary>
    public IEnumerable<MarkdownSection> All()
    {
        var waiting = new Stack<MarkdownSection>();
        for (var at = Sections.Count - 1; at >= 0; at--) waiting.Push(Sections[at]);

        while (waiting.Count > 0)
        {
            var section = waiting.Pop();
            yield return section;

            for (var at = section.Sections.Count - 1; at >= 0; at--) waiting.Push(section.Sections[at]);
        }
    }

    /// <summary>The name a heading answers to, where the document's working over gave it one (<see cref="WithTargets"/>).</summary>
    public static string? AnchorOf(ContentNode heading)
    {
        foreach (var child in heading.Children)
            foreach (var held in child.Children)
                if (held.Kind == MarkdownKinds.Anchor && held.Held is string id)
                    return id;

        return null;
    }

    /// <summary>The sections hung on <paramref name="root"/>, or null where none were.</summary>
    internal static MarkdownSections? Held(ContentNode root)
    {
        foreach (var child in root.Children)
            if (child.IsDerived && child.Kind == MarkdownKinds.Sections)
                foreach (var held in child.Children)
                    if (held.Held is MarkdownSections sections) return sections;

        return null;
    }

    /// <summary>The sections the parts of <paramref name="root"/> make.</summary>
    internal static MarkdownSections Read(ContentNode root)
    {
        var outermost = new List<Opened>();
        var open = new Stack<Opened>();
        var preamble = root.Children.Count;

        for (var at = 0; at < root.Children.Count; at++)
        {
            var part = root.Children[at];
            if (part.IsDerived || part.Role == Roles.Trivia) continue;

            if (part.Kind != MarkdownKinds.Heading)
            {
                if (open.Count > 0) open.Peek().Holds = true;
                continue;
            }

            var rank = int.TryParse(part.Said(MarkdownRoles.Rank), System.Globalization.CultureInfo.InvariantCulture, out var said)
                ? Math.Clamp(said, 1, 6)
                : 1;

            while (open.Count > 0 && open.Peek().Rank >= rank) open.Pop().End = at;

            var section = new Opened(at, rank, AnchorOf(part));
            if (open.Count == 0)
            {
                if (outermost.Count == 0) preamble = at;
                outermost.Add(section);
            }
            else
            {
                open.Peek().Under.Add(section);
                open.Peek().Holds = true;
            }

            open.Push(section);
        }

        var end = Ended(root);
        while (open.Count > 0) open.Pop().End = end;

        return outermost.Count == 0 ? None : new MarkdownSections(preamble, [.. outermost.Select(section => section.Made())]);
    }

    /// <summary>The place just past the document's last part of its own — before anything hung on it.</summary>
    private static int Ended(ContentNode root)
    {
        var end = root.Children.Count;
        while (end > 0 && root.Children[end - 1].IsDerived) end--;

        return end;
    }

    /// <summary>A section while the headings after it are still being read.</summary>
    private sealed class Opened(int heading, int rank, string? anchor)
    {
        public int Rank => rank;

        public int End { get; set; }

        public bool Holds { get; set; }

        public List<Opened> Under { get; } = [];

        public MarkdownSection Made() => new(heading, rank, anchor, End, [.. Under.Select(section => section.Made())], Holds);
    }
}
