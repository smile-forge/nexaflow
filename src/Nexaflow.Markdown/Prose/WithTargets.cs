using System.Text;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// Finds what a markdown document's links can name, and says so (<see cref="LinkTargets"/>): every link definition and
/// abbreviation written in it, and every heading, by the name a link calls it.
///
/// <para>
/// <strong>A definition is put back where it was written.</strong> A definition is read for what it defines and not drawn, so
/// the parse holds what it was written as among the trivia between blocks (<see cref="MarkdownParser"/>). Each is read out of
/// that trivia here into a piece of its own — trivia still, which nothing draws — holding what it says.
/// </para>
/// <para>
/// <strong>A heading is named here</strong>, GitHub's way — its words lower-cased, spaces made hyphens and punctuation dropped,
/// and a name already taken numbered <c>-1</c>, <c>-2</c>… in the order the headings are written — and the name hung on it
/// (<see cref="MarkdownKinds.Anchor"/>). What a heading says is only words until something decides it is a name, and two
/// headings saying the same are told apart only by the order they stand in, so it is the whole document's working over that
/// names them. Which is why a document is worked over from its top, always: every heading is numbered as it is in the whole.
/// </para>
/// </summary>
/// <param name="targets">Where what the document defines is said, for the stages after this one and whatever shows it.</param>
public sealed class WithTargets(LinkTargets targets) : IAstStage
{
    /// <inheritdoc/>
    public string Name => "markdown:targets";

    /// <inheritdoc/>
    public ContentNode Run(ContentNode tree)
    {
        var found = new List<LinkTarget>();
        var worked = Worked(tree, found, new HashSet<string>(StringComparer.Ordinal));

        targets.Report(Reached(tree), found);

        return worked;
    }

    /// <summary>
    /// What a <c>[text][name]</c> link's name is compared by: its words with every run of space made one space, case-folded — as
    /// CommonMark matches a link to its definition (<see cref="MarkdownLabels"/>).
    /// </summary>
    public static string Label(string name) => MarkdownLabels.Folded(name);

    /// <summary>How far into the document a tree reaches: to its end, or to where what is not read yet starts.</summary>
    private static int Reached(ContentNode tree)
    {
        foreach (var part in tree.Children)
            if (part.Kind == Kinds.Unreached && part.Offset is { } unread) return unread;

        return (tree.Offset ?? 0) + tree.Width;
    }

    /// <summary>A piece with every definition among its parts read out of the trivia they were held as, and every heading in it named.</summary>
    private static ContentNode Worked(ContentNode node, List<LinkTarget> found, HashSet<string> names)
    {
        if (node.IsLeaf || node.IsDerived || Elsewhere(node)) return node;

        if (node.Kind == MarkdownKinds.Heading) return Named(node, found, names);

        List<ContentNode>? parts = null;

        for (var at = 0; at < node.Children.Count; at++)
        {
            var child = node.Children[at];

            IReadOnlyList<ContentNode>? now = child is { IsLeaf: true, Role: Roles.Trivia } && child.Kind != Kinds.Unreached
                ? Defined(child, found)
                : Worked(child, found, names) is var worked && !ReferenceEquals(worked, child) ? [worked] : null;

            if (now is null)
            {
                parts?.Add(child);
                continue;
            }

            parts ??= [.. node.Children.Take(at)];
            parts.AddRange(now);
        }

        return parts is null ? node : node.With(parts);
    }

    /// <summary>
    /// Whether a piece is written in something whose characters say nothing markdown defines: another language a fence or a
    /// formula holds, code, raw HTML, front matter.
    /// </summary>
    private static bool Elsewhere(ContentNode node) =>
        node is BlockNode { Language: not MarkdownParser.Language }
        || node.Kind is MarkdownKinds.Code or MarkdownKinds.Html or MarkdownKinds.FrontMatter;

    // ── Headings ─────────────────────────────────────────────────────────────

    /// <summary>A heading with its name hung on it — the first of its words' names not taken by a heading above it.</summary>
    private static ContentNode Named(ContentNode heading, List<LinkTarget> found, HashSet<string> names)
    {
        var said = new StringBuilder();
        Plain(heading, said);

        var called = Anchored(said.ToString());
        if (called.Length == 0) called = "section";

        var name = called;
        for (var count = 1; !names.Add(name); count++) name = $"{called}-{count}";

        found.Add(new LinkTarget(MarkdownKinds.Anchor, name, name, heading.Offset ?? 0));

        return heading.Holding(MarkdownKinds.Anchor, Roles.Derived, name);
    }

