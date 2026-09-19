using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Sequence;

/// <summary>What a participant is drawn as, from <c>actor</c> or from the <c>type</c> its metadata names.</summary>
public enum SequenceKind
{
    /// <summary>A box with its name in it, which is what a participant is unless it says otherwise.</summary>
    Participant,

    /// <summary>A figure: a head, a body and four limbs, with its name under it.</summary>
    Actor,

    /// <summary>A circle against a bar, UML's boundary.</summary>
    Boundary,

    /// <summary>A circle with an arrow round it, UML's control.</summary>
    Control,

    /// <summary>A circle on a line, UML's entity.</summary>
    Entity,

    /// <summary>A cylinder.</summary>
    Database,

    /// <summary>Two boxes, one behind the other.</summary>
    Collections,

    /// <summary>A box open at one end.</summary>
    Queue,
}

/// <summary>What a message draws at one of its ends.</summary>
public enum SequenceHead
{
    None,

    /// <summary>A filled triangle: <c>-&gt;&gt;</c>.</summary>
    Arrow,

    /// <summary>Two strokes meeting at the tip: <c>-)</c>, which says the message is not waited for.</summary>
    Open,

    /// <summary>A cross: <c>-x</c>.</summary>
    Cross,

    /// <summary>Half a triangle, above the line: <c>-|\</c>.</summary>
    HalfTop,

    /// <summary>And below it: <c>-|/</c>.</summary>
    HalfBottom,

    /// <summary>One stroke above the line: <c>-\\</c>.</summary>
    StickTop,

    /// <summary>And one below it: <c>-//</c>.</summary>
    StickBottom,
}

/// <summary>Where a note sits against the participants it names.</summary>
public enum SequencePlace { RightOf, LeftOf, Over }

/// <summary>What a frame holds its messages under.</summary>
public enum SequenceFrame
{
    /// <summary>One of several ways it may go, divided by <c>else</c>.</summary>
    Alt,

    /// <summary>Something that may not happen at all.</summary>
    Opt,

    /// <summary>Something that happens again and again.</summary>
    Loop,

    /// <summary>Things that happen at once, divided by <c>and</c>.</summary>
    Par,

    /// <summary>Something that must happen, with what may go wrong written as <c>option</c>s.</summary>
    Critical,

    /// <summary>Where it all stops.</summary>
    Break,

    /// <summary>No frame at all: a wash of colour behind the messages inside it.</summary>
    Rect,
}

/// <summary>Somewhere a participant leads, from a <c>link</c> line or one of the several a <c>links</c> line gives.</summary>
/// <param name="Part">What it was written as, which is what a press on it means.</param>
public sealed record SequenceLink(ContentPart Part, ContentPart? Said, string Url);

/// <summary>One lifeline, read: where it was written, what it is called, and what it is drawn as.</summary>
/// <param name="Part">The name as it was written, which is what a press on it means.</param>
/// <param name="Id">What it is called, which is what a message and a note name it by.</param>
public sealed record SequenceParticipant(ContentPart Part, string Id, int Order)
{
    /// <summary>The words drawn for it: the label <c>as</c> or an <c>alias</c> gives, and otherwise its name.</summary>
    public ContentPart? Said { get; init; }

    /// <summary>The hole standing where its name goes.</summary>
    public ContentPart? SaidHole { get; init; }

    public SequenceKind Kind { get; init; }

    /// <summary>Whether its lifeline starts partway down, at the message that makes it.</summary>
    public bool Created { get; init; }

    /// <summary>Whether its lifeline stops partway down, with a cross where it ends.</summary>
    public bool Destroyed { get; init; }

    /// <summary>The box it is grouped in, or null for one written outside them all.</summary>
    public string? Box { get; init; }

    /// <summary>Where it leads, in the order written.</summary>
    public IReadOnlyList<SequenceLink> Links { get; init; } = [];

    /// <summary>Whether anything on the timeline names it, which is what <c>hideUnusedParticipants</c> asks about.</summary>
    public bool Reached { get; init; }
}

