using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Treemap;

/// <summary>
/// Reads a treemap: items nested by how far each line is indented, each a label in quotes — a section, which holds the items
/// indented under it, or a leaf, with a value after a colon or a comma — and the class it takes after <c>:::</c>.
///
/// <para>
/// A treemap is a language of its own: a <c>treemap</c> fence names it, and a <c>mermaid</c> block whose header is
/// <c>treemap-beta</c> or <c>treemap</c> is handed to it whole (<see cref="MermaidFenceParser"/>). What every block written
/// Mermaid's way holds round what it says — front matter, <c>%%</c> comments, directives, <c>title</c>, <c>accTitle</c> and
/// <c>accDescr</c>, the space between lines — is read with Mermaid's own pieces (<see cref="MermaidParser"/>), so the block's title
/// and front matter are found the same way. The header is optional here, since the fence already says what the block is.
/// </para>
/// <para>
/// The lines are read as Mermaid's own grammar reads them. A label is in double quotes or single, and holds anything but the quote
/// it is in, on one line: nothing in it is a code standing for something else. A value is digits, with <c>,</c>, <c>_</c> and a
/// point among them as a reader writes a number. <c>classDef</c> writes a class's style. What nests an item under another — the
/// nearest line before it indented less — is the whole block's to say, and its stages say it (<see cref="Stages.ResolveItems"/>).
/// </para>
/// </summary>
public sealed class TreemapParser : ITranspile
{
    private TreemapParser()
    {
    }

    /// <summary>The language a treemap is written in: what a fence names it by, and what a <c>mermaid</c> block headed by it is held as.</summary>
    public const string Language = "treemap";

    /// <summary>The words a treemap's header is written with.</summary>
    public static readonly string[] Keywords = ["treemap-beta", Language];

    /// <summary>What stands between a leaf's label and its value.</summary>
    public const string Colon = ":";

    /// <summary>What may stand there instead.</summary>
    public const string Comma = ",";

    /// <summary>What stands between an item and the class it takes.</summary>
    public const string StyleMark = ":::";

    /// <summary>What a label is put in where it is written fresh.</summary>
    public const char Opening = '"';

    /// <summary>What opens a comment running to the end of its line, outside a label.</summary>
    private const string Comment = "%%";

    /// <summary>The <c>classDef</c> lines, read as every diagram reads them.</summary>
    public static readonly MermaidStyling Styling = new(ClassLetter, TreemapRoles.Class, TreemapRoles.Class, "item");

    private const string ItemShape = "An item is its label in quotes — a section, \"Products\", or a leaf with its value, \"Phones\": 50 — and its class after it, :::important.";
    private const string HeaderShape = "A treemap's header is treemap-beta, with nothing after it: its title is a line of its own.";
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

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: between a label's quotes, and after a leaf's colon where its value is still to write.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind is TreemapKinds.Label or MermaidKinds.Amount;

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

