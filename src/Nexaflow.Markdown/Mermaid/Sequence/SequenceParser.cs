using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Sequence;

/// <summary>
/// Reads a sequence diagram — a <c>sequenceDiagram</c> block: the participants along the top, the messages between their lifelines
/// down the page, the notes beside them, the bars saying one is working, and the boxes and frames drawn round a run of either — and
/// says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in Mermaid's syntax
/// rather than about a sequence diagram. The header is optional, since a <c>sequenceDiagram</c> fence already says what the block
/// is.
/// </para>
/// <para>
/// The rules are Mermaid's. <strong>A name is whatever is written where one goes</strong>, space and all, up to what a line is
/// written with — a colon, a comma, a semicolon, an angle bracket, a plus or the <c>@</c> opening its metadata — or up to the
/// characters of an arrow, which is what lets <c>Order-Service</c> be a name and <c>Order-&gt;&gt;Service</c> be a message. Every
/// statement is its row, but for a participant's metadata, which runs from its <c>@{</c> to the <c>}</c> closing it across as many
/// lines as it takes. Which box or frame each line is in, and the number <c>autonumber</c> gives each message, are facts about the
/// whole block, worked out by the stages its language offers the engine (<see cref="Stages.ResolveFrames"/>,
/// <see cref="Stages.ResolveNumbers"/>). A C4 sequence reads every line that is not a macro with the pieces here
/// (<see cref="C4.C4SequenceParser"/>).
/// </para>
/// <para>
/// A participant is named once and reached by that name from every message, note and activation after it, so a character that
/// would end the name is refused where one is written into it. What ends one here is its own small set — a sequence holds a dash
/// in a name, where a state diagram does not.
/// </para>
/// </summary>
public sealed class SequenceParser : ITranspile
{
    private SequenceParser()
    {
    }

    /// <summary>The language a sequence diagram is written in: what a fence names it by, and the word its header is written with.</summary>
    public const string Language = "sequenceDiagram";

    /// <summary>The words a sequence diagram's header is written with.</summary>
    public static readonly string[] Keywords = [Language];
    public const string ParticipantWord = "participant";
    public const string ActorWord = "actor";
    public const string CreateWord = "create";
    public const string DestroyWord = "destroy";
    public const string ActivateWord = "activate";
    public const string DeactivateWord = "deactivate";
    public const string AutonumberWord = "autonumber";
    public const string OffWord = "off";
    public const string NoteWord = "note";
    public const string BoxWord = "box";
    public const string EndWord = "end";
    public const string AsWord = "as";
    public const string RectWord = "rect";
    public const string LinkWord = "link";
    public const string LinksWord = "links";
    public const string PropertiesWord = "properties";
    public const string DetailsWord = "details";

    /// <summary>What opens a participant's metadata, and what closes it.</summary>
    public const string Opening = "@{";
    public const string Shutting = "}";

    /// <summary>The <c>()</c> that runs an end of a message to the middle of the lifeline rather than to the edge of its box.</summary>
    public const string Centre = "()";

    /// <summary>The words that open a frame round the messages written until the <c>end</c> closing it.</summary>
    public static readonly string[] Frames = ["alt", "opt", "loop", "par", "par_over", "critical", "break", RectWord];

    /// <summary>The words that divide the frame they are written in.</summary>
    public static readonly string[] Sections = ["else", "and", "option"];

    /// <summary>Where a note sits against the participants it names.</summary>
    public static readonly string[] Places = ["right of", "left of", "over"];

    /// <summary>What a participant may be drawn as, which its metadata's <c>type</c> names.</summary>
    public static readonly string[] Types =
        [ParticipantWord, ActorWord, "boundary", "control", "entity", "database", "collections", "queue"];

    /// <summary>The keys its metadata sets.</summary>
    public static readonly string[] Keys = ["type", "alias"];

    /// <summary>
    /// The characters a message is drawn with, longest first so <c>-&gt;</c> does not stand for the start of <c>-&gt;&gt;</c>.
    /// Which head each end draws is the builder's to say.
    /// </summary>
    public static readonly string[] Arrows =
        [.. new[]
        {
            "<<-->>", "<<->>", "-->>", "-->", "--x", "--)", "->>", "->", "-x", "-)",
            "--|\\", "--|/", "/|--", "\\|--", "-|\\", "-|/", "/|-", "\\|-",
            "--\\\\", "--//", "//--", "\\\\--", "-\\\\", "-//", "//-", "\\\\-",
        }.OrderByDescending(arrow => arrow.Length)];