/// <summary>Anything on the timeline, in the order it was written.</summary>
/// <param name="Part">What it was written as, which is what a press on it means.</param>
public abstract record SequenceItem(ContentPart Part, int Order);

/// <summary>One message between two lifelines, or from one to itself.</summary>
public sealed record SequenceMessage(ContentPart Part, string From, string To, int Order) : SequenceItem(Part, Order)
{
    /// <summary>What it says, drawn over the line.</summary>
    public ContentPart? Said { get; init; }

    public ContentPart? SaidHole { get; init; }

    /// <summary>Whether it is drawn dotted, which is what a message written with two dashes says.</summary>
    public bool Dotted { get; init; }

    /// <summary>What it draws at the end it leaves, and at the end it reaches.</summary>
    public SequenceHead Near { get; init; }
    public SequenceHead Far { get; init; }

    /// <summary>Whether <c>()</c> runs an end to the middle of its lifeline rather than to the edge of its box.</summary>
    public bool FromCentre { get; init; }
    public bool ToCentre { get; init; }

    /// <summary>Whether <c>+</c> starts a bar on what it reaches, and <c>-</c> ends the one on what it leaves.</summary>
    public bool Starts { get; init; }
    public bool Stops { get; init; }

    /// <summary>The number <c>autonumber</c> gives it, or null where nothing numbers it.</summary>
    public string? Number { get; init; }

    /// <summary>Whether it goes from a participant to itself, which is drawn as a loop off its own lifeline.</summary>
    public bool Self => string.Equals(From, To, StringComparison.Ordinal);
}

/// <summary>One note, beside a lifeline or spanning several.</summary>
public sealed record SequenceNote(ContentPart Part, SequencePlace Place, IReadOnlyList<string> Over, int Order)
    : SequenceItem(Part, Order)
{
    public ContentPart? Said { get; init; }

    public ContentPart? SaidHole { get; init; }
}

/// <summary>A bar started or ended on a lifeline by an <c>activate</c> or <c>deactivate</c> line.</summary>
public sealed record SequenceTurn(ContentPart Part, string Id, bool On, int Order) : SequenceItem(Part, Order);

/// <summary>Where a lifeline ends, from a <c>destroy</c> line.</summary>
public sealed record SequenceGone(ContentPart Part, string Id, int Order) : SequenceItem(Part, Order);

/// <summary>Where a frame opens, and everything about it but how far down it reaches.</summary>
/// <param name="Key">What the nesting calls it, which is what the line closing it and the lines dividing it name.</param>
public sealed record SequenceOpening(ContentPart Part, string Key, SequenceFrame Kind, int Order) : SequenceItem(Part, Order)
{
    /// <summary>What it holds its messages under — the condition an <c>alt</c> is drawn for.</summary>
    public ContentPart? Said { get; init; }

    /// <summary>The word that opened it, as it was written, which is what is drawn in its tab.</summary>
    public ContentPart? Word { get; init; }

    /// <summary>What a <c>rect</c> is washed with, where a colour is written.</summary>
    public string? Colour { get; init; }

    /// <summary>The frame it is written inside, or null for one written outside them all.</summary>
    public string? Parent { get; init; }

    /// <summary>The whole of it as written, from the line that opened it through the <c>end</c> that closed it.</summary>
    public ISourcePart Whole { get; init; } = default(SourceSpan);
}

/// <summary>A line dividing the frame it is written in: an <c>else</c>, an <c>and</c> or an <c>option</c>.</summary>
public sealed record SequenceDivider(ContentPart Part, string Key, int Order) : SequenceItem(Part, Order)
{
    public ContentPart? Said { get; init; }
}

/// <summary>Where a frame closes.</summary>
public sealed record SequenceClosing(ContentPart Part, string Key, int Order) : SequenceItem(Part, Order);

