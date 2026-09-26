using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// The lines a diagram written as an outline is made of — a kanban board's columns and cards, a mindmap's nodes: a node, and the
/// <c>::icon(…)</c> and <c>:::class</c> lines decorating the node above it. What nests a node under another is its indentation
/// (<see cref="MermaidParts.Indent(ContentPart)"/>), which is the whole block's business rather than a line's.
///
/// <para>
/// A node is its id, its title in brackets, or both — a bare id being its title too. Mermaid's brackets are <c>[…]</c>,
/// <c>(…)</c>, <c>((…))</c>, <c>{{…}}</c>, <c>)…(</c> and <c>))…((</c>, and what each means is the diagram's own (a mindmap
/// draws a shape for each; a kanban board draws them all the same).
/// </para>
/// </summary>
public static class MermaidOutline
{
    public const string IconMark = "::icon(";
    public const string ClassMark = ":::";

    /// <summary>The brackets a title is written in, each opening with its closing, the longest first.</summary>
    public static readonly IReadOnlyList<(string Open, string Close)> Brackets =
        [("((", "))"), ("{{", "}}"), ("))", "(("), ("-)", "(-"), ("(-", "-)"), ("[", "]"), ("(", ")"), (")", "(")];

    /// <summary>
    /// A node's id and title, read into <paramref name="line"/>: false where a title is never closed, and otherwise true, saying
    /// whether one was written at all.
    /// </summary>
    /// <param name="stops">The characters that end a bare id, which is everything the brackets open with, and whatever else the diagram does not allow.</param>
    /// <param name="brackets">The brackets a title may be written in, longest opening first — <see cref="Brackets"/> where the diagram writes those.</param>
    /// <param name="spaced">
    /// Whether the space after the node is the node's. A diagram writing one node to a line takes it, so what follows on that line
    /// is read hard against it; one writing several takes only the space between an id and the brackets after it, so each node
    /// stands for its own characters and the space between two of them is the line's.
    /// </param>
    public static bool Node(MermaidLine line, string idRole, string titleRole, string stops, out bool titled,
                            IReadOnlyList<(string Open, string Close)>? brackets = null, bool spaced = true,
                            Func<string, int, int>? ends = null)
    {
        var written = brackets ?? Brackets;
        titled = Bracket(line, written) is not null;

        if (!titled)
        {
            line.Open();

            if (ends is null) line.Words(idRole, until: stops);
            else line.Words(idRole, ends(line.Written, line.At));

            line.Close(MermaidKinds.Name, idRole);

            var mark = line.Save();
            line.Space();
            if (!spaced && Bracket(line, written) is null) line.Restore(mark);
        }

        if (Bracket(line, written) is var (open, close))
        {
            if (!line.Label(open, close, titleRole)) return false;

            titled = true;
            if (spaced) line.Space();
        }

        return true;
    }

    /// <summary>
    /// A line decorating the node above it — <c>::icon(fa fa-book)</c>, <c>:::urgent</c> — as a <paramref name="kind"/>, or held
    /// with the reason where it says nothing an icon or a class would.
    /// </summary>
    /// <param name="close">What closes what it names, where anything does: <c>)</c> for an icon.</param>
    public static ContentNode Decoration(MermaidLine line, string mark, string? close, string role, string kind)
    {
        line.Token(mark, Roles.Open);
        // An icon's name is an icon, which is the stage's to say which (MermaidKinds.Icon).
        var icon = mark == IconMark;
        if (icon) line.Open();
        line.Words(role, until: close);
        if (icon) line.Close(MermaidKinds.Icon);
        line.Space();
        if (close is not null) line.Token(close, Roles.Close);

        return line.Done ? line.Read(kind) : line.Shown($"An icon is written {IconMark}name), and a class :::name.");
    }

