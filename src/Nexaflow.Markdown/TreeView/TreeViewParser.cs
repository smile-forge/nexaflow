using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.TreeView;

/// <summary>
/// Reads a tree view: nodes nested by how far each line is indented — or by where the branch of a box drawing in front of it
/// stands — each a label, bare or in quotes, a slash after it making it a folder, and after it the class it takes
/// (<c>:::highlight</c>), the icon it is drawn with (<c>icon(name)</c>) and what is said beside it (<c>## text</c>), in any order.
///
/// <para>
/// A tree view is a language of its own: a <c>treeview</c> fence names it, and a <c>mermaid</c> block whose header is
/// <c>treeView-beta</c> is handed to it whole (<see cref="MermaidFenceParser"/>). What every block written Mermaid's way holds round
/// what it says — front matter, <c>%%</c> comment lines, directives, <c>title</c>, <c>accTitle</c> and <c>accDescr</c> — is read
/// with Mermaid's own pieces (<see cref="MermaidParser"/>). The header is optional here, since the fence already says what the
/// block is.
/// </para>
/// <para>
/// The lines are read as Mermaid's own grammar reads them. A bare label runs to the first annotation — space and then <c>:::</c>
/// and a class's name, <c>icon(</c> or <c>##</c> — or the end of the line, and holds anything else, a pair of percents included; a
/// label in quotes holds anything but its quote. The box drawing is held as written, a piece of its own in front of the label, and a
/// line of nothing but its bars stands for no node. How deep a node stands, and so what it is under, is the whole block's to say,
/// and its stages say it (<see cref="Stages.ResolveNodes"/>).
/// </para>
/// </summary>
public sealed class TreeViewParser : ITranspile
{
    private TreeViewParser()
    {
    }

    /// <summary>The language a tree view is written in: what a fence names it by, and what a <c>mermaid</c> block headed by it is held as.</summary>
    public const string Language = "treeview";

    /// <summary>The words a tree view's header is written with.</summary>
    public static readonly string[] Keywords = ["treeView-beta", "treeView"];

    /// <summary>What stands before the class a node takes.</summary>
    public const string ClassMark = ":::";

    /// <summary>What opens the name of the icon a node is drawn with, and what closes it.</summary>
    public const string IconMark = "icon(";
    public const string IconClose = ")";

    /// <summary>What stands before what is said beside a node.</summary>
    public const string DescriptionMark = "##";

    /// <summary>What after a label makes the node a folder.</summary>
    public const string Slash = "/";

    /// <summary>The class Mermaid draws a node's row picked out for.</summary>
    public const string Highlight = "highlight";

    /// <summary>The quote a label is put in where it has to be.</summary>
    public const char Opening = '"';

    /// <summary>What a box drawing branches off to a node with — a branch with others after it, and the last.</summary>
    public const string Branches = "├└┣┗";

    /// <summary>The bars a box drawing carries down past the nodes of a folder whose others are still to come.</summary>
    public const string Bars = "│┃";

    /// <summary>The dashes along a branch.</summary>
    public const string Dashes = "─━";

    private const string Comment = "%%";

    private const string ItemShape = "A node is its name — src/, index.js or \"my file\" — then, in any order, :::class, icon(name) and ## a description.";
    private const string HeaderShape = "A tree view's header is treeView-beta, with nothing after it: its title is a line of its own.";
    private const string IconShape = "An icon's name is closed with a bracket: icon(folder).";
    private const string NeverClosed = "This label is never closed with the quote it opens with.";

    /// <summary>Reads a block into its lines, every character kept.</summary>
    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;

        var lines = new List<ContentNode>();
        var at = 0;
        var headed = false;

        if (MermaidParser.Fences(source) is (var open, var close))
        {
            for (; at < open.Start; at = Next(source, at, ref headed, lines)) { }

            lines.Add(MermaidParser.FrontMatter(source, open, close));
            at = close.Stop;
        }

        while (at < source.Length) at = Next(source, at, ref headed, lines);

