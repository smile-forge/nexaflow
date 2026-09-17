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
    public static bool Node(MermaidLine line, string idRole, string titleRole, string stops, out bool titled)
    {
        titled = Bracket(line) is not null;

        if (!titled)
        {
            line.Open();
            line.Words(idRole, until: stops);
            line.Close(MermaidKinds.Name, idRole);
            line.Space();
        }

        if (Bracket(line) is var (open, close))
        {
            if (!line.Label(open, close, titleRole)) return false;

            titled = true;
            line.Space();
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
        line.Words(role, until: close);
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

    /// <summary>The brackets a title opens with next, where one does.</summary>
    private static (string Open, string Close)? Bracket(MermaidLine line)
    {
        foreach (var bracket in Brackets)
            if (line.Sees(bracket.Open))
                return bracket;

        return null;
    }
}