        var said = Statement(text, from);
        lines.Add(MermaidParser.Line(source, row.Start, from, [said], from + said.Width, row));
        return row.Stop;
    }

    /// <summary>The header — <c>treemap-beta</c> or <c>treemap</c> — or null where the line says something else, which a treemap fence needs no header before.</summary>
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

    /// <summary>A title, a <c>classDef</c>, or an item — which is what everything else is meant to be.</summary>
    private static ContentNode Statement(string text, int at)
    {
        if (Titled(text, at) is { } title) return title;

        var line = MermaidLine.Of(text, at, comments: false);
        if (MermaidLine.Keyword(text, MermaidStyling.ClassDefWord) is not null) return Styling.Defined(MermaidLine.Of(text, at), TreemapKinds.ClassDef);

        return Item(line);
    }

    /// <summary>A title: its word and what it says to the end of the line, or to a comment. Null where the line does not start with the word.</summary>
    private static ContentNode? Titled(string text, int at)
    {
        var line = MermaidLine.Of(text, at);
        if (!line.Word(MermaidLine.TitleWord)) return null;

        line.Space();
        if (!line.Done) line.Words(MermaidRoles.Title);

        return line.Read(MermaidKinds.Title);
    }

    /// <summary>
    /// An item: its label, then — for a leaf — a colon or a comma and its value, then the class it takes after <c>:::</c>, space allowed
    /// between any two of them, and a comment after it. A value still to write is no complaint: a hole stands where it goes. Anything
    /// that is not the shape of one is held as written, with the shape it should have.
    /// </summary>
    private static ContentNode Item(MermaidLine line)
    {
        if (line.Next is not ('"' or '\'')) return line.Shown(ItemShape);
        if (!Labelled(line)) return line.Shown(ItemShape);

        line.Space();

        if (!line.Sees(StyleMark) && (line.Sees(Colon) || line.Sees(Comma)))
        {
            line.Token(line.Sees(Colon) ? Colon : Comma);
            line.Room();
            line.Amount(TreemapRoles.Value, Valued, until: Colon, stop: Comment);
            line.Space();
        }

        if (line.Sees(StyleMark))
        {
            line.Token(StyleMark);
            if (!line.Done && !line.Name(TreemapRoles.Class, ClassLetter)) return line.Shown(ItemShape);
            line.Space();
        }

        if (line.Sees(Comment)) line.Add(ContentNode.Leaf(Kinds.Comment, line.Rest, Roles.Trivia, offset: line.Begins));

        return line.Done ? line.Read(TreemapKinds.Item) : line.Shown(ItemShape);
    }

    /// <summary>A label: the quote it opens with, what it says, and the same quote closing it. False where nothing closes it, with the reason.</summary>
    private static bool Labelled(MermaidLine line)
    {
        var written = line.Rest;
        var close = written.IndexOf(written[0], 1);
        if (close < 0) return line.Fail(NeverClosed);

        var at = line.Begins;
        var quote = written[..1];

        line.Open();
        line.Add(ContentNode.Leaf(Kinds.Token, quote, Roles.Open, offset: at));
        line.Add(ContentNode.Leaf(Kinds.Words, written[1..close], TreemapRoles.Label, offset: at + 1));
        line.Add(ContentNode.Leaf(Kinds.Token, quote, Roles.Close, offset: at + close));
        line.Close(TreemapKinds.Label);

        return true;
    }

    /// <summary>What is wrong with a value as it is written — null where it is a number as Mermaid reads one.</summary>
    private static string? Valued(string number) =>
        !number.All(Numeral) || TreemapValues.Read(number) is null ? $"A value is a number, such as 12, 2.5 or 1,500: {number} is not one." : null;

    /// <summary>What a value is written with: digits, and the point, comma and underscore a reader writes a number with.</summary>
    public static bool Numeral(char character) => char.IsAsciiDigit(character) || character is '.' or ',' or '_';

    /// <summary>What a class's name is written with.</summary>
    public static bool ClassLetter(char character) => char.IsAsciiLetterOrDigit(character) || character == '_';

    // ── Writing back ────────────────────────────────────────────────────────

    /// <inheritdoc cref="ITranspile.Rewrite"/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>
    /// A change as a treemap spells it: into a label, without the quote it is in, which nothing in it can stand for; into a value, only
    /// what a number is written with; anywhere else, as every block written Mermaid's way spells it.
    /// </summary>
    private static string? Spelled(ContentPart part, string text) =>
        LabelOf(part) is { } label ? Unquoted(text, Quote(label))
        : part.Role == TreemapRoles.Value || part.Parent?.Kind == MermaidKinds.Amount ? new string([.. text.Where(Numeral)])
        : MermaidParser.Spelled(part, text);

    /// <summary>The label <paramref name="part"/> is in — its words, or the hole in them — or null where it is in none.</summary>
    public static ContentPart? LabelOf(ContentPart? part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == TreemapKinds.Label) return at;

        return null;
    }

    /// <summary>The quote <paramref name="label"/> is written in.</summary>
    public static char Quote(ContentPart label) => label.Children.FirstOrDefault(child => child.Role == Roles.Open)?.Text[0] ?? Opening;

    /// <summary><paramref name="text"/> on one line, without <paramref name="quote"/>, which would end the label it goes in.</summary>
    public static string Unquoted(string text, char quote) =>
        new([.. text.Where(character => character != quote && character is not ('\r' or '\n'))]);
}