/// <summary>One box, read: the tint drawn behind a run of participants.</summary>
public sealed record SequenceBox(ContentPart Part, string Key, ContentPart? Said, string? Colour, int Order)
{
    /// <summary>The whole of it as written, from the line that opened it through the <c>end</c> that closed it.</summary>
    public ISourcePart Whole { get; init; } = default(SourceSpan);
}

/// <summary>
/// A <c>sequenceDiagram</c> block, read: the participants along the top, everything on the timeline in the order written, and
/// the boxes grouping the participants. Its title is the block's (<see cref="MermaidBlock.Title"/>).
///
/// <para>
/// <strong>This is what was written, not what is drawn.</strong> The timeline is a list in source order, with the line that
/// opens a frame, the lines that divide it and the line that closes it each standing where they were written; how deep each one
/// ends up and what boxes it is inside are the builder's, which is where the drawing's own shape is decided.
/// </para>
///
/// A participant written twice is one participant: the second writing says more about the one the first made — its label, what
/// it is drawn as — rather than making another, which is what lets a message name the participants a line above declared.
/// </summary>
public sealed class SequenceDiagram
{
    /// <summary>What a name with nothing written in it yet is known by, which is where it was written.</summary>
    private const string Unwritten = " ";

    private SequenceDiagram(MermaidBlock block, SequenceConfig config, IReadOnlyList<SequenceParticipant> participants,
                            IReadOnlyList<SequenceItem> items, IReadOnlyList<SequenceBox> boxes)
    {
        Block = block;
        Config = config;
        Participants = participants;
        Items = items;
        Boxes = boxes;
    }

    /// <summary>Reads a block: parsed, then worked over by its stages (<see cref="MermaidParser.Read"/>).</summary>
    public static SequenceDiagram Read(string? block) => Of(MermaidParser.Read(block));

    /// <summary>Reads a tree the stages have already been over.</summary>
    public static SequenceDiagram Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    /// <summary>The block this was read from — its front matter, its header, its title, everything written in it.</summary>
    public MermaidBlock Block { get; }

    /// <summary>What the front matter asks for.</summary>
    public SequenceConfig Config { get; }

    /// <summary>The participants, in the order they are first written.</summary>
    public IReadOnlyList<SequenceParticipant> Participants { get; }

    /// <summary>Everything on the timeline, in the order written.</summary>
    public IReadOnlyList<SequenceItem> Items { get; }

    /// <summary>The boxes grouping the participants, in the order written.</summary>
    public IReadOnlyList<SequenceBox> Boxes { get; }

    /// <summary>The messages, which is what a sequence diagram is mostly made of.</summary>
    public IEnumerable<SequenceMessage> Messages => Items.OfType<SequenceMessage>();

    /// <summary>The participants drawn: all of them, or only those something names where the front matter asks.</summary>
    public IEnumerable<SequenceParticipant> Drawn =>
        Config.HideUnused ? Participants.Where(one => one.Reached) : Participants;

    public SequenceParticipant? Find(string id) =>
        Participants.FirstOrDefault(one => string.Equals(one.Id, id, StringComparison.Ordinal));

    /// <summary>What a participant is drawn as, from the word it was declared with or the <c>type</c> its metadata names.</summary>
    public static SequenceKind Typed(string? said) => said?.Trim().ToLowerInvariant() switch
    {
        "actor" => SequenceKind.Actor,
        "boundary" => SequenceKind.Boundary,
        "control" => SequenceKind.Control,
        "entity" => SequenceKind.Entity,
        "database" => SequenceKind.Database,
        "collections" => SequenceKind.Collections,
        "queue" => SequenceKind.Queue,
        _ => SequenceKind.Participant,
    };

    /// <summary>Where a note sits, from the words written before the participants it names.</summary>
    public static SequencePlace Placed(string? said) => said?.Trim().ToLowerInvariant() switch
    {
        "left of" => SequencePlace.LeftOf,
        "right of" => SequencePlace.RightOf,
        _ => SequencePlace.Over,
    };

