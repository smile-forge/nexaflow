using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Sequence.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Sequence;

/// <summary>
/// What a <c>sequenceDiagram</c> block says: the participants along the top, the messages between their lifelines down the
/// page, the notes beside them, the bars saying one is working, and the boxes and frames drawn round a run of either.
///
/// <para>
/// The rules are Mermaid's. <strong>A name is whatever is written where one goes</strong>, space and all, up to what a line is
/// written with — a colon, a comma, a semicolon, an angle bracket, a plus or the <c>@</c> opening its metadata — or up to the
/// characters of an arrow, which is what lets <c>Order-Service</c> be a name and <c>Order-&gt;&gt;Service</c> be a message.
/// </para>
/// </summary>
public sealed class SequenceGrammar : IMermaidGrammar
{
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
    public static readonly string[] Frames = ["alt", "opt", "loop", "par", "critical", "break", RectWord];

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
    /// Which head each end draws is <see cref="SequenceDiagram.Ended"/>.
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

    /// <summary>And what a name typed into never holds, since a name cannot be quoted and there would be nowhere to put it.</summary>
    private const string Never = ":,;<>+@-|/\\%";

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

    /// <inheritdoc/>
    /// <remarks>Nothing follows the keyword: everything a sequence diagram says, it says on a line of its own.</remarks>
    public ContentNode? Header(string arguments)
    {
        var line = MermaidLine.Of(arguments);
        if (line.Done) return null;

        return ContentNode.Shown(arguments, "Nothing follows sequenceDiagram — it is written on a line of its own.",
                                 MermaidRoles.Arguments);
    }

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

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

    /// <inheritdoc/>
    /// <remarks>Nothing: everything a sequence diagram says is written on one line, and what nests is closed by a word.</remarks>
    public IEnumerable<MermaidStretch> Stretches => [];

    /// <inheritdoc/>
    /// <remarks>
    /// Under the participants at the top, and inside a box, another participant; and everywhere else a message, which is what a
    /// sequence diagram is mostly made of.
    /// </remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => above?.Kind switch
    {
        SequenceKinds.Participant or SequenceKinds.Created or SequenceKinds.Box => (ParticipantWord + " ", ParticipantWord.Length + 1),
        SequenceKinds.Ends or SequenceKinds.Numbering => null,
        _ => ("->>: ", 0),
    };

    /// <inheritdoc/>
    /// <remarks>
    /// A name holds anything that does not end one — the characters an arrow is written with, and what goes between a message's
    /// parts. What a message says, what a note says and what a frame holds under are written to the end of the line and hold
    /// anything at all.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        // In quotes anything but a quote goes in as it is, and the quote has already been written as its entity code.
        if (part.Parent?.Children.Any(child => child.Role == Roles.Open && child.Text == "\"") ?? false) return null;

        // A hole stands for what is not written yet, so what may go in it is what holds it.
        var role = part.Kind == Kinds.Hole ? part.Parent?.Role ?? part.Role : part.Role;

        if (role is SequenceRoles.Id) return Written(part, caret, text);
        if (role is SequenceRoles.Key or SequenceRoles.Type) return MermaidWriting.Only(caret, text, MermaidLine.Letter);
        if (role is SequenceRoles.Menu) return MermaidWriting.Only(caret, text, character => character != '@');
        if (role is SequenceRoles.Url) return MermaidWriting.Only(caret, text, Linked);
        if (role is SequenceRoles.Colour) return MermaidWriting.Only(caret, text, Tinted);

        return null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A participant is declared where it is first written and used wherever it is written again — either end of a message, the
    /// participants a note is over, an <c>activate</c> line, a <c>link</c> line.
    /// </remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var said = new Dictionary<string, List<ContentPart>>(StringComparer.Ordinal);

        foreach (var name in block.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Name))
        {
            if (name.Words() is not { Role: SequenceRoles.Id, Length: > 0 } words) continue;

            if (!said.TryGetValue(words.Text, out var places)) said[words.Text] = places = [];
            places.Add(name);
        }

        return [.. said.Select(name => new MermaidName(name.Key, name.Value[0], [.. name.Value.Skip(1)]))];
    }

    /// <inheritdoc/>
    /// <remarks>A name cannot be quoted, so what would end one is dropped rather than held.</remarks>
    public string Naming(string name) => new([.. name.Where(Held)]);

    /// <inheritdoc/>
    /// <remarks>
    /// Which box or frame each line is in (<see cref="ResolveFrames"/>), and the number <c>autonumber</c> gives each message
    /// (<see cref="ResolveNumbers"/>).
    /// </remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) =>
        [new ResolveFrames(), new ResolveNumbers(SequenceConfig.Read(block.Config).Numbered)];

    /// <inheritdoc/>
    /// <remarks>Where a name is still to be written, and where what a message or a note says is still to come after its colon.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) =>
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

    /// <summary>Whether a character may go in a name at all, wherever in one it is written.</summary>
    private static bool Held(char character) => !Never.Contains(character) && !char.IsControl(character);

    /// <summary>What a colour is written with: a name, a <c>#</c> and its digits, or a function and its parts.</summary>
    private static bool Tinted(char character) =>
        char.IsLetterOrDigit(character) || character is '#' or '(' or ')' or ',' or '.' or '%';

    /// <summary>What carries a url on, which is everything but what would close the quotes or the braces round it.</summary>
    private static bool Linked(char character) => character is not ('"' or ',' or '}');

    /// <summary>
    /// A name typed into: the whole of what is typed goes in where the name still ends where it did, so a hyphen joins a name —
    /// and where it would not, what would end one is dropped instead.
    /// </summary>
    private static MermaidWriting? Written(ContentPart part, int caret, string text)
    {
        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var whole = said[..at] + text + said[at..];

        return Ident(whole, 0) == whole.Length && !whole.Any(char.IsControl)
            ? null
            : MermaidWriting.Only(caret, text, Held);
    }

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
}
