using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// Reads the links a markdown document writes by name — <c>[text][name]</c>, <c>[name][]</c>, <c>[name]</c>, and a picture
/// written the same way — and the abbreviations it uses, by what the document defines (<see cref="LinkTargets"/>, told by
/// <see cref="WithTargets"/> before this runs).
///
/// <para>
/// A block's words are read on their own, and a definition is written somewhere else — so the reading of a block cannot tell
/// a link by name from a few words in brackets, and holds the brackets as the words it was written with. Which brackets make
/// a link is settled here, as CommonMark settles it: each <c>]</c> closes the nearest <c>[</c> still open; what follows says
/// which name it is — <c>[name]</c>, <c>[]</c> for its own words, or nothing for its own words where no name follows — and it
/// is a link only where the document defines that name. A link holds no other link, so one made shuts every <c>[</c> before
/// it. The brackets and the words between are gathered into the link — <see cref="Kinds.Link"/> or
/// <see cref="MarkdownKinds.Image"/> — and where it goes is hung on it.
/// </para>
/// <para>
/// An abbreviation is a whole word, or words, the document says it stands for something: starting where a line or a space does,
/// and ending where the words do, a space does or punctuation does. Each is made a piece of its own holding what it stands for.
/// </para>
/// </summary>
/// <param name="targets">What the document defines.</param>
public sealed class WithReferences(LinkTargets targets) : IAstStage
{
    /// <inheritdoc/>
    public string Name => "markdown:references";

    /// <inheritdoc/>
    public ContentNode Run(ContentNode tree)
    {
        var linked = targets.Every(MarkdownKinds.Reference).Any() ? Linked(tree) : tree;

        if (Abbreviations() is not { } abbreviations) return linked;

        var before = '\n';
        return Abbreviated(linked, abbreviations, ref before) is [var only] ? only : linked;
    }

    // ── Links ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A piece with every run of words in it read for links by name, the deepest first. What Markdig left of a bracket it could
    /// not close holds words that belong to the run around it, so they are read with that run, never as a run of their own.
    /// </summary>
    private ContentNode Linked(ContentNode node)
    {
        if (node.IsLeaf || node.IsDerived || Elsewhere(node)) return node;

        node = Each(node, child => Leftover(child) ? Each(child, Inside) : Linked(child));

        return node.Kind == Kinds.Words ? Gathered(node) : node;

        // The words a leftover bracket holds, each read as anything in a run is — and the run they make not read on its own.
        ContentNode Inside(ContentNode part) =>
            part is { Kind: Kinds.Words, Role: Roles.Body } ? Each(part, inner => Leftover(inner) ? Each(inner, Inside) : Linked(inner)) : Linked(part);
    }

    /// <summary>A piece with <paramref name="of"/> applied to each of its parts — itself, where none of them changed.</summary>
    private static ContentNode Each(ContentNode node, Func<ContentNode, ContentNode> of)
    {
        ContentNode[]? parts = null;

        for (var at = 0; at < node.Children.Count; at++)
        {
            var child = node.Children[at];
            var now = of(child);

            if (parts is null && !ReferenceEquals(now, child)) parts = [.. node.Children];
            if (parts is not null) parts[at] = now;
        }

        return parts is null ? node : node.With(parts);
    }

    /// <summary>
    /// Whether a piece is written in something whose characters hold no link by name: another language a fence or a formula
    /// holds, code, raw HTML, front matter.
    /// </summary>
    private static bool Elsewhere(ContentNode node) =>
        node is BlockNode { Language: not MarkdownParser.Language }
        || node.Kind is MarkdownKinds.Code or MarkdownKinds.Html or MarkdownKinds.FrontMatter;