    /// <summary>What kind of frame a word opens.</summary>
    public static SequenceFrame Framed(string? said) => said?.Trim().ToLowerInvariant() switch
    {
        "opt" => SequenceFrame.Opt,
        "loop" => SequenceFrame.Loop,
        "par" => SequenceFrame.Par,
        "critical" => SequenceFrame.Critical,
        "break" => SequenceFrame.Break,
        "rect" => SequenceFrame.Rect,
        _ => SequenceFrame.Alt,
    };

    /// <summary>
    /// What the characters a message is drawn with say: whether its line is dotted, and what it draws at the end it leaves and
    /// the end it reaches. A head is written at the end it is drawn at, so <c>-|\</c> draws half a head where it arrives and
    /// <c>/|-</c> draws one where it set out.
    /// </summary>
    public static (bool Dotted, SequenceHead Near, SequenceHead Far) Ended(string? arrow) => arrow switch
    {
        "->" => (false, SequenceHead.None, SequenceHead.None),
        "-->" => (true, SequenceHead.None, SequenceHead.None),
        "->>" => (false, SequenceHead.None, SequenceHead.Arrow),
        "-->>" => (true, SequenceHead.None, SequenceHead.Arrow),
        "<<->>" => (false, SequenceHead.Arrow, SequenceHead.Arrow),
        "<<-->>" => (true, SequenceHead.Arrow, SequenceHead.Arrow),
        "-x" => (false, SequenceHead.None, SequenceHead.Cross),
        "--x" => (true, SequenceHead.None, SequenceHead.Cross),
        "-)" => (false, SequenceHead.None, SequenceHead.Open),
        "--)" => (true, SequenceHead.None, SequenceHead.Open),
        "-|\\" => (false, SequenceHead.None, SequenceHead.HalfTop),
        "--|\\" => (true, SequenceHead.None, SequenceHead.HalfTop),
        "-|/" => (false, SequenceHead.None, SequenceHead.HalfBottom),
        "--|/" => (true, SequenceHead.None, SequenceHead.HalfBottom),
        "/|-" => (false, SequenceHead.HalfTop, SequenceHead.None),
        "/|--" => (true, SequenceHead.HalfTop, SequenceHead.None),
        "\\|-" => (false, SequenceHead.HalfBottom, SequenceHead.None),
        "\\|--" => (true, SequenceHead.HalfBottom, SequenceHead.None),
        "-\\\\" => (false, SequenceHead.None, SequenceHead.StickTop),
        "--\\\\" => (true, SequenceHead.None, SequenceHead.StickTop),
        "-//" => (false, SequenceHead.None, SequenceHead.StickBottom),
        "--//" => (true, SequenceHead.None, SequenceHead.StickBottom),
        "//-" => (false, SequenceHead.StickTop, SequenceHead.None),
        "//--" => (true, SequenceHead.StickTop, SequenceHead.None),
        "\\\\-" => (false, SequenceHead.StickBottom, SequenceHead.None),
        "\\\\--" => (true, SequenceHead.StickBottom, SequenceHead.None),
        _ => (false, SequenceHead.None, SequenceHead.Arrow),
    };