    /// <summary>
    /// What writing <paramref name="text"/> into a node's id is written as: a bare id that is its own title becomes a title in
    /// quotes, so it can hold what an id cannot; an id beside a title cannot hold those characters at all, so they are dropped.
    /// Null where <paramref name="part"/> is no id, or the text goes in as it is.
    /// </summary>
    public static MermaidWriting? Escaping(ContentPart part, int caret, string text, string idRole, Func<char, bool> stops)
    {
        if (part.Parent is not { Kind: MermaidKinds.Name } id || id.Role != idRole || !text.Any(stops)) return null;

        if (id.Parent?.Children.Any(child => child.Kind == MermaidKinds.Label) == true)
        {
            var kept = new string([.. text.Where(character => !stops(character))]);
            return new MermaidWriting(caret, caret, kept, caret + kept.Length);
        }

        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var head = "[\"" + MermaidText.Quoted(said[..at] + text);
        return new MermaidWriting(part.Start, part.Start + said.Length, head + MermaidText.Quoted(said[at..]) + "\"]", part.Start + head.Length);
    }

    /// <summary>What a node's title opens with — <c>[</c>, <c>((</c> — or null where it has no title in brackets.</summary>
    public static string? Opening(ContentPart node) =>
        node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label)?.Children.FirstOrDefault(child => child.Role == Roles.Open)?.Text;

    /// <summary>
    /// The brackets a title opens with next, where one does. Where several closings may end the same opening — <c>[/…/]</c>
    /// and <c>[/…\]</c> — it is ended by whichever of them is written first, which is what says which shape it is.
    /// </summary>
    private static (string Open, string Close)? Bracket(MermaidLine line, IReadOnlyList<(string Open, string Close)> brackets)
    {
        (string Open, string Close)? found = null;

        foreach (var bracket in brackets)
        {
            if (!line.Sees(bracket.Open)) continue;
            if (found is null) { found = bracket; continue; }
            if (!string.Equals(bracket.Open, found.Value.Open, StringComparison.Ordinal)) break;

            if (Ends(line, bracket.Close) < Ends(line, found.Value.Close)) found = bracket;
        }

        return found;
    }

    /// <summary>How far along the line a closing is written, or the end of it where it is not written at all.</summary>
    private static int Ends(MermaidLine line, string close) =>
        line.Written.IndexOf(close, line.At, StringComparison.Ordinal) is var at && at < 0 ? int.MaxValue : at;

    /// <summary>
    /// What nests what, for a diagram written as an indented outline: each item with the item it hangs off, which is the nearest
    /// item before it indented less. The first item hangs off nothing, and is the outline's root.
    ///
    /// <para>
    /// The indentation only has to say which of the lines before it a line belongs under, so an outline nobody lined up neatly still
    /// nests: a line indented deeper than its uncle but shallower than its sibling hangs off the nearest line shallower than it,
    /// which is what Mermaid does. An item indented no further than the root hangs off nothing — null, for the diagram to refuse.
    /// </para>
    /// <para>
    /// Where <paramref name="floor"/> says so, the root's own indentation is nothing to go by — the first item after it is where the
    /// children start, and anything indented less than that is a child of the root rather than hanging off nothing. That is how a
    /// fishbone reads, since its event may be written further in than its causes.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(T Item, int? Parent)> Nested<T>(IReadOnlyList<(int Indent, T Item)> items, bool floor = false)
    {
        var nested = new List<(T, int?)>(items.Count);
        if (items.Count == 0) return nested;

        nested.Add((items[0].Item, null));
        if (items.Count == 1) return nested;

        var root = items[0].Indent;
        var start = items[1].Indent;

        for (var at = 1; at < items.Count; at++)
        {
            var indent = Deep(items[at].Indent);

            var parent = (int?)null;
            for (var over = at - 1; over > 0; over--)
                if (Deep(items[over].Indent) < indent)
                {
                    parent = over;
                    break;
                }

            nested.Add((items[at].Item, parent ?? (floor || indent > root ? 0 : null)));
        }

        return nested;

        int Deep(int indent) => floor ? Math.Max(indent, start) : indent;
    }
}