    /// <summary>
    /// What a heading saying <paramref name="said"/> is called, GitHub's way: its letters and digits lower-cased, its hyphens and
    /// underscores kept, each space a hyphen, and everything else dropped.
    /// </summary>
    private static string Anchored(string said)
    {
        var called = new StringBuilder(said.Length);

        foreach (var character in said)
            if (char.IsLetterOrDigit(character) || character is '-' or '_') called.Append(char.ToLowerInvariant(character));
            else if (character == ' ') called.Append('-');

        return called.ToString();
    }

    /// <summary>
    /// What a heading says, as plain text: its words, what its escapes and entities stand for, the words of its links and the
    /// insides of its code — none of the marks that spell any of them.
    /// </summary>
    private static void Plain(ContentNode node, StringBuilder said)
    {
        if (node.IsDerived) return;

        switch (node.Kind)
        {
            case MarkdownKinds.Word when node.IsLeaf && node.Role is Roles.Element or Roles.Body:
                said.Append(node.Text);
                return;

            case MarkdownKinds.Escape when node.IsLeaf:
                said.Append(node.Text.AsSpan(1));
                return;

            case MarkdownKinds.Entity when node.IsLeaf:
                said.Append(MarkdownEntities.Decoded(node.Text) ?? node.Text);
                return;

            case MarkdownKinds.Break:
                said.Append('\n');
                return;

            // What a code span holds, without the marks of the containers its lines are written in.
            case MarkdownKinds.Code:
                if (node.Part(Roles.Body) is { } code)
                    said.Append(Spanned(code.IsLeaf ? code.Text : string.Concat(code.Children.Where(part => part.Role != Roles.Trivia).Select(part => part.Text))));
                return;

            case MarkdownKinds.Html:
                return;

            // A bare address says itself.
            case Kinds.Link when node.Part(Roles.Body) is null:
                if (node.Part(Roles.Destination) is { IsLeaf: true } address) said.Append(address.Text);
                return;
        }

        foreach (var child in node.Children)
            if (child.Role is not (Roles.Open or Roles.Close or Roles.Trivia or Roles.Destination or MarkdownRoles.Title))
                Plain(child, said);
    }

    /// <summary>What a code span holds: one space taken off each end where both have one and it is not all space, every line ending a space.</summary>
    private static string Spanned(string code)
    {
        var flat = code.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

        return flat.Length >= 2 && flat[0] == ' ' && flat[^1] == ' ' && !string.IsNullOrWhiteSpace(flat) ? flat[1..^1] : flat;
    }

    // ── Definitions ──────────────────────────────────────────────────────────

    /// <summary>
    /// The trivia <paramref name="trivia"/> held, with every definition in it a piece of its own — or null where it holds none.
    ///
    /// <para>
    /// Nothing but a definition is ever trivia beside the marks of the blocks holding it — a quote's <c>&gt;</c>, an item's
    /// bullet, the space between two blocks — so whatever reads as a definition where a line starts in it is one.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ContentNode>? Defined(ContentNode trivia, List<LinkTarget> found)
    {
        var text = trivia.Text;
        var stands = trivia.Offset ?? 0;
        List<ContentNode>? parts = null;
        var taken = 0;

        for (var line = 0; line < text.Length;)
        {
            var start = Opening(text, line);

            if (Definition(text, start) is not { } definition)
            {
                var next = text.IndexOf('\n', line);
                line = next < 0 ? text.Length : next + 1;
                continue;
            }

            parts ??= [];
            if (start > taken) parts.Add(Gap(trivia, text[taken..start], stands + taken));

            var written = text[start..definition.End];
            parts.Add(ContentNode.Branch(MarkdownKinds.Reference, [ContentNode.Leaf(Kinds.Verbatim, written, Roles.Body, offset: stands + start)],
                                         Roles.Trivia, stands + start)
                                 .Holding(definition.Kind, definition.Role, definition.Meaning));

            found.Add(new LinkTarget(definition.Kind, definition.Name, definition.Meaning, stands + start));

            line = taken = definition.End;
        }

        if (parts is null) return null;

        if (taken < text.Length) parts.Add(Gap(trivia, text[taken..], stands + taken));

        return parts;
    }