    public static SequenceDiagram Of(MermaidBlock block)
    {
        var config = SequenceConfig.Read(block.Config);

        var made = new List<Made>();
        var known = new Dictionary<string, Made>(StringComparer.Ordinal);
        var items = new List<SequenceItem>();
        var boxes = new List<SequenceBox>();
        var open = new Stack<Opened>();

        foreach (var line in block.Reading.Root.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { } stated) continue;
            var inside = Keyed(stated.Fact(SequenceRoles.Inside));

            switch (stated.Kind)
            {
                case SequenceKinds.Participant:
                case SequenceKinds.Created:
                    Standing(stated, inside, boxes, made, known);
                    break;

                case SequenceKinds.Destroyed:
                    if (Gathered(stated.Inner(SequenceKinds.Named), made, known) is { } going)
                    {
                        going.Destroyed = true;
                        going.Reached = true;
                        items.Add(new SequenceGone(stated, going.Id, items.Count));
                    }

                    break;

                case SequenceKinds.Activation:
                    if (Gathered(stated.Inner(SequenceKinds.Named), made, known) is { } turning)
                    {
                        turning.Reached = true;
                        items.Add(new SequenceTurn(stated, turning.Id, Turned(stated), items.Count));
                    }

                    break;

                case SequenceKinds.Message:
                    Messaged(stated, made, known, items);
                    break;

                case SequenceKinds.Note:
                    Noted(stated, made, known, items);
                    break;

                case SequenceKinds.Box:
                    var boxed = Keyed(stated.Fact(SequenceRoles.Opened)) ?? Unwritten + stated.Start;
                    boxes.Add(new SequenceBox(stated, boxed, Spaced(stated), Coloured(stated), boxes.Count)
                    {
                        Whole = new SourceSpan(stated.Start, stated.End - stated.Start),
                    });
                    open.Push(new Opened(boxed, boxes.Count - 1, Box: true));
                    break;

                case SequenceKinds.Frame:
                    var framed = Keyed(stated.Fact(SequenceRoles.Opened)) ?? Unwritten + stated.Start;
                    items.Add(new SequenceOpening(stated, framed, Framed(Said(stated, MermaidKinds.Setting, SequenceRoles.Word)),
                                                  items.Count)
                    {
                        Said = Piece(stated, SequenceRoles.Space),
                        Word = Worded(stated, SequenceRoles.Word),
                        Colour = Coloured(stated),
                        Parent = inside,
                        Whole = new SourceSpan(stated.Start, stated.End - stated.Start),
                    });
                    open.Push(new Opened(framed, items.Count - 1, Box: false));
                    break;

                case SequenceKinds.Section when inside is not null:
                    items.Add(new SequenceDivider(stated, inside, items.Count) { Said = Piece(stated, SequenceRoles.Space) });
                    break;

                // The end closing one is where the whole of it stops, which is what a press on the room round it means.
                case SequenceKinds.Ends when open.Count > 0:
                    var shut = open.Pop();

                    if (shut.Box)
                    {
                        boxes[shut.At] = boxes[shut.At] with { Whole = Spanned(boxes[shut.At].Part, stated) };
                    }
                    else if (items[shut.At] is SequenceOpening opening)
                    {
                        items[shut.At] = opening with { Whole = Spanned(opening.Part, stated) };
                        items.Add(new SequenceClosing(stated, shut.Key, items.Count));
                    }

                    break;

                case SequenceKinds.Link:
                    Leading(stated, made, known);
                    break;

                case SequenceKinds.Menu:
                    Menued(stated, made, known);
                    break;
            }
        }

