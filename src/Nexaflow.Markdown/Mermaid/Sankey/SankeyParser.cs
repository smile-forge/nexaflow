using System;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Sankey;

/// <summary>
/// Reads a sankey diagram — a <c>sankey-beta</c> or <c>sankey</c> block, a flow to a row, written as the three columns of a CSV
/// record: where it comes from, where it goes, and what it is worth — and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own — a <c>mermaid</c> fence says only which (<see cref="MermaidFenceParser"/>) — so this
/// reads its own source rather than being handed rows of it. What a fence holds round any diagram — the front matter, the
/// header, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is read with the shared
/// pieces (<see cref="MermaidParser"/>), because those are facts about a block written in Mermaid's syntax rather than about a
/// sankey. The header is optional, since a <c>sankey</c> fence already says what the block is.
/// </para>
/// <para>
/// The rules are Mermaid's, which are RFC 4180's cut to three fields. A field holds anything but a comma and a quote as it is;
/// one that needs either is written in quotes, a quote inside those is written twice, and a field in quotes runs to its
/// closing quote wherever that stands — across a line ending too, as Mermaid's own reader takes it. Only a quote opening a
/// field opens one, and a quote nothing closes is a character like any other, so one typed by mistake costs the row it is on
/// rather than everything under it. Nothing declares a node: the nodes are the names the flows are written between, in the
/// order they are first written.
/// </para>
/// <para>
/// A name is how every other row says which node it means, and a sankey writes a quote in one by writing it twice, rather
/// than as the entity code the rest of Mermaid uses. A name written bare holds neither a comma nor a quote: being given
/// either means the whole field has to be written again in quotes, which is more than the run of words this is asked about,
/// so it is refused — a keystroke that needs it is caught before this, where the field can be written again. Everything else
/// a sankey writes, Mermaid spells.
/// </para>
/// </summary>
public sealed class SankeyParser : ITranspile
{
    private SankeyParser()
    {
    }

    /// <summary>What a quote inside a quoted field is written as.</summary>
    public const string DoubledQuote = "\"\"";

    /// <summary>The language a sankey is written in: what a fence names it by, and what a <c>mermaid</c> block headed by it is held as.</summary>
    public const string Language = "sankey";

    /// <summary>The words a sankey's header is written with — Mermaid's own, and the one it wrote while the diagram was in beta.</summary>
    public static readonly string[] Keywords = ["sankey-beta", Language];

    private const string HeaderShape = "A sankey's header is its keyword alone, with the flows on the lines under it: sankey-beta.";

    /// <summary>What opens a comment running to the end of its row, outside a field in quotes.</summary>
    private const string Comment = "%%";

    private const string Shape = "A flow is where it comes from, where it goes and what it is worth: Bio-conversion,Losses,26.862.";

    /// <summary>Reads a block into its lines, every character kept.</summary>
    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;

        var lines = new List<ContentNode>();
        var at = 0;
        var headed = false;

        if (MermaidParser.Fences(source) is (var open, var close))
        {
            // Only blank lines come before front matter; that is what makes it front matter.
            for (; at < open.Start; at = Next(source, at, ref headed, lines)) { }

            lines.Add(MermaidParser.FrontMatter(source, open, close));
            at = close.Stop;
        }

        while (at < source.Length) at = Next(source, at, ref headed, lines);