    /// <summary>What a run of words is once every link it writes by a name the document defines is made one — itself where none is.</summary>
    private ContentNode Gathered(ContentNode words)
    {
        if (!Bracketed(words)) return words;

        var atoms = new List<Atom>(words.Children.Count);
        foreach (var child in words.Children) Atoms(child, atoms);

        var openers = new List<int>();
        var made = false;

        for (var at = 0; at < atoms.Count; at++)
        {
            if (atoms[at].Mark is Mark.Open or Mark.Picture)
            {
                openers.Add(at);
                continue;
            }

            if (atoms[at].Mark != Mark.Close || openers.Count == 0) continue;

            var opener = openers[^1];
            openers.RemoveAt(openers.Count - 1);

            if (!atoms[opener].Active || Link(atoms, opener, at) is not { } link) continue;

            atoms.RemoveRange(opener, link.Taken);
            atoms.Insert(opener, new Atom(link.Node, Mark.None, null));
            at = opener;
            made = true;

            // A link holds no link, so no bracket before this one can open one round it — a picture still can.
            if (link.Node.Kind == Kinds.Link)
                foreach (var before in openers)
                    if (atoms[before].Mark == Mark.Open) atoms[before] = atoms[before] with { Active = false };
        }

        return made ? words.With(Joined(atoms)) : words;
    }

    /// <summary>Whether a run of words holds a bracket closing something at all — most hold none, and are left as they are.</summary>
    private static bool Bracketed(ContentNode words)
    {
        foreach (var child in words.Children)
            if (child.Kind == MarkdownKinds.Word && (child.IsLeaf ? child.Text.Contains(']') : Leftover(child))) return true;

        return false;
    }

    /// <summary>
    /// Where a link opened at <paramref name="opener"/> and closed at <paramref name="closer"/> is one, the link — and how many
    /// atoms from the opener it takes in; null where it is no link.
    /// </summary>
    private (ContentNode Node, int Taken)? Link(List<Atom> atoms, int opener, int closer)
    {
        var text = atoms.GetRange(opener + 1, closer - opener - 1);
        var written = Printed(text);
        var name = written;
        var taken = closer - opener + 1;
        List<Atom>? named = null;

        // Followed by a name, it is a link by that name or by none: [words][name], or [words][] for its own words.
        if (Label(atoms, closer + 1) is { } label)
        {
            named = atoms.GetRange(closer + 1, label - closer);
            var between = Printed(named)[1..^1];

            if (between.Length > 0) name = between;
            taken += named.Count;
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 999) return null;
        if (targets.Named(MarkdownKinds.Reference, WithTargets.Label(name)) is not { } defined) return null;

        var picture = atoms[opener].Mark == Mark.Picture;
        var kind = picture ? MarkdownKinds.Image : Kinds.Link;
        var open = atoms[opener].Node;

        var words = Joined(text);
        var sized = picture ? MarkdownPictureSize.Taken(words) : null;

        List<ContentNode> parts = [ContentNode.Leaf(Kinds.Token, open.Text, Roles.Open, offset: open.Offset)];
        if (words.Count > 0) parts.Add(ContentNode.Branch(Kinds.Words, words, Roles.Body));
        if (sized is { } size) parts.Add(size.Written);

        parts.Add(ContentNode.Leaf(Kinds.Token, atoms[closer].Node.Text, Roles.Close, offset: atoms[closer].Node.Offset));

        if (named is not null)
            parts.Add(ContentNode.Leaf(Kinds.Token, Printed(named), Roles.Close, offset: named[0].Node.Offset));

        var link = ContentNode.Branch(kind, parts);
        if (sized is { } asked) link = link.Holding(MarkdownKinds.Size, MarkdownRoles.Size, asked.Size);

        return (link.Holding(kind, Roles.Destination, defined.Meaning), taken);
    }

    /// <summary>
    /// Where a name written straight after a link's words ends — the atom closing the <c>[name]</c> or <c>[]</c> opened at
    /// <paramref name="at"/> — or null where none is written there. A name holds no bracket.
    /// </summary>
    private static int? Label(List<Atom> atoms, int at)
    {
        if (at >= atoms.Count || atoms[at].Mark != Mark.Open) return null;

        for (var next = at + 1; next < atoms.Count; next++)
            switch (atoms[next].Mark)
            {
                case Mark.Close: return next;
                case Mark.None: continue;
                default: return null;
            }

        return null;
    }