        return new SequenceDiagram(block, config, [.. made.Select(Frozen)], items, boxes);
    }

    // ── What each line says ─────────────────────────────────────────────────

    /// <summary>A participant declared: what it is drawn as, the label drawn instead of its name, and the box it is in.</summary>
    private static void Standing(ContentPart stated, string? inside, List<SequenceBox> boxes, List<Made> made,
                                 Dictionary<string, Made> known)
    {
        if (Gathered(stated.Inner(SequenceKinds.Named), made, known) is not { } one) return;

        one.Kind = Typed(Meta(stated, Kinded)?.Text ?? Said(stated, MermaidKinds.Key, SequenceRoles.Word));
        one.Created |= stated.Kind == SequenceKinds.Created;

        if (Meta(stated, Aliased) is { Length: > 0 } alias) one.Said = alias;

        // What is written after 'as' is what the reader chose to see, so it wins over the alias in the metadata.
        if (Piece(stated, SequenceRoles.Label) is { Length: > 0 } label) one.Said = label;

        if (inside is not null && boxes.Any(box => string.Equals(box.Key, inside, StringComparison.Ordinal)))
            one.Box ??= inside;
    }

    /// <summary>A message: the participants at each end, what it is drawn with, and what it says.</summary>
    private static void Messaged(ContentPart stated, List<Made> made, Dictionary<string, Made> known, List<SequenceItem> items)
    {
        var named = stated.SelfAndDescendants().Where(part => part.Kind == SequenceKinds.Named).ToList();
        if (named.Count < 2) return;

        if (Gathered(named[0], made, known) is not { } from) return;
        if (Gathered(named[1], made, known) is not { } to) return;

        from.Reached = true;
        to.Reached = true;

        var arrow = stated.SelfAndDescendants().FirstOrDefault(part => part.Role == SequenceRoles.Arrow);
        var (dotted, near, far) = Ended(arrow?.Text);
        var centres = stated.SelfAndDescendants().Where(part => part.Role == SequenceRoles.Centre).ToList();
        var turns = stated.SelfAndDescendants().FirstOrDefault(part => part.Role == SequenceRoles.Turns)?.Text;
        var says = stated.Inner(SequenceKinds.Said);

        items.Add(new SequenceMessage(stated, from.Id, to.Id, items.Count)
        {
            Said = says.Words() is { Length: > 0 } words ? words : null,
            SaidHole = says.Hole(),
            Dotted = dotted,
            Near = near,
            Far = far,
            FromCentre = arrow is not null && centres.Any(centre => centre.Start < arrow.Start),
            ToCentre = arrow is not null && centres.Any(centre => centre.Start > arrow.Start),
            Starts = turns == "+",
            Stops = turns == "-",
            Number = Keyed(stated.Fact(SequenceRoles.Number)),
        });
    }

    /// <summary>A note and the participants it is drawn against.</summary>
    private static void Noted(ContentPart stated, List<Made> made, Dictionary<string, Made> known, List<SequenceItem> items)
    {
        var over = new List<string>();

        foreach (var named in stated.SelfAndDescendants().Where(part => part.Kind == SequenceKinds.Named))
        {
            if (Gathered(named, made, known) is not { } one) continue;

            one.Reached = true;
            over.Add(one.Id);
        }

        if (over.Count == 0) return;

        var says = stated.Inner(SequenceKinds.Said);

        items.Add(new SequenceNote(stated, Placed(Said(stated, MermaidKinds.Setting, SequenceRoles.Place)), over, items.Count)
        {
            Said = says.Words() is { Length: > 0 } words ? words : null,
            SaidHole = says.Hole(),
        });
    }

    /// <summary>A <c>link</c> line, which gives a participant one place to lead.</summary>
    private static void Leading(ContentPart stated, List<Made> made, Dictionary<string, Made> known)
    {
        if (Gathered(stated.Inner(SequenceKinds.Named), made, known) is not { } one) return;
        if (Piece(stated, SequenceRoles.Url) is not { Length: > 0 } url) return;

        one.Links.Add(new SequenceLink(stated, Piece(stated, SequenceRoles.Menu), url.Text));
    }

    /// <summary>
    /// A <c>links</c> line, which gives it several at once. A <c>properties</c> or <c>details</c> line names a participant the
    /// same way and says something about it that is not a place to lead, so it is read and kept and nothing is drawn for it.
    /// </summary>
    private static void Menued(ContentPart stated, List<Made> made, Dictionary<string, Made> known)
    {
        if (Gathered(stated.Inner(SequenceKinds.Named), made, known) is not { } one) return;
        if (!string.Equals(Said(stated, MermaidKinds.Key, SequenceRoles.Word), SequenceGrammar.LinksWord,
                           StringComparison.OrdinalIgnoreCase)) return;

        foreach (var property in stated.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Property))
        {
            var said = property.SelfAndDescendants()
                               .FirstOrDefault(part => part.Kind == MermaidKinds.Words && part.Role == SequenceRoles.Menu);
            var url = property.SelfAndDescendants()
                              .FirstOrDefault(part => part.Kind == MermaidKinds.Words && part.Role == SequenceRoles.Url);

            if (url is { Length: > 0 }) one.Links.Add(new SequenceLink(property, said, url.Text));
        }
    }

    /// <summary>
    /// The participant a name names: the one already made where it names it again, and otherwise a new one. A name with nothing
    /// written in it yet is a participant of its own, known by where it is written, so writing it is watched as it is typed.
    /// </summary>
    private static Made? Gathered(ContentPart? named, List<Made> made, Dictionary<string, Made> known)
    {
        if (named is not { } holder) return null;

        if (holder.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name
                                                    && child.Role == SequenceRoles.Id) is not { } name) return null;

        var words = name.Words();
        var hole = name.Hole();
        if (words is not { Length: > 0 } && hole is null) return null;

        var id = words is { Length: > 0 } said ? said.Text : Unwritten + name.Start;

        if (!known.TryGetValue(id, out var one))
        {
            one = new Made(name, id, made.Count) { Said = words, SaidHole = hole };

            made.Add(one);
            known[id] = one;
        }

        return one;
    }

    // ── What the pieces say ─────────────────────────────────────────────────

    private static bool Turned(ContentPart stated) =>
        !(Said(stated, MermaidKinds.Key, SequenceRoles.Turns)?.StartsWith("de", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>What a participant's metadata sets a key to, or null where it sets none.</summary>
    private static ContentPart? Meta(ContentPart stated, string key) =>
        stated.SelfAndDescendants()
              .Where(part => part.Kind == MermaidKinds.Property
                             && string.Equals(Named(part, SequenceRoles.Key), key, StringComparison.OrdinalIgnoreCase))
              .Select(part => part.SelfAndDescendants()
                                  .FirstOrDefault(said => said.Kind == MermaidKinds.Words && said.Role == SequenceRoles.Type))
              .FirstOrDefault(said => said is { Length: > 0 });

    /// <summary>The keys a participant's metadata sets.</summary>
    private const string Kinded = "type";
    private const string Aliased = "alias";

    private static string? Named(ContentPart property, string role) =>
        property.SelfAndDescendants()
                .FirstOrDefault(part => part.Kind == MermaidKinds.Words && part.Role == role)?.Text;

    private static ContentPart? Piece(ContentPart stated, string role) =>
        stated.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidKinds.Words && part.Role == role);

    /// <summary>The word a line was opened with, as it was written.</summary>
    private static ContentPart? Worded(ContentPart stated, string role) =>
        stated.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidKinds.Setting && part.Role == role);

    private static string? Said(ContentPart stated, string kind, string role) =>
        stated.SelfAndDescendants().FirstOrDefault(part => part.Kind == kind && part.Role == role)?.Text;

    private static ContentPart? Spaced(ContentPart stated) =>
        Piece(stated, SequenceRoles.Space) is { Length: > 0 } said ? said : null;

    private static string? Coloured(ContentPart stated) => Piece(stated, SequenceRoles.Colour)?.Text;

    private static SourceSpan Spanned(ContentPart from, ContentPart to) => new(from.Start, to.End - from.Start);

    private static string? Keyed(string? fact) => string.IsNullOrEmpty(fact) ? null : fact;

    private static SequenceParticipant Frozen(Made one) =>
        new(one.Part, one.Id, one.Order)
        {
            Said = one.Said,
            SaidHole = one.SaidHole,
            Kind = one.Kind,
            Created = one.Created,
            Destroyed = one.Destroyed,
            Box = one.Box,
            Links = one.Links,
            Reached = one.Reached,
        };

    /// <summary>A box or a frame while the block is being read, before the <c>end</c> closing it has been seen.</summary>
    private sealed record Opened(string Key, int At, bool Box);

    /// <summary>One participant while the block is being read, before every line saying something about it is known.</summary>
    private sealed class Made(ContentPart part, string id, int order)
    {
        public ContentPart Part { get; } = part;

        public string Id { get; } = id;

        public int Order { get; } = order;

        public ContentPart? Said { get; set; }

        public ContentPart? SaidHole { get; set; }

        public SequenceKind Kind { get; set; }

        public bool Created { get; set; }

        public bool Destroyed { get; set; }

        public string? Box { get; set; }

        public bool Reached { get; set; }

        public List<SequenceLink> Links { get; } = [];
    }
}