    /// <summary>What ends a name wherever one is written, besides the characters of an arrow.</summary>
    private const string Stops = ":,;<>+@";

    /// <summary>Whether a character carries a name on, which is every one that does not end one.</summary>
    public static bool Bare(char character) => !Stops.Contains(character, StringComparison.Ordinal);

    private const string ParticipantShape = "A participant is written participant A, participant A as Alice, or actor A.";
    private const string LifetimeShape = "A participant is made by create participant B, and ended by destroy B.";
    private const string TurnShape = "A bar is started by activate A and ended by deactivate A.";
    private const string MessageShape = "A message joins two participants: Alice->>John: Hello John.";
    private const string NoteShape = "A note is written Note right of A: text, or Note over A,B: text.";
    private const string BoxShape = "A box is written box Aqua Group Description, and closed by end.";
    private const string FrameShape = "A frame is written alt Is it?, and closed by end.";
    private const string SectionShape = "A frame is divided by else, and or option, with what follows it written after.";
    private const string NumberShape = "Numbering is written autonumber, autonumber off, or autonumber 10 10.";
    private const string LinkShape = "A link is written link A: Dashboard @ https://example.com.";
    private const string MenuShape = "Several at once are written links A: {\"Dashboard\": \"https://example.com\"}.";
    private const string MetaShape = "Metadata is written A@{ \"type\": \"database\", \"alias\": \"Users\" }.";