        return new BlockNode(Language, lines, MermaidKinds.Block);
    }

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: in a label and in what is said beside a node, where nothing is written yet.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind is TreeViewKinds.Label or TreeViewKinds.Description;

    // ── Lines ───────────────────────────────────────────────────────────────

    private static int Next(string source, int at, ref bool headed, List<ContentNode> lines)
    {
        var row = MermaidParser.Row.At(source, at);
        var (from, to) = row.Text(source);
        var text = source[from..to];

        if (text.Length == 0)
        {
            lines.Add(MermaidParser.Line(source, row.Start, from, [], to, row));
            return row.Stop;
        }

        if (text.StartsWith("%%{", StringComparison.Ordinal)) return MermaidParser.Directive(source, row, from, to, lines);

        if (text.StartsWith(Comment, StringComparison.Ordinal))
        {
            lines.Add(MermaidParser.Line(source, row.Start, from, [ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia, offset: from)], to, row));
            return row.Stop;
        }

        if (!headed)
        {
            headed = true;
            if (Heading(text, from) is { } header)
            {
                lines.Add(MermaidParser.Line(source, row.Start, from, [header], to, row));
                return row.Stop;
            }
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;

        var said = Titled(text, from) ?? Item(text, from);
        lines.Add(MermaidParser.Line(source, row.Start, from, [said], from + said.Width, row));
        return row.Stop;
    }

    /// <summary>The header — <c>treeView-beta</c> — or null where the line says something else, which a tree view fence needs no header before.</summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length)
        {
            var said = rest[gap..];
            pieces.Add(said.StartsWith(Comment, StringComparison.Ordinal) ? ContentNode.Leaf(Kinds.Comment, said, Roles.Trivia, offset: after)
                       : ContentNode.Shown(said, HeaderShape, offset: after));
        }

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    // ── Statements ──────────────────────────────────────────────────────────

    /// <summary>
    /// A title: its word and what it says to the end of the line — the word standing alone or with space after it, as Mermaid reads
    /// one, so a file called <c>title.txt</c> is a node. Null where the line is no title.
    /// </summary>
    private static ContentNode? Titled(string text, int at)
    {
        if (!text.StartsWith(MermaidLine.TitleWord, StringComparison.Ordinal)
            || (text.Length > MermaidLine.TitleWord.Length && text[MermaidLine.TitleWord.Length] is not (' ' or '\t'))) return null;

        var line = MermaidLine.Of(text, at, comments: false);
        line.Word(MermaidLine.TitleWord);
        line.Space();
        if (!line.Done) line.Words(MermaidRoles.Title);

        return line.Read(MermaidKinds.Title);
    }

    /// <summary>
    /// A node: the box drawing in front of it where there is some, its label, and what is noted after it — or a line of nothing but a
    /// box drawing's bars. Anything that is not the shape of one is held as written, with the shape it should have.
    /// </summary>
    private static ContentNode Item(string text, int at)
    {
        var line = MermaidLine.Of(text, at, comments: false);

        if (Drawn(line.Rest) is > 0 and var drawn)
        {
            if (line.Rest.All(character => Bars.Contains(character) || character is ' ' or '\t'))
                return ContentNode.Leaf(TreeViewKinds.Decoration, line.Rest, Roles.Trivia, offset: line.Begins);

            line.Add(ContentNode.Leaf(TreeViewKinds.Prefix, line.Rest[..drawn], Roles.Trivia, offset: line.Begins));
            line.Space();
        }

        if (!Labelled(line, out var quoted)) return line.Shown(ItemShape);

        while (!line.Done)
        {
            line.Space();

            if (line.Sees(ClassMark)) Classed(line);
            else if (line.Sees(IconMark))
            {
                if (!Iconed(line)) return line.Shown(IconShape);
            }
            else if (line.Sees(DescriptionMark)) Described(line);
            else if (quoted && line.Sees(Comment)) line.Add(ContentNode.Leaf(Kinds.Comment, line.Rest, Roles.Trivia, offset: line.Begins));
            else if (!line.Done) return line.Shown(ItemShape);
        }

        return line.Read(TreeViewKinds.Item);
    }

    /// <summary>How long the box drawing at the start of <paramref name="text"/> is, to its last box character — nought where there is none.</summary>
    private static int Drawn(string text)
    {
        var drawn = 0;

        for (var at = 0; at < text.Length; at++)
        {
            var character = text[at];
            if (Boxed(character)) drawn = at + 1;
            else if (character is not (' ' or '\t')) break;
        }

        return drawn;
    }

    /// <summary>Whether <paramref name="character"/> is one a box drawing is made of.</summary>
    public static bool Boxed(char character) => Branches.Contains(character) || Bars.Contains(character) || Dashes.Contains(character);

    /// <summary>
    /// A label: in quotes, to the same quote — or bare, to the first annotation or the end of the line — and the slash after its words
    /// that makes the node a folder; or, after a box drawing with nothing after it yet, a label still to write. False where nothing
    /// that is a label is written, with the reason where a quote is never closed.
    /// </summary>
    private static bool Labelled(MermaidLine line, out bool quoted)
    {
        quoted = false;
        var written = line.Rest;
        var at = line.Begins;

        if (written.Length == 0)
        {
            line.Add(ContentNode.Branch(TreeViewKinds.Label, [ContentNode.Leaf(Kinds.Words, string.Empty, TreeViewRoles.Label, offset: at)], offset: at));
            return true;
        }

        if (written[0] is '"' or '\'')
        {
            var close = written.IndexOf(written[0], 1);
            if (close < 0) return line.Fail(NeverClosed);

            quoted = true;
            var said = written[1..close];
            var folder = said.EndsWith(Slash, StringComparison.Ordinal);
            var words = folder ? said[..^1] : said;

            line.Open();
            line.Add(ContentNode.Leaf(Kinds.Token, written[..1], Roles.Open, offset: at));
            line.Add(ContentNode.Leaf(Kinds.Words, words, TreeViewRoles.Label, offset: at + 1));
            if (folder) line.Add(ContentNode.Leaf(Kinds.Token, Slash, TreeViewRoles.Folder, offset: at + 1 + words.Length));
            line.Add(ContentNode.Leaf(Kinds.Token, written[..1], Roles.Close, offset: at + close));
            line.Close(TreeViewKinds.Label);
            return true;
        }

        var bare = written[..BareEnd(written)].TrimEnd();
        if (bare.Length == 0) return false;

        var slashed = bare.EndsWith(Slash, StringComparison.Ordinal);
        var named = slashed ? bare[..^1] : bare;

        line.Open();
        line.Add(ContentNode.Leaf(Kinds.Words, named, TreeViewRoles.Label, offset: at));
        if (slashed) line.Add(ContentNode.Leaf(Kinds.Token, Slash, TreeViewRoles.Folder, offset: at + named.Length));
        line.Close(TreeViewKinds.Label);
        return true;
    }

    /// <summary>Where a bare label written at the start of <paramref name="text"/> ends: at the space before the first annotation, or the end.</summary>
    private static int BareEnd(string text)
    {
        if (Annotation(text, 0)) return 0;

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] is not (' ' or '\t')) continue;

            var past = at;
            while (past < text.Length && text[past] is ' ' or '\t') past++;
            if (Annotation(text, past)) return at;

            at = past - 1;
        }

        return text.Length;
    }

    /// <summary>Whether an annotation starts at <paramref name="at"/>: <c>icon(</c>, <c>##</c>, or <c>:::</c> and the start of a class's name.</summary>
    private static bool Annotation(string text, int at)
    {
        var rest = text.AsSpan(at);
        if (rest.StartsWith(IconMark, StringComparison.Ordinal) || rest.StartsWith(DescriptionMark, StringComparison.Ordinal)) return true;
        if (!rest.StartsWith(ClassMark, StringComparison.Ordinal)) return false;

        var name = rest[ClassMark.Length..].TrimStart(" \t");
        return name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_');
    }

    /// <summary>The class a node takes: <c>:::</c> and its name.</summary>
    private static void Classed(MermaidLine line)
    {
        line.Open();
        line.Token(ClassMark);
        line.Space();

        var rest = line.Rest;
        var length = 0;
        if (rest.Length > 0 && (char.IsAsciiLetter(rest[0]) || rest[0] == '_'))
            while (length < rest.Length && (char.IsAsciiLetterOrDigit(rest[length]) || rest[length] is '_' or '-')) length++;

        if (length > 0) line.Add(ContentNode.Leaf(Kinds.Words, rest[..length], TreeViewRoles.Class, offset: line.Begins));
        line.Close(TreeViewKinds.Class);
    }

    /// <summary>The icon a node is drawn with: <c>icon(</c>, its name — none, where it is to be drawn with none — and the bracket closing it.</summary>
    private static bool Iconed(MermaidLine line)
    {
        var close = line.Rest.IndexOf(IconClose, StringComparison.Ordinal);
        if (close < 0) return line.Fail(IconShape);

        line.Open();
        line.Token(IconMark, Roles.Open);
        line.Add(ContentNode.Leaf(Kinds.Words, line.Rest[..(close - IconMark.Length)], TreeViewRoles.Icon, offset: line.Begins));
        line.Token(IconClose, Roles.Close);
        line.Close(MermaidKinds.Icon);
        return true;
    }

    /// <summary>What is said beside a node: <c>##</c> and the rest of the line.</summary>
    private static void Described(MermaidLine line)
    {
        line.Open();
        line.Token(DescriptionMark);
        line.Space();
        line.Words(TreeViewRoles.Description);
        line.Close(TreeViewKinds.Description);
    }

    // ── Writing back ────────────────────────────────────────────────────────

    /// <inheritdoc cref="ITranspile.Rewrite"/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>
    /// A change as a tree view spells it: a whole label from what it says — bare where it can be, in quotes where it cannot — keeping
    /// whether it is a folder; words into a label in quotes, without its quote; what is said beside a node on one line; anywhere else,
    /// as every block written Mermaid's way spells it.
    /// </summary>
    private static string? Spelled(ContentPart part, string text)
    {
        if (part.Kind == TreeViewKinds.Label) return Labelled(text, Quote(part), Folder(part));
        if (LabelOf(part) is { } label) return Quote(label) is { } quote ? Unquoted(text, quote) : OneLine(text);
        if (part.Role == TreeViewRoles.Description) return OneLine(text);

        return MermaidParser.Spelled(part, text);
    }

    /// <summary>
    /// A label saying <paramref name="words"/>: bare where nothing in them would read as something else, and otherwise in quotes —
    /// <paramref name="quote"/> where it holds none of those, else whichever quote it holds none of, the quote it was in being left out
    /// of it where it holds both — and a slash after it where it is a folder.
    /// </summary>
    public static string Labelled(string words, char? quote, bool folder)
    {
        var said = OneLine(words).Trim();
        var slash = folder ? Slash : string.Empty;

        if (Bare(said)) return said + slash;

        char wrap = quote is { } given && !said.Contains(given) ? given
                    : !said.Contains(Opening) ? Opening
                    : !said.Contains('\'') ? '\''
                    : quote ?? Opening;

        return wrap + Unquoted(said, wrap) + slash + wrap;
    }

    /// <summary>
    /// Whether <paramref name="words"/> can be a label written bare: something written, starting with no quote, space, comment or box
    /// drawing, and holding nothing that would read as an annotation.
    /// </summary>
    public static bool Bare(string words) =>
        words.Length > 0
        && words[0] is not ('"' or '\'') && !char.IsWhiteSpace(words[0]) && !Boxed(words[0])
        && !words.StartsWith(Comment, StringComparison.Ordinal)
        && !words.Contains('\n') && !words.Contains('\r')
        && BareEnd(words) == words.Length;

    /// <summary>The label <paramref name="part"/> is in — its words, the slash after them, or the hole in them — or null.</summary>
    public static ContentPart? LabelOf(ContentPart? part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == TreeViewKinds.Label) return at;

        return null;
    }

    /// <summary>The quote <paramref name="label"/> is written in, or null where it is bare.</summary>
    public static char? Quote(ContentPart label) => label.Children.FirstOrDefault(child => child.Role == Roles.Open)?.Text[0];

    /// <summary>Whether <paramref name="label"/> makes its node a folder: written with a slash after its words.</summary>
    public static bool Folder(ContentPart label) => label.Children.Any(child => child.Role == TreeViewRoles.Folder);

    /// <summary><paramref name="text"/> on one line, without <paramref name="quote"/>.</summary>
    public static string Unquoted(string text, char quote) => new([.. OneLine(text).Where(character => character != quote)]);

    /// <summary><paramref name="text"/> on one line: a line ending in it a space.</summary>
    private static string OneLine(string text) => text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
}