        return new BlockNode(Language, lines, MermaidKinds.Block);
    }

    /// <summary>
    /// Whether a hole stands in <paramref name="node"/> while somebody writes in the block: between a name's quotes, where an
    /// empty name is written, and where a value is still to be written.
    /// </summary>
    public static bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind == MermaidKinds.Amount || (node.Kind == MermaidKinds.Name && node.Width <= 2);

    /// <inheritdoc/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>
    /// What a name says: what is between its quotes where it has them, with a quote written twice standing for one, and
    /// without the space either side of it, as Mermaid reads a field.
    ///
    /// <para>
    /// The other way round from <see cref="Named"/>, and the reason both are here: one field holds a name bare and the
    /// next holds the same name in quotes, so saying what one holds and writing it into the other are two different
    /// spellings of the one name. Asked of a whole field, quotes and all, or of the run of words inside one — a run holds
    /// no bare quote either way, so the answer is the same.
    /// </para>
    /// </summary>
    public static string Said(string written)
    {
        var said = written.Trim();

        if (said.Length >= 2 && said[0] == '"' && said[^1] == '"') said = said[1..^1];

        return said.Replace(DoubledQuote, "\"", StringComparison.Ordinal).Trim();
    }

    /// <summary>A change as a sankey diagram spells it.</summary>
    private static string? Spelled(ContentPart part, string text) =>
        part.Role is SankeyRoles.Source or SankeyRoles.Target ? Named(part, text) : MermaidParser.Spelled(part, text);

    /// <summary>
    /// A name as the field holding it is written: between quotes, a quote written twice; written bare, nothing that
    /// would end the field.
    /// </summary>
    private static string? Named(ContentPart part, string text) =>
        Quoted(part)
            ? text.Replace("\"", DoubledQuote, StringComparison.Ordinal)
            : text.Any(character => character is ',' or '"') ? null : text;

    /// <summary>
    /// Whether the field being written into is in quotes. A sankey field opens with a quote of its own rather than
    /// holding a quoted run, so this is asked of what holds the words as well as of the words themselves.
    /// </summary>
    private static bool Quoted(ContentPart part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Children.Any(child => child.Role == Roles.Open && child.Text == "\"")) return true;

        return false;
    }

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>The next line of the block, read and added to <paramref name="lines"/>, and where the one after it starts.</summary>
    /// <param name="headed">Whether the first line saying anything has been read: only it is the header.</param>
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
            lines.Add(MermaidParser.Line(source, row.Start, from,
                                         [ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia, offset: from)], to, row));
            return row.Stop;
        }

        // A binding standing on a line of its own after the header is flows supplied by whatever the diagram is shown against.
        // Held as written; read into its place before the diagram is worked over.
        if (BoundText.Path(text) is not null)
        {
            lines.Add(MermaidParser.Line(source, row.Start, from,
                [headed
                    ? ContentNode.Leaf(Kinds.BoundContent, text, offset: from)
                    : ContentNode.Shown(text, "A diagram is not bound whole: its first line names its type, and a binding after it supplies lines of it.", offset: from)],
                to, row));
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

        // The flow, which is as much of the source as it takes rather than as much as the row holds: a field in quotes runs to
        // its closing quote. As much of that as is read is what was written, and the rest is the line's.
        var said = Flow(source[from..Ended(source, from)], from);
        var last = MermaidParser.Row.Containing(source, Math.Max(from, from + said.Width - 1));

        lines.Add(MermaidParser.Line(source, row.Start, from, [said], from + said.Width, last));
        return last.Stop;
    }

    /// <summary>
    /// Where the flow starting at <paramref name="from"/> ends: the end of the row a line ending outside a field in quotes
    /// first ends, or the end of the row a comment is written on — what is after <c>%%</c> is the comment's, quotes and all.
    /// A quote opens a field only where a field starts, at the start of the flow or after a comma, with space before it.
    /// </summary>
    private static int Ended(string source, int from)
    {
        var opening = true;

        for (var at = from; at < source.Length; at++)
        {
            var character = source[at];

            if (character == '\n') return MermaidParser.Row.Containing(source, at).End;
            if (character == '%' && at + 1 < source.Length && source[at + 1] == '%') return MermaidParser.Row.Containing(source, at).End;

            if (character == ',')
            {
                opening = true;
                continue;
            }

            if (character is ' ' or '\t') continue;

            if (character == '"' && opening && Closing(source, at) is var close and >= 0) at = close;
            opening = false;
        }

        return MermaidParser.Row.Containing(source, source.Length).End;
    }

    /// <summary>
    /// Where the field in quotes opening at <paramref name="open"/> closes — past every quote written twice — or -1 where
    /// nothing closes it.
    /// </summary>
    private static int Closing(string source, int open)
    {
        for (var at = open + 1; at < source.Length; at++)
        {
            if (source[at] != '"') continue;
            if (at + 1 < source.Length && source[at + 1] == '"')
            {
                at++;
                continue;
            }

            return at;
        }

        return -1;
    }

    // ── The header ──────────────────────────────────────────────────────────

    /// <summary>
    /// The header — <c>sankey-beta</c> or <c>sankey</c>, with nothing after it but a comment — or null where the line says something
    /// else, which a sankey fence needs no header before.
    /// </summary>
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
            pieces.Add(said.StartsWith(Comment, StringComparison.Ordinal)
                           ? ContentNode.Leaf(Kinds.Comment, said, Roles.Trivia, offset: after)
                           : ContentNode.Shown(said, HeaderShape, offset: after));
        }

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    // ── Flows ───────────────────────────────────────────────────────────────

    /// <summary>
    /// One flow: where it comes from, a comma, where it goes, a comma, and what it is worth — or the text held as written,
    /// with the shape a flow takes, where it is not one.
    /// </summary>
    private static ContentNode Flow(string text, int at)
    {
        var line = MermaidLine.Of(text, at);

        if (!Field(line, SankeyRoles.Source) || !line.Token(",")) return line.Shown(Shape);
        if (!Field(line, SankeyRoles.Target) || !line.Token(",")) return line.Shown(Shape);

        line.Room();

        // Mermaid reads what a flow is worth as parseFloat does: in quotes or not — a CSV export quotes every field — and as far as a
        // number is written, so a unit or a % after it is the field's but not the number's: "10", 12 TWh, 10%.
        var quoted = line.Next == '"';
        if (quoted) line.Token("\"", Roles.Open);

        // Any number, as Mermaid takes one: a flow worth less than nought counts for nought, as one worth nought does.
        var worth = MermaidNumber.Where(double.IsFinite, "A flow is worth a number.");
        var number = Numeric(line.Written, line.At);
        var rest = quoted && line.At < line.Written.Length ? line.Written.IndexOf('"', line.At) : line.Written.Length;
        if (rest < 0) rest = line.Written.Length;

        // What is no number at all, or runs on into another field, is read whole and said to be no number: Mermaid reads three fields.
        if (number == line.At || number > rest || line.Written.AsSpan(number, rest - number).Contains(','))
        {
            line.Amount(SankeyRoles.Value, worth, until: quoted ? "\"" : null);
        }
        else
        {
            line.Amount(SankeyRoles.Value, worth, number);
            if (rest > line.At) line.Words(SankeyRoles.Value, rest);
        }

        if (quoted) line.Token("\"", Roles.Close);

        line.Space();
        return line.Done ? line.Read(SankeyKinds.Flow) : line.Shown(Shape);
    }

    /// <summary>Where the number written at <paramref name="at"/> ends, as parseFloat reads one: a sign, digits, a point and an exponent — or <paramref name="at"/> where none is.</summary>
    private static int Numeric(string text, int at)
    {
        var end = at;
        if (end < text.Length && text[end] is '+' or '-') end++;

        var digits = end;
        while (end < text.Length && (char.IsAsciiDigit(text[end]) || text[end] == '.')) end++;
        if (end == digits) return at;

        if (end + 1 < text.Length && text[end] is 'e' or 'E' && (char.IsAsciiDigit(text[end + 1]) || text[end + 1] is '+' or '-'))
        {
            var past = end + 1;
            if (text[past] is '+' or '-') past++;
            if (past < text.Length && char.IsAsciiDigit(text[past]))
            {
                end = past;
                while (end < text.Length && char.IsAsciiDigit(text[end])) end++;
            }
        }

        return end;
    }

    /// <summary>
    /// One field, quoted or not: the name as it was written, quotes and all, so what it says is read back from it rather than
    /// worked out here.
    /// </summary>
    private static bool Field(MermaidLine line, string role)
    {
        line.Space();
        line.Open();

        if (line.Next == '"')
        {
            if (!line.Quoted(role, kind: null, what: "name", doubled: true))
            {
                line.Close(MermaidKinds.Name, role);
                return false;
            }
        }
        else
        {
            line.Words(role, until: ",\"");
        }

        line.Close(MermaidKinds.Name, role);
        line.Space();
        return true;
    }
}