    /// <summary>What a definition says: what it defines, by which name, and what that means.</summary>
    /// <param name="Kind">A link's definition, or an abbreviation's.</param>
    /// <param name="Role">What its meaning is hung on it as.</param>
    /// <param name="End">Where it ends: past the line it ends on.</param>
    private readonly record struct Said(string Kind, string Role, string Name, string Meaning, int End);

    /// <summary>The definition written from <paramref name="at"/> on, or null where none is.</summary>
    private static Said? Definition(string text, int at)
    {
        if (at >= text.Length || text[at] is not ('[' or '*')) return null;

        // An abbreviation: *[HTML]: HyperText Markup Language — to the end of its line.
        if (text[at] == '*')
        {
            if (at + 1 >= text.Length || text[at + 1] != '[') return null;

            var shut = text.IndexOf(']', at + 2);
            var line = LineEnd(text, at);
            if (shut < 0 || shut + 1 >= line || text[shut + 1] != ':') return null;

            var name = text[(at + 2)..shut].Trim();
            if (name.Length == 0) return null;

            return new Said(MarkdownKinds.Abbreviation, Roles.Tip, name, text[(shut + 2)..line].Trim(), Ended(text, line));
        }

        // A link's — [name]: where "title" — read as the scan read it, over the lines it is written on: each from past the
        // marks of the blocks holding it. It goes where its address says, escapes and entities read.
        var lines = Lines(text, at);
        if (LinkDefinitions.Written(text, lines, 0) is not { } link) return null;

        var label = Label(MarkdownLinkSyntax.Between(text, lines, link.Label.From, link.Label.To));
        var meaning = MarkdownLinkSyntax.Unescaped(text[link.Destination.From..link.Destination.To]);

        return new Said(MarkdownKinds.Reference, Roles.Destination, label, meaning, Ended(text, lines[link.Last].To));
    }

    /// <summary>
    /// The lines from <paramref name="at"/> on, each from where what it says starts: the first from <paramref name="at"/>, each
    /// after it past its indentation and the quote marks a quote puts in front of it.
    /// </summary>
    private static List<(int From, int To)> Lines(string text, int at)
    {
        var lines = new List<(int From, int To)>();

        for (var start = at; ;)
        {
            var end = LineEnd(text, start);
            lines.Add((start, end));

            var next = Ended(text, end);
            if (next == end || next >= text.Length) return lines;

            start = next;
            while (start < text.Length && text[start] is ' ' or '\t' or '>') start++;
        }
    }

    /// <summary>
    /// Where what a line says starts: past its indentation and the marks of the blocks holding it — a quote's <c>&gt;</c>, a
    /// list item's bullet or number.
    /// </summary>
    private static int Opening(string text, int line)
    {
        var at = line;

        while (at < text.Length)
        {
            if (text[at] is ' ' or '\t' or '>') { at++; continue; }

            if (text[at] is '-' or '+' or '*' && at + 1 < text.Length && text[at + 1] is ' ' or '\t') { at += 2; continue; }

            var digits = at;
            while (digits < text.Length && char.IsAsciiDigit(text[digits])) digits++;

            if (digits > at && digits + 1 < text.Length && text[digits] is '.' or ')' && text[digits + 1] is ' ' or '\t')
            {
                at = digits + 2;
                continue;
            }

            break;
        }

        return at;
    }

    /// <summary>Where the line <paramref name="at"/> stands on ends, before its line break.</summary>
    private static int LineEnd(string text, int at)
    {
        var end = text.IndexOf('\n', at);
        if (end < 0) return text.Length;

        return end > at && text[end - 1] == '\r' ? end - 1 : end;
    }

    /// <summary>Past the line break at <paramref name="at"/>, where there is one.</summary>
    private static int Ended(string text, int at)
    {
        if (at < text.Length && text[at] == '\r') at++;
        if (at < text.Length && text[at] == '\n') at++;

        return at;
    }

    /// <summary>What falls between two definitions, held as the trivia it is.</summary>
    private static ContentNode Gap(ContentNode trivia, string text, int at) =>
        ContentNode.Leaf(text.AsSpan().IsWhiteSpace() ? Kinds.Space : trivia.Kind, text, Roles.Trivia, offset: at);
}