    /// <summary>
    /// What a link's brackets and words are seen as: each piece a run of words holds, with every bracket in its words a piece of
    /// its own, and what Markdig left of a bracket it opened and could not close — a branch it calls a word — taken apart into
    /// the bracket and what followed it.
    /// </summary>
    private static void Atoms(ContentNode node, List<Atom> atoms)
    {
        if (Leftover(node))
        {
            atoms.Add(Bracket(node.Children[0].Text, node.Children[0].Offset, null));

            foreach (var child in node.Children.Skip(1))
                if (child is { Kind: Kinds.Words, Role: Roles.Body }) foreach (var inner in child.Children) Atoms(inner, atoms);
                else Atoms(child, atoms);

            return;
        }

        if (node is not { IsLeaf: true, Kind: MarkdownKinds.Word, Role: Roles.Element } || node.Offset is not { } stands
            || node.Text.AsSpan().IndexOfAny('[', ']') < 0)
        {
            atoms.Add(new Atom(node, Mark.None, null));
            return;
        }

        var text = node.Text;
        var taken = 0;

        for (var at = 0; at < text.Length; at++)
        {
            var width = text[at] is '[' or ']' ? 1 : text[at] == '!' && at + 1 < text.Length && text[at + 1] == '[' ? 2 : 0;
            if (width == 0) continue;

            if (at > taken) atoms.Add(new Atom(Word(text[taken..at], stands + taken), Mark.None, node));
            atoms.Add(Bracket(text.Substring(at, width), stands + at, node));

            taken = at + width;
            at = taken - 1;
        }

        if (taken < text.Length) atoms.Add(new Atom(Word(text[taken..], stands + taken), Mark.None, node));
    }

    /// <summary>
    /// Whether a piece is what Markdig leaves of a bracket it opened and could not close: a branch it calls a word, holding the
    /// bracket and the words after it.
    /// </summary>
    private static bool Leftover(ContentNode node) =>
        node is { IsLeaf: false, Kind: MarkdownKinds.Word } && node.Children.Count > 0
        && node.Children[0] is { IsLeaf: true, Role: Roles.Open, Text: "[" or "![" };

    /// <summary>A bracket, as the word it is until it is found to open or close a link.</summary>
    private static Atom Bracket(string text, int? at, ContentNode? origin) =>
        new(Word(text, at), text switch { "]" => Mark.Close, "[" => Mark.Open, _ => Mark.Picture }, origin);

    private static ContentNode Word(string text, int? at) => ContentNode.Leaf(MarkdownKinds.Word, text, Roles.Element, offset: at);

    /// <summary>The pieces of some atoms, where words cut apart for their brackets and taken into no link are joined up again.</summary>
    private static List<ContentNode> Joined(List<Atom> atoms)
    {
        var joined = new List<ContentNode>(atoms.Count);

        for (var at = 0; at < atoms.Count; at++)
        {
            var atom = atoms[at];

            if (atom.Origin is null || atom.Node.Offset is not { } from)
            {
                joined.Add(atom.Node);
                continue;
            }

            var text = atom.Node.Text;
            while (at + 1 < atoms.Count && ReferenceEquals(atoms[at + 1].Origin, atom.Origin)) text += atoms[++at].Node.Text;

            joined.Add(text.Length == atom.Origin.Text.Length ? atom.Origin : Word(text, from));
        }

        return joined;
    }

    private static string Printed(List<Atom> atoms) => string.Concat(atoms.Select(atom => atom.Node.Print()));
    /// <summary>What a piece is seen as, finding links: a bracket, or anything else.</summary>
    private enum Mark { None, Open, Picture, Close }

    /// <summary>One piece a run of words is seen as, finding links — and the word it was cut from, where it was.</summary>
    private sealed record Atom(ContentNode Node, Mark Mark, ContentNode? Origin)
    {
        /// <summary>Whether a bracket can still open a link: not once a link has been made after it.</summary>
        public bool Active { get; init; } = true;
    }

    // ── Abbreviations ────────────────────────────────────────────────────────