    /// <summary>One statement — or the line held as written, with the reason. Null for a title with nothing written after it yet.</summary>
    internal static ContentNode? Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at);

        return MermaidLine.Keyword(line.Written, MermaidLine.Letter,
                [MermaidLine.TitleWord, ParticipantWord, ActorWord, CreateWord, DestroyWord, ActivateWord, DeactivateWord,
                 AutonumberWord, NoteWord, BoxWord, EndWord, LinkWord, LinksWord, PropertiesWord, DetailsWord,
                 .. Frames, .. Sections]) switch
        {
            MermaidLine.TitleWord => line.Title(),
            ParticipantWord => Standing(line, ParticipantWord),
            ActorWord => Standing(line, ActorWord),
            CreateWord => Making(line),
            DestroyWord => Ending(line),
            ActivateWord => Turning(line, ActivateWord),
            DeactivateWord => Turning(line, DeactivateWord),
            AutonumberWord => Numbering(line),
            NoteWord => Noted(line),
            BoxWord => Boxed(line),
            EndWord => Ended(line),
            LinkWord => Leading(line),
            LinksWord => Menued(line, LinksWord),
            PropertiesWord => Menued(line, PropertiesWord),
            DetailsWord => Menued(line, DetailsWord),
            { } word when Sections.Contains(word, StringComparer.Ordinal) => Divided(line, word),
            { } word => Framed(line, word),
            _ => Messaged(line),
        };
    }

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: where a name is still to be written, and where what a message or a note says is still to come after its colon.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind == SequenceKinds.Said
        || (node.Kind == MermaidKinds.Name && node.Role is SequenceRoles.Id or SequenceRoles.Label);

    /// <summary>
    /// Where the name written at <paramref name="at"/> ends: at what a line is written with, at the <c>()</c> running a message
    /// to the middle of a lifeline, or where the characters of an arrow begin — and, where <paramref name="until"/> is given, at
    /// that word written on its own, which is how <c>participant A as Alice</c> keeps <c>as</c> out of the name.
    /// </summary>
    public static int Ident(string written, int at, string? until = null)
    {
        for (var index = at; index < written.Length; index++)
        {
            if (Stops.Contains(written[index])) return index;
            if (written.AsSpan(index).StartsWith(Centre, StringComparison.Ordinal)) return index;

            foreach (var arrow in Arrows)
                if (written.AsSpan(index).StartsWith(arrow, StringComparison.Ordinal)) return index;

            if (until is not null && index > at && written[index - 1] is ' ' or '\t'
                && MermaidLine.Keyword(written[index..], until) is not null) return index;
        }

        return written.Length;
    }

    /// <summary>What carries a url on, which is everything but what would close the quotes or the braces round it.</summary>
    internal static bool Linked(char character) => character is not ('"' or ',' or '}');

    /// <summary>What opens a comment running to the end of its row.</summary>
    private const string Comment = "%%";

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

    // ── Reading the block ───────────────────────────────────────────────────

    /// <summary>The next line of the block, read and added to <paramref name="lines"/> — or the rows it takes — and where the one after it starts.</summary>
    /// <param name="headed">Whether the first line saying anything has been read: only it may be the header.</param>
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

        if (SeparatorOrComment(source, row, from, to, lines) is { } apart) return apart;

        // A binding standing on a line of its own after the header is lines supplied by whatever the diagram is shown against. Held
        // as written; read into its place before the diagram is worked over.
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
            // A header may end at a ; with the first statement after it on its row, as Mermaid lets a sequence be written on one line.
            var head = text[..Ended(text, 0)];
            if (Heading(head, from) is { } header) return MermaidParser.StatementLine(source, row.Start, from, header, from + head.Length, lines);
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;

        // The statement, which is its row but for metadata running on to the brace closing it, and a ; or a # ending it sooner. As much of it as is read is what was
        // written, and the rest is the line's; a statement read as nothing yet — a title with nothing after it — is held whole.
        var end = Ended(source, from);
        var said = Statement(source[from..end], from) ?? ContentNode.Leaf(MermaidKinds.Statement, source[from..end], offset: from);

        return MermaidParser.StatementLine(source, row.Start, from, said, end, lines);
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: its row — but for metadata, which runs to the brace closing it —
    /// or before a ; or a # comment on it, which Mermaid's sequence lexer reads as a line ending and a comment. A # starting an entity
    /// code — <c>#35;</c>, <c>#quot;</c> — is a character, not a comment.
    /// </summary>
    internal static int Ended(string source, int from)
    {
        for (var at = from; at < source.Length; at++)
        {
            if (source[at] == '\n' || source.AsSpan(at).StartsWith(Comment, StringComparison.Ordinal)) return MermaidParser.Row.Containing(source, at).End;

            if (source[at] == '#')
            {
                var entity = Entity(source, at);
                if (entity == 0) return Trimmed(source, from, at);

                at += entity - 1;
                continue;
            }

            if (source[at] == ';') return Trimmed(source, from, at);
            if (!source.AsSpan(at).StartsWith(Opening, StringComparison.Ordinal)) continue;

            var close = source.IndexOf(Shutting, at + Opening.Length, StringComparison.Ordinal);
            if (close >= 0) at = close;
        }

        return MermaidParser.Row.Containing(source, source.Length).End;
    }

    /// <summary>Where <paramref name="at"/> is, less the space before it back to <paramref name="from"/>.</summary>
    private static int Trimmed(string source, int from, int at)
    {
        while (at > from && char.IsWhiteSpace(source[at - 1])) at--;
        return at;
    }

    /// <summary>How many characters the entity code written at <paramref name="at"/> takes — <c>#35;</c>, <c>#quot;</c> — or nought where none is.</summary>
    internal static int Entity(string source, int at)
    {
        return MermaidLine.EntityLength(source, at);
    }

    /// <summary>
    /// A ; or a # comment standing where a statement would start, read and added to <paramref name="lines"/>, and where the next line
    /// starts — or null where neither stands there. Mermaid's sequence lexer reads a ; as a line ending, so a sequence may be written on
    /// one line, and what follows a # as a comment.
    /// </summary>
    internal static int? SeparatorOrComment(string source, MermaidParser.Row row, int from, int to, List<ContentNode> lines)
    {
        if (source[from] == ';')
            return MermaidParser.StatementLine(source, row.Start, from, ContentNode.Leaf(Kinds.Token, ";", Roles.Separator, offset: from), from + 1, lines);

        if (source[from] != '#' || Entity(source, from) > 0) return null;

        lines.Add(MermaidParser.Line(source, row.Start, from, [ContentNode.Leaf(Kinds.Comment, source[from..to], Roles.Trivia, offset: from)], to, row));
        return row.Stop;
    }

    /// <summary>
    /// The header — <c>sequenceDiagram</c>, and anything after it held as written — or null where the line says something else,
    /// which a <c>sequenceDiagram</c> fence needs no header before.
    /// </summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(Arguments(rest[gap..], after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    /// <summary>What follows the keyword: nothing but a comment, since everything a sequence diagram says, it says on a line of its own.</summary>
    private static ContentNode Arguments(string arguments, int at) =>
        MermaidLine.Of(arguments, at).Done
            ? ContentNode.Leaf(Kinds.Verbatim, arguments, MermaidRoles.Arguments, offset: at)
            : ContentNode.Shown(arguments, "Nothing follows sequenceDiagram — it is written on a line of its own.", MermaidRoles.Arguments, at);
    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>A participant, declared where it is written so the lifelines stand in the order the reader asked for.</summary>
    private static ContentNode Standing(MermaidLine line, string word)
    {
        line.Word(word, MermaidKinds.Key, SequenceRoles.Word, MermaidLine.Letter);
        line.Room();
        Declared(line);

        return line.Closed(SequenceKinds.Participant, ParticipantShape);
    }

    /// <summary>A participant made partway down, whose lifeline starts at the message that makes it.</summary>
    private static ContentNode Making(MermaidLine line)
    {
        line.Word(CreateWord, MermaidKinds.Key, SequenceRoles.Lifetime, MermaidLine.Letter);
        line.Room();

        if (MermaidLine.Keyword(line.Rest, MermaidLine.Letter, ParticipantWord, ActorWord) is { } word)
        {
            line.Word(word, MermaidKinds.Key, SequenceRoles.Word, MermaidLine.Letter);
            line.Room();
        }

        Declared(line);
        return line.Closed(SequenceKinds.Created, LifetimeShape);
    }

    /// <summary>A participant ended partway down, its lifeline stopping with a cross where it ends.</summary>
    private static ContentNode Ending(MermaidLine line)
    {
        line.Word(DestroyWord, MermaidKinds.Key, SequenceRoles.Lifetime, MermaidLine.Letter);
        line.Room();
        Named(line);

        return line.Closed(SequenceKinds.Destroyed, LifetimeShape);
    }

    /// <summary>A bar started or ended on a lifeline.</summary>
    private static ContentNode Turning(MermaidLine line, string word)
    {
        line.Word(word, MermaidKinds.Key, SequenceRoles.Turns, MermaidLine.Letter);
        line.Room();
        Named(line);

        return line.Closed(SequenceKinds.Activation, TurnShape);
    }

    /// <summary>A message: the participant it leaves, what it is drawn with, the one it reaches, and what it says.</summary>
    private static ContentNode Messaged(MermaidLine line)
    {
        Named(line);
        line.Space();
        line.Token(Centre, SequenceRoles.Centre);
        line.Space();

        if (!Arrowed(line)) return line.Shown(MessageShape);

        line.Space();
        line.Token(Centre, SequenceRoles.Centre);
        line.Space();

        if (line.Next is '+' or '-') line.Token(line.Next.ToString(), SequenceRoles.Turns);
        line.Space();

        Named(line);
        Says(line);

        return line.Closed(SequenceKinds.Message, MessageShape);
    }

    /// <summary>A note, beside one participant or spanning several.</summary>
    private static ContentNode Noted(MermaidLine line)
    {
        line.Word(NoteWord, MermaidKinds.Key, Roles.Name, MermaidLine.Letter);
        line.Room();

        if (MermaidLine.Keyword(line.Rest, MermaidLine.Letter, Places) is not { } place) return line.Shown(NoteShape);

        line.Word(place, MermaidKinds.Setting, SequenceRoles.Place, MermaidLine.Letter);
        line.Room();

        if (!line.Names(Naming, SequenceRoles.Id, ParticipantWord)) return line.Shown(NoteShape);
        if (!Says(line)) return line.Shown(NoteShape);

        return line.Closed(SequenceKinds.Note, NoteShape);
    }

    /// <summary>A box, which tints the participants written until the <c>end</c> closing it.</summary>
    private static ContentNode Boxed(MermaidLine line)
    {
        line.Word(BoxWord, MermaidKinds.Key, Roles.Name, MermaidLine.Letter);
        line.Room();
        Coloured(line);
        Spaced(line);

        return line.Closed(SequenceKinds.Box, BoxShape);
    }

    /// <summary>A frame round the messages written until the <c>end</c> closing it.</summary>
    private static ContentNode Framed(MermaidLine line, string word)
    {
        line.Word(word, MermaidKinds.Setting, SequenceRoles.Word, MermaidLine.Letter);
        line.Room();

        if (string.Equals(word, RectWord, StringComparison.Ordinal)) Coloured(line);
        Spaced(line);

        return line.Closed(SequenceKinds.Frame, FrameShape);
    }

    /// <summary>A line dividing the frame it is written in.</summary>
    private static ContentNode Divided(MermaidLine line, string word)
    {
        line.Word(word, MermaidKinds.Setting, SequenceRoles.Word, MermaidLine.Letter);
        line.Room();
        Spaced(line);

        return line.Closed(SequenceKinds.Section, SectionShape);
    }

    private static ContentNode Ended(MermaidLine line)
    {
        line.Word(EndWord, MermaidKinds.Key, Roles.Name, MermaidLine.Letter);

        return line.Closed(SequenceKinds.Ends, "A box or a frame is closed by end, with nothing after it.");
    }

    /// <summary>What numbers the messages under it, from where it says and by as much as it says.</summary>
    private static ContentNode Numbering(MermaidLine line)
    {
        line.Word(AutonumberWord, MermaidKinds.Key, Roles.Name, MermaidLine.Letter);
        line.Space();

        if (MermaidLine.Keyword(line.Rest, MermaidLine.Letter, OffWord) is not null)
        {
            line.Word(OffWord, MermaidKinds.Setting, SequenceRoles.Off, MermaidLine.Letter);
            return line.Closed(SequenceKinds.Numbering, NumberShape);
        }

        if (line.Done) return line.Closed(SequenceKinds.Numbering, NumberShape);

        line.Amount(SequenceRoles.Start, Counted, until: " \t");
        line.Space();

        if (!line.Done) line.Amount(SequenceRoles.Step, Counted, until: " \t");

        return line.Closed(SequenceKinds.Numbering, NumberShape);
    }

    /// <summary>Somewhere a participant leads, drawn under it and followed where it is pressed.</summary>
    private static ContentNode Leading(MermaidLine line)
    {
        line.Word(LinkWord, MermaidKinds.Key, Roles.Name, MermaidLine.Letter);
        line.Room();
        Named(line);
        line.Space();

        if (!line.Token(":", Roles.Open)) return line.Shown(LinkShape);

        line.Room();
        line.Words(SequenceRoles.Menu, until: "@");
        line.Space();

        if (!line.Token("@")) return line.Shown(LinkShape);

        line.Room();
        line.Words(SequenceRoles.Url);

        return line.Closed(SequenceKinds.Link, LinkShape);
    }

    /// <summary>Several places at once, and the properties and details Mermaid shows beside them.</summary>
    private static ContentNode Menued(MermaidLine line, string word)
    {
        line.Word(word, MermaidKinds.Key, SequenceRoles.Word, MermaidLine.Letter);
        line.Room();
        Named(line);
        line.Space();

        if (!line.Token(":", Roles.Open)) return line.Shown(MenuShape);

        line.Room();
        if (!line.Token("{", Roles.Open)) return line.Shown(MenuShape);

        line.Room();
        if (!Paired(line)) return line.Shown(MenuShape);
        if (!line.Token(Shutting, Roles.Close)) return line.Shown(MenuShape);

        return line.Closed(SequenceKinds.Menu, MenuShape);
    }

    // ── The pieces a line is made of ────────────────────────────────────────

    /// <summary>One participant where it is named, as a message and a note name it: nothing but the name itself.</summary>
    private static void Named(MermaidLine line)
    {
        line.Open();
        Called(line, SequenceRoles.Id);
        line.Close(SequenceKinds.Named, SequenceRoles.Id);
    }

    /// <summary>And where it is declared: its metadata, and the label <c>as</c> draws instead of its name.</summary>
    private static void Declared(MermaidLine line)
    {
        line.Open();
        Called(line, SequenceRoles.Id, AsWord);
        line.Space();

        if (line.Sees(Opening)) Metaed(line);

        line.Space();
        if (MermaidLine.Keyword(line.Rest, MermaidLine.Letter, AsWord) is not null)
        {
            line.Word(AsWord, MermaidKinds.Key, Roles.Name, MermaidLine.Letter);
            line.Room();
            line.Open();
            line.Words(SequenceRoles.Label);
            line.Close(MermaidKinds.Name, SequenceRoles.Label);
        }

        line.Close(SequenceKinds.Named, SequenceRoles.Id);
    }

    /// <summary>The characters of a name — or nothing at all, where the room a hole stands in has already been read.</summary>
    private static void Called(MermaidLine line, string role, string? until = null)
    {
        line.Open();
        if (line.At <= line.Written.Length) line.Words(role, Ident(line.Written, line.At, until));
        line.Close(MermaidKinds.Name, role);
    }

    private static bool Naming(MermaidLine line)
    {
        Named(line);
        return true;
    }

    /// <summary>The <c>@{ … }</c> written against a name: what kind of thing it is drawn as, and what it is called.</summary>
    private static bool Metaed(MermaidLine line)
    {
        line.Token(Opening, Roles.Open);
        line.Room();
        line.Open();

        while (!line.Done && line.Next != '}')
        {
            line.Open();
            if (!line.Name(SequenceRoles.Key, MermaidLine.Letter)) return line.Fail(MetaShape);

            line.Space();
            if (!line.Token(":")) return line.Fail(MetaShape);

            line.Room();
            if (!line.Name(SequenceRoles.Type, MermaidLine.Letter)) return line.Fail(MetaShape);

            line.Close(MermaidKinds.Property);
            line.Room();

            if (line.Next != ',') break;

            line.Token(",");
            line.Room();
        }

        line.Close(MermaidKinds.Properties);

        return line.Token(Shutting, Roles.Close) || line.Fail(MetaShape);
    }

    /// <summary>The <c>{ "Label": "url", … }</c> of a line giving a participant several places to lead at once.</summary>
    private static bool Paired(MermaidLine line)
    {
        line.Open();

        while (!line.Done && line.Next != '}')
        {
            line.Open();
            if (!line.Name(SequenceRoles.Menu, MermaidLine.Letter)) return line.Fail(MenuShape);

            line.Space();
            if (!line.Token(":")) return line.Fail(MenuShape);

            line.Room();
            if (!line.Name(SequenceRoles.Url, Linked)) return line.Fail(MenuShape);

            line.Close(MermaidKinds.Property);
            line.Room();

            if (line.Next != ',') break;

            line.Token(",");
            line.Room();
        }

        line.Close(MermaidKinds.Properties);
        return true;
    }

    /// <summary>What a message or a note says, after the colon opening it — whether anything is written there at all.</summary>
    private static bool Says(MermaidLine line)
    {
        var mark = line.Save();
        line.Space();

        if (!line.Token(":", Roles.Open))
        {
            line.Restore(mark);
            return false;
        }

        line.Open();
        line.Room();
        line.Words(SequenceRoles.Said);
        line.Close(SequenceKinds.Said, SequenceRoles.Said);

        return true;
    }

    /// <summary>The characters a message is drawn with.</summary>
    private static bool Arrowed(MermaidLine line)
    {
        foreach (var arrow in Arrows)
            if (line.Token(arrow, SequenceRoles.Arrow)) return true;

        return line.Fail(MessageShape);
    }

    /// <summary>The colour a box or a <c>rect</c> is washed with, where one is written before what it is called.</summary>
    private static void Coloured(MermaidLine line)
    {
        if (MermaidColour.At(line.Rest) is not { } taken) return;

        line.Words(SequenceRoles.Colour, line.At + taken);
        line.Space();
    }

    /// <summary>What a box is called, or what a frame holds under — the rest of the line, whatever is written in it.</summary>
    private static void Spaced(MermaidLine line)
    {
        line.Open();
        line.Words(SequenceRoles.Space);
        line.Close(MermaidKinds.Name, SequenceRoles.Space);
    }

    private static readonly Func<string, string?> Counted =
        MermaidNumber.Where(number => number >= 0, "Numbering starts and steps by nought or more.");

    // ── Writing back ────────────────────────────────────────────────────────

    /// <inheritdoc cref="ITranspile.Rewrite"/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>A change as a sequence diagram spells it.</summary>
    private static string? Spelled(ContentPart part, string text) =>
        part.Role == SequenceRoles.Id
            ? text.All(Bare) ? text : null
            : MermaidParser.Spelled(part, text);
}