    /// <summary>Every abbreviation the document defines, by its first character, longest first — the first definition of each.</summary>
    private Dictionary<char, List<(string Name, string Meaning)>>? Abbreviations()
    {
        Dictionary<char, List<(string Name, string Meaning)>>? by = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var target in targets.Every(MarkdownKinds.Abbreviation))
        {
            if (target.Name.Length == 0 || !seen.Add(target.Name)) continue;

            by ??= [];
            if (!by.TryGetValue(target.Name[0], out var alike)) by[target.Name[0]] = alike = [];
            alike.Add((target.Name, target.Meaning));
        }

        if (by is null) return null;

        foreach (var alike in by.Values) alike.Sort((one, other) => other.Name.Length.CompareTo(one.Name.Length));

        return by;
    }

    /// <summary>
    /// A piece with every abbreviation in its words made a piece of its own: the piece itself where none is, or what stands in
    /// its place. <paramref name="before"/> is the character printed before it, and comes back as the last it prints.
    /// </summary>
    private static IReadOnlyList<ContentNode> Abbreviated(ContentNode node, Dictionary<char, List<(string Name, string Meaning)>> abbreviations,
                                                          ref char before)
    {
        if (node.IsDerived) return [node];

        if (node.IsLeaf)
        {
            var said = node is { Kind: MarkdownKinds.Word, Role: Roles.Element, Offset: not null } ? Spelled(node, abbreviations, before) : null;
            if (node.Text.Length > 0) before = node.Text[^1];

            return said ?? [node];
        }

        // Nothing written in code, raw HTML or another language is an abbreviation.
        if (node.Kind is MarkdownKinds.Code or MarkdownKinds.Html or MarkdownKinds.Abbreviation || node is BlockNode { Language: not MarkdownParser.Language })
        {
            if (Last(node) is { } last) before = last;
            return [node];
        }

        List<ContentNode>? parts = null;

        for (var at = 0; at < node.Children.Count; at++)
        {
            var child = node.Children[at];
            var now = Abbreviated(child, abbreviations, ref before);

            if (parts is null && (now.Count != 1 || !ReferenceEquals(now[0], child))) parts = [.. node.Children.Take(at)];
            parts?.AddRange(now);
        }

        return parts is null ? [node] : [node.With(parts)];
    }

    /// <summary>A word with every abbreviation in it a piece of its own, or null where it holds none.</summary>
    private static List<ContentNode>? Spelled(ContentNode word, Dictionary<char, List<(string Name, string Meaning)>> abbreviations, char before)
    {
        var text = word.Text;
        var stands = word.Offset!.Value;
        List<ContentNode>? parts = null;
        var taken = 0;

        for (var at = 0; at < text.Length; at++)
        {
            var starts = at == 0 ? char.IsWhiteSpace(before) : char.IsWhiteSpace(text[at - 1]);
            if (!starts || !abbreviations.TryGetValue(text[at], out var alike)) continue;

            foreach (var (name, meaning) in alike)
            {
                var end = at + name.Length;
                if (end > text.Length || string.CompareOrdinal(text, at, name, 0, name.Length) != 0) continue;
                if (end < text.Length && !char.IsWhiteSpace(text[end]) && !Punctuation(text[end])) continue;

                parts ??= [];
                if (at > taken) parts.Add(Word(text[taken..at], stands + taken));

                parts.Add(ContentNode.Branch(MarkdownKinds.Abbreviation, [ContentNode.Leaf(MarkdownKinds.Word, name, Roles.Body, offset: stands + at)])
                                     .Holding(MarkdownKinds.Abbreviation, Roles.Tip, meaning));

                taken = end;
                at = end - 1;
                break;
            }
        }

        if (parts is null) return null;

        if (taken < text.Length) parts.Add(Word(text[taken..], stands + taken));

        return parts;
    }

    /// <summary>The last character a piece prints, or null where it prints none.</summary>
    private static char? Last(ContentNode node)
    {
        for (var at = node.Children.Count - 1; at >= 0; at--)
            if (!node.Children[at].IsDerived && Last(node.Children[at]) is { } last) return last;

        return node.Text.Length > 0 ? node.Text[^1] : null;
    }

    /// <summary>Whether a character is ASCII punctuation, which may end an abbreviation as a space may.</summary>
    private static bool Punctuation(char character) =>
        character is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~');
}
