using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sequence;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Sequence;

/// <summary>What a sequence diagram draws, read down its tree in the order it is written.</summary>
internal partial class SequenceBuilder
{
    /// <summary>What a participant is drawn as, from <c>actor</c> or from the <c>type</c> its metadata names.</summary>
    internal enum Kind
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
    internal enum Tip
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
    internal enum Place { RightOf, LeftOf, Over }

    /// <summary>What a frame holds its messages under.</summary>
    internal enum Frame
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

    /// <summary>
    /// What a participant's box holds beyond its name, where a diagram writes more than one thing for it: the line saying what
    /// kind of thing it is, and the sentence saying what it does. A participant written as itself has none of this, and is a name
    /// in a box.
    /// </summary>
    internal sealed record Card
    {
        /// <summary>What it is, drawn in brackets under the name — worked out from several things written, so pressed rather than typed into.</summary>
        public string? Stereotype { get; init; }

        /// <summary>What it is built with, which the stereotype says as well — kept so a press on it means what was written.</summary>
        public ContentPart? Technology { get; init; }

        /// <summary>The sentence under it, where the diagram asks for descriptions.</summary>
        public ContentPart? Said { get; init; }

        public DiagramCardShape Shape { get; init; }

        /// <summary>
        /// Which band of the diagram's own grading the card takes, where a diagram grades its cards — a C4 sequence does, by the
        /// abstraction each element sits at. Null where the diagram grades nothing, and the theme's own surface is the answer.
        /// </summary>
        public int? Tone { get; init; }

        /// <summary>What it is filled, written in and outlined with, where anything says — otherwise the theme's own.</summary>
        public string? Fill { get; init; }
        public string? Ink { get; init; }
        public string? Border { get; init; }
    }

    /// <summary>One row of the key drawn under a diagram that asks for one: what it says, and the colours it stands for.</summary>
    internal sealed record Legend(string Says, string? Fill, string? Border)
    {
        /// <summary>Which band of the diagram's own grading the row's swatch takes, where nothing is written for it.</summary>
        public int? Tone { get; init; }
    }

    /// <summary>Somewhere a participant leads, from a <c>link</c> line or one of the several a <c>links</c> line gives.</summary>
    /// <param name="Part">What it was written as, which is what a press on it means.</param>
    internal sealed record Link(ContentPart Part, ContentPart? Said, string Url);

    /// <summary>
    /// One lifeline: where it was first written, what it is called, and what it is drawn as. A participant written twice is one
    /// participant — the second writing says more about the one the first made.
    /// </summary>
    /// <param name="Part">The name as it was first written, which is what a press on it means.</param>
    /// <param name="Id">What it is called, which is what a message and a note name it by.</param>
    internal sealed class Participant(ContentPart part, string id)
    {
        public ContentPart Part { get; } = part;

        public string Id { get; } = id;

        /// <summary>The words drawn for it: the label <c>as</c> or an <c>alias</c> gives, and otherwise its name.</summary>
        public ContentPart? Said { get; set; }

        /// <summary>The hole standing where its name goes.</summary>
        public ContentPart? SaidHole { get; set; }

        public Kind Kind { get; set; }

        /// <summary>Whether its lifeline starts partway down, at the message that makes it.</summary>
        public bool Created { get; set; }

        /// <summary>Whether its lifeline stops partway down, with a cross where it ends.</summary>
        public bool Destroyed { get; set; }

        /// <summary>The box it is grouped in, or null for one written outside them all.</summary>
        public string? Box { get; set; }

        /// <summary>Where it leads, in the order written.</summary>
        public List<Link> Links { get; } = [];

        /// <summary>What its box holds beyond its name, where the diagram writes more than one thing for it.</summary>
        public Card? Card { get; set; }

        /// <summary>Whether anything on the timeline names it, which is what <c>hideUnusedParticipants</c> asks about.</summary>
        public bool Reached { get; set; }
    }

    /// <summary>Anything on the timeline, in the order it was written.</summary>
    /// <param name="Part">What it was written as, which is what a press on it means.</param>
    internal abstract record Item(ContentPart Part, int Order);

    /// <summary>One message between two lifelines, or from one to itself.</summary>
    internal sealed record Message(ContentPart Part, string From, string To, int Order) : Item(Part, Order)
    {
        /// <summary>What it says, drawn over the line.</summary>
        public ContentPart? Said { get; init; }

        public ContentPart? SaidHole { get; init; }

        /// <summary>Whether it is drawn dotted, which is what a message written with two dashes says.</summary>
        public bool Dotted { get; init; }

        /// <summary>What it draws at the end it leaves, and at the end it reaches.</summary>
        public Tip Near { get; init; }
        public Tip Far { get; init; }

        /// <summary>Whether <c>()</c> runs an end to the middle of its lifeline rather than to the edge of its box.</summary>
        public bool FromCentre { get; init; }
        public bool ToCentre { get; init; }

        /// <summary>Whether <c>+</c> starts a bar on what it reaches, and <c>-</c> ends the one on what it leaves.</summary>
        public bool Starts { get; init; }
        public bool Stops { get; init; }

        /// <summary>The number its stages gave it, or null where nothing numbers it.</summary>
        public string? Number { get; init; }

        /// <summary>
        /// The smaller lines drawn under what it says: what it is done with, and what it is for — a C4 relationship's technology
        /// and its description. A message written as a sequence diagram's own says everything on the one line and has none.
        /// </summary>
        public IReadOnlyList<ContentPart> Under { get; init; } = [];

        /// <summary>What its line and its words are drawn in, where anything says — otherwise the theme's own.</summary>
        public string? Ink { get; init; }
        public string? SaidInk { get; init; }

        /// <summary>Whether it goes from a participant to itself, which is drawn as a loop off its own lifeline.</summary>
        public bool Self => string.Equals(From, To, StringComparison.Ordinal);
    }

    /// <summary>One note, beside a lifeline or spanning several.</summary>
    internal sealed record Note(ContentPart Part, Place Place, IReadOnlyList<string> Over, int Order) : Item(Part, Order)
    {
        public ContentPart? Said { get; init; }

        public ContentPart? SaidHole { get; init; }
    }

    /// <summary>A bar started or ended on a lifeline by an <c>activate</c> or <c>deactivate</c> line.</summary>
    internal sealed record Turn(ContentPart Part, string Id, bool On, int Order) : Item(Part, Order);

    /// <summary>Where a lifeline ends, from a <c>destroy</c> line.</summary>
    internal sealed record Gone(ContentPart Part, string Id, int Order) : Item(Part, Order);

    /// <summary>Where a frame opens, and everything about it but how far down it reaches.</summary>
    /// <param name="Key">What the lines closing it and dividing it name it by.</param>
    internal sealed record Opening(ContentPart Part, string Key, Frame Kind, int Order) : Item(Part, Order)
    {
        /// <summary>What it holds its messages under — the condition an <c>alt</c> is drawn for.</summary>
        public ContentPart? Said { get; init; }

        /// <summary>The word that opened it, as it was written, which is what is drawn in its tab.</summary>
        public ContentPart? Word { get; init; }

        /// <summary>What a <c>rect</c> is washed with, where a colour is written.</summary>
        public string? Colour { get; init; }

        /// <summary>The box or frame it is written inside, or null for one written outside them all.</summary>
        public string? Parent { get; init; }

        /// <summary>The whole of it as written, from the line that opened it through the <c>end</c> that closed it.</summary>
        public ISourcePart Whole { get; init; } = default(SourceSpan);
    }

    /// <summary>A line dividing the frame it is written in: an <c>else</c>, an <c>and</c> or an <c>option</c>.</summary>
    internal sealed record Divider(ContentPart Part, string Key, int Order) : Item(Part, Order)
    {
        public ContentPart? Said { get; init; }
    }

    /// <summary>Where a frame closes.</summary>
    internal sealed record Closing(ContentPart Part, string Key, int Order) : Item(Part, Order);

    /// <summary>One box: the tint drawn behind a run of participants.</summary>
    internal sealed record Box(ContentPart Part, string Key, ContentPart? Said, string? Colour, int Order)
    {
        /// <summary>
        /// The box this one was written inside, or null for one written outside them all. A sequence diagram's own boxes never
        /// nest; a C4 boundary does, and then the outer one is drawn round everything the inner one holds.
        /// </summary>
        public string? Parent { get; init; }

        /// <summary>The whole of it as written, from the line that opened it through the <c>end</c> that closed it.</summary>
        public ISourcePart Whole { get; init; } = default(SourceSpan);
    }

    /// <summary>
    /// A sequence diagram as its lines say it: the participants along the top, everything on the timeline in the order written,
    /// and the boxes grouping the participants. How deep each thing ends up and what boxes it is inside are the layout's.
    /// </summary>
    internal sealed class Diagram(SequenceConfig config, IReadOnlyList<Participant> participants, IReadOnlyList<Item> items,
                                  IReadOnlyList<Box> boxes, IReadOnlyList<Legend> legend)
    {
        /// <summary>What the front matter asks for.</summary>
        public SequenceConfig Config { get; } = config;

        /// <summary>The participants, in the order they are first written.</summary>
        public IReadOnlyList<Participant> Participants { get; } = participants;

        /// <summary>Everything on the timeline, in the order written.</summary>
        public IReadOnlyList<Item> Items { get; } = items;

        /// <summary>The boxes grouping the participants, in the order written.</summary>
        public IReadOnlyList<Box> Boxes { get; } = boxes;

        /// <summary>The rows of the key drawn under it, where the diagram asks for one — none for a sequence diagram's own.</summary>
        public IReadOnlyList<Legend> Legend { get; } = legend;

        /// <summary>The messages, which is what a sequence diagram is mostly made of.</summary>
        public IEnumerable<Message> Messages => Items.OfType<Message>();

        /// <summary>The participants drawn: all of them, or only those something names where the front matter asks.</summary>
        public IEnumerable<Participant> Drawn => Config.HideUnused ? Participants.Where(one => one.Reached) : Participants;

        public Participant? Find(string id) =>
            Participants.FirstOrDefault(one => string.Equals(one.Id, id, StringComparison.Ordinal));
    }

    /// <summary>What the block draws, as its reader reads it off the tree.</summary>
    protected virtual Diagram Read(ContentPart root, SequenceConfig config) => new Reader().Read(root, config);

    /// <summary>
    /// Reads a block's lines onto a timeline, down the tree in the order they are written: each box and frame its stages gathered
    /// with the lines written in it, opened where it starts and closed by the line ending it.
    ///
    /// <para>
    /// A diagram written in another language reads its own lines onto this very timeline by claiming them
    /// (<see cref="Claims"/>), and says what else opens a box and closes one — which is how a C4 sequence's macros sit on a
    /// sequence diagram's own timeline, with <c>alt</c>, <c>note over</c> and <c>activate</c> read here rather than twice.
    /// </para>
    /// </summary>
    protected class Reader
    {
        /// <summary>What a name with nothing written in it yet is known by, which is where it was written.</summary>
        private const string Unwritten = " ";

        private readonly Dictionary<string, Participant> known = new(StringComparer.Ordinal);
        private int groups;

        protected List<Participant> Participants { get; } = [];

        protected List<Item> Items { get; } = [];

        protected List<Box> Boxes { get; } = [];

        protected List<Legend> Legend { get; } = [];

        public Diagram Read(ContentPart root, SequenceConfig config)
        {
            Walk(root.Children, null);
            return new Diagram(config, Participants, Items, Boxes, Legend);
        }

        /// <summary>Whether a line is the other language's, having read it where it is — false for one of this grammar's own.</summary>
        /// <param name="inside">The box or frame it is written in.</param>
        protected virtual bool Claims(ContentPart stated, string? inside) => false;

        /// <summary>Whether a line of this kind closes the box or frame it ends.</summary>
        protected virtual bool Closes(string kind) => kind == SequenceKinds.Ends;

        /// <summary>
        /// What a line opening a box or a frame puts on the timeline — false where it opens neither, and what it holds is read as
        /// though written outside it.
        /// </summary>
        protected virtual bool Opens(ContentPart stated, string key, string? inside, ISourcePart whole)
        {
            switch (stated.Kind)
            {
                case SequenceKinds.Box:
                    Boxed(stated, key, Spaced(stated), Coloured(stated), inside, whole);
                    return true;

                case SequenceKinds.Frame:
                    Items.Add(new Opening(stated, key, Framed(Said(stated, MermaidKinds.Setting, SequenceRoles.Word)), Items.Count)
                    {
                        Said = Piece(stated, SequenceRoles.Space),
                        Word = stated.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidKinds.Setting && part.Role == SequenceRoles.Word),
                        Colour = Coloured(stated),
                        Parent = inside,
                        Whole = whole,
                    });
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>A box round the lifelines written in it, inside the box it is written in.</summary>
        protected void Boxed(ContentPart stated, string key, ContentPart? said, string? colour, string? inside, ISourcePart whole) =>
            Boxes.Add(new Box(stated, key, said, colour, Boxes.Count) { Parent = Holds(inside) ? inside : null, Whole = whole });

        /// <summary>Whether a box or frame is a box.</summary>
        protected bool Holds(string? key) => key is not null && Boxes.Any(box => string.Equals(box.Key, key, StringComparison.Ordinal));

        /// <summary>
        /// The participant an id names where the id is not written as a name of its own — a C4 macro's first argument, which is
        /// one word of several in a call rather than a whole name.
        /// </summary>
        protected Participant Called(ContentPart part, string id)
        {
            if (known.TryGetValue(id, out var one)) return one;

            one = new Participant(part, id);
            Participants.Add(one);
            known[id] = one;

            return one;
        }

        /// <summary>
        /// The participant a name names: the one already made where it names it again, and otherwise a new one. A name with
        /// nothing written in it yet is a participant of its own, known by where it is written, so writing it is watched as it is typed.
        /// </summary>
        protected Participant? Gathered(ContentPart? named)
        {
            if (named?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name && child.Role == SequenceRoles.Id) is not { } name) return null;

            var words = name.Words();
            var hole = name.Hole();
            if (words is not { Length: > 0 } && hole is null) return null;

            var id = words is { Length: > 0 } said ? said.Text : Unwritten + name.Start;
            if (known.TryGetValue(id, out var one)) return one;

            one = new Participant(name, id) { Said = words, SaidHole = hole };
            Participants.Add(one);
            known[id] = one;

            return one;
        }

        /// <summary>Everything written in one stretch of the block, inside the box or frame given.</summary>
        private void Walk(IEnumerable<ContentPart> parts, string? inside)
        {
            foreach (var part in parts)
            {
                if (part.Kind == MermaidKinds.Group)
                {
                    Grouped(part, inside);
                    continue;
                }

                if (part.Stated() is not { } stated || Claims(stated, inside)) continue;

                Line(stated, inside);
            }
        }

        /// <summary>
        /// A box or a frame and everything written in it: known by where it stands among those written, and reaching from the line
        /// opening it through the one closing it — or the opening line alone, where nothing closes it.
        /// </summary>
        private void Grouped(ContentPart group, string? inside)
        {
            if (group.Children.FirstOrDefault()?.Stated() is not { } opening)
            {
                Walk(group.Children, inside);
                return;
            }

            var key = (groups++).ToString(CultureInfo.InvariantCulture);
            var ending = group.Children.Count > 1 && group.Children[^1].Stated() is { } last && Closes(last.Kind) ? group.Children[^1] : null;
            var closing = ending?.Stated();
            var whole = new SourceSpan(opening.Start, (closing ?? opening).End - opening.Start);

            var opened = Opens(opening, key, inside, whole);
            Walk(group.Children.Skip(1).Where(child => !ReferenceEquals(child, ending)), opened ? key : inside);

            // The end closing a frame is where the whole of it stops, which is what a press on the room round it means.
            if (opened && closing is not null && !Holds(key)) Items.Add(new Closing(closing, key, Items.Count));
        }

        /// <summary>One line of the sequence diagram's own, and what it puts on the timeline.</summary>
        private void Line(ContentPart stated, string? inside)
        {
            switch (stated.Kind)
            {
                case SequenceKinds.Participant:
                case SequenceKinds.Created:
                    Standing(stated, inside);
                    break;

                case SequenceKinds.Destroyed:
                    if (Gathered(stated.Inner(SequenceKinds.Named)) is { } going)
                    {
                        going.Destroyed = true;
                        going.Reached = true;
                        Items.Add(new Gone(stated, going.Id, Items.Count));
                    }

                    break;

                case SequenceKinds.Activation:
                    if (Gathered(stated.Inner(SequenceKinds.Named)) is { } turning)
                    {
                        turning.Reached = true;
                        Items.Add(new Turn(stated, turning.Id, Turned(stated), Items.Count));
                    }

                    break;

                case SequenceKinds.Message:
                    Messaged(stated);
                    break;

                case SequenceKinds.Note:
                    Noted(stated);
                    break;

                case SequenceKinds.Section when inside is not null:
                    Items.Add(new Divider(stated, inside, Items.Count) { Said = Piece(stated, SequenceRoles.Space) });
                    break;

                case SequenceKinds.Link:
                    Leading(stated);
                    break;

                case SequenceKinds.Menu:
                    Menued(stated);
                    break;
            }
        }

        /// <summary>A participant declared: what it is drawn as, the label drawn instead of its name, and the box it is in.</summary>
        private void Standing(ContentPart stated, string? inside)
        {
            if (Gathered(stated.Inner(SequenceKinds.Named)) is not { } one) return;

            one.Kind = Typed(Meta(stated, Kinded)?.Text ?? Said(stated, MermaidKinds.Key, SequenceRoles.Word));
            one.Created |= stated.Kind == SequenceKinds.Created;

            if (Meta(stated, Aliased) is { Length: > 0 } alias) one.Said = alias;

            // What is written after 'as' is what the reader chose to see, so it wins over the alias in the metadata.
            if (Piece(stated, SequenceRoles.Label) is { Length: > 0 } label) one.Said = label;

            if (Holds(inside)) one.Box ??= inside;
        }

        /// <summary>A message: the participants at each end, what it is drawn with, and what it says.</summary>
        private void Messaged(ContentPart stated)
        {
            var named = stated.SelfAndDescendants().Where(part => part.Kind == SequenceKinds.Named).ToList();
            if (named.Count < 2) return;

            if (Gathered(named[0]) is not { } from) return;
            if (Gathered(named[1]) is not { } to) return;

            from.Reached = true;
            to.Reached = true;

            var arrow = stated.SelfAndDescendants().FirstOrDefault(part => part.Role == SequenceRoles.Arrow);
            var (dotted, near, far) = Ended(arrow?.Text);
            var centres = stated.SelfAndDescendants().Where(part => part.Role == SequenceRoles.Centre).ToList();
            var turns = stated.SelfAndDescendants().FirstOrDefault(part => part.Role == SequenceRoles.Turns)?.Text;
            var says = stated.Inner(SequenceKinds.Said);

            Items.Add(new Message(stated, from.Id, to.Id, Items.Count)
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
                Number = stated.Fact(SequenceRoles.Number) is { Length: > 0 } number ? number : null,
            });
        }

        /// <summary>A note and the participants it is drawn against.</summary>
        private void Noted(ContentPart stated)
        {
            var over = new List<string>();

            foreach (var named in stated.SelfAndDescendants().Where(part => part.Kind == SequenceKinds.Named))
            {
                if (Gathered(named) is not { } one) continue;

                one.Reached = true;
                over.Add(one.Id);
            }

            if (over.Count == 0) return;

            var says = stated.Inner(SequenceKinds.Said);

            Items.Add(new Note(stated, Placed(Said(stated, MermaidKinds.Setting, SequenceRoles.Place)), over, Items.Count)
            {
                Said = says.Words() is { Length: > 0 } words ? words : null,
                SaidHole = says.Hole(),
            });
        }

        /// <summary>A <c>link</c> line, which gives a participant one place to lead.</summary>
        private void Leading(ContentPart stated)
        {
            if (Gathered(stated.Inner(SequenceKinds.Named)) is not { } one) return;
            if (Piece(stated, SequenceRoles.Url) is not { Length: > 0 } url) return;

            one.Links.Add(new Link(stated, Piece(stated, SequenceRoles.Menu), url.Text));
        }

        /// <summary>
        /// A <c>links</c> line, which gives it several at once. A <c>properties</c> or <c>details</c> line names a participant the
        /// same way and says something about it that is not a place to lead, so it is read and nothing is drawn for it.
        /// </summary>
        private void Menued(ContentPart stated)
        {
            if (Gathered(stated.Inner(SequenceKinds.Named)) is not { } one) return;
            if (!string.Equals(Said(stated, MermaidKinds.Key, SequenceRoles.Word), SequenceGrammar.LinksWord, StringComparison.OrdinalIgnoreCase)) return;

            foreach (var property in stated.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Property))
            {
                var said = property.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidKinds.Words && part.Role == SequenceRoles.Menu);
                var url = property.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidKinds.Words && part.Role == SequenceRoles.Url);

                if (url is { Length: > 0 }) one.Links.Add(new Link(property, said, url.Text));
            }
        }

        private static bool Turned(ContentPart stated) =>
            !(Said(stated, MermaidKinds.Key, SequenceRoles.Turns)?.StartsWith("de", StringComparison.OrdinalIgnoreCase) ?? false);

        /// <summary>The keys a participant's metadata sets.</summary>
        private const string Kinded = "type";
        private const string Aliased = "alias";

        /// <summary>What a participant's metadata sets a key to, or null where it sets none.</summary>
        private static ContentPart? Meta(ContentPart stated, string key) =>
            stated.SelfAndDescendants()
                  .Where(part => part.Kind == MermaidKinds.Property
                                 && string.Equals(part.SelfAndDescendants().FirstOrDefault(said => said.Kind == MermaidKinds.Words && said.Role == SequenceRoles.Key)?.Text,
                                                  key, StringComparison.OrdinalIgnoreCase))
                  .Select(part => part.SelfAndDescendants().FirstOrDefault(said => said.Kind == MermaidKinds.Words && said.Role == SequenceRoles.Type))
                  .FirstOrDefault(said => said is { Length: > 0 });

        private static ContentPart? Piece(ContentPart stated, string role) =>
            stated.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidKinds.Words && part.Role == role);

        private static string? Said(ContentPart stated, string kind, string role) =>
            stated.SelfAndDescendants().FirstOrDefault(part => part.Kind == kind && part.Role == role)?.Text;

        private static ContentPart? Spaced(ContentPart stated) =>
            Piece(stated, SequenceRoles.Space) is { Length: > 0 } said ? said : null;

        private static string? Coloured(ContentPart stated) => Piece(stated, SequenceRoles.Colour)?.Text;
    }

    /// <summary>What a participant is drawn as, from the word it was declared with or the <c>type</c> its metadata names.</summary>
    private static Kind Typed(string? said) => said?.Trim().ToLowerInvariant() switch
    {
        "actor" => Kind.Actor,
        "boundary" => Kind.Boundary,
        "control" => Kind.Control,
        "entity" => Kind.Entity,
        "database" => Kind.Database,
        "collections" => Kind.Collections,
        "queue" => Kind.Queue,
        _ => Kind.Participant,
    };

    /// <summary>Where a note sits, from the words written before the participants it names.</summary>
    private static Place Placed(string? said) => said?.Trim().ToLowerInvariant() switch
    {
        "left of" => Place.LeftOf,
        "right of" => Place.RightOf,
        _ => Place.Over,
    };

    /// <summary>What kind of frame a word opens.</summary>
    private static Frame Framed(string? said) => said?.Trim().ToLowerInvariant() switch
    {
        "opt" => Frame.Opt,
        "loop" => Frame.Loop,
        "par" => Frame.Par,
        "critical" => Frame.Critical,
        "break" => Frame.Break,
        "rect" => Frame.Rect,
        _ => Frame.Alt,
    };

    /// <summary>
    /// What the characters a message is drawn with say: whether its line is dotted, and what it draws at the end it leaves and
    /// the end it reaches. A head is written at the end it is drawn at, so <c>-|\</c> draws half a head where it arrives and
    /// <c>/|-</c> draws one where it set out.
    /// </summary>
    private static (bool Dotted, Tip Near, Tip Far) Ended(string? arrow) => arrow switch
    {
        "->" => (false, Tip.None, Tip.None),
        "-->" => (true, Tip.None, Tip.None),
        "->>" => (false, Tip.None, Tip.Arrow),
        "-->>" => (true, Tip.None, Tip.Arrow),
        "<<->>" => (false, Tip.Arrow, Tip.Arrow),
        "<<-->>" => (true, Tip.Arrow, Tip.Arrow),
        "-x" => (false, Tip.None, Tip.Cross),
        "--x" => (true, Tip.None, Tip.Cross),
        "-)" => (false, Tip.None, Tip.Open),
        "--)" => (true, Tip.None, Tip.Open),
        "-|\\" => (false, Tip.None, Tip.HalfTop),
        "--|\\" => (true, Tip.None, Tip.HalfTop),
        "-|/" => (false, Tip.None, Tip.HalfBottom),
        "--|/" => (true, Tip.None, Tip.HalfBottom),
        "/|-" => (false, Tip.HalfTop, Tip.None),
        "/|--" => (true, Tip.HalfTop, Tip.None),
        "\\|-" => (false, Tip.HalfBottom, Tip.None),
        "\\|--" => (true, Tip.HalfBottom, Tip.None),
        "-\\\\" => (false, Tip.None, Tip.StickTop),
        "--\\\\" => (true, Tip.None, Tip.StickTop),
        "-//" => (false, Tip.None, Tip.StickBottom),
        "--//" => (true, Tip.None, Tip.StickBottom),
        "//-" => (false, Tip.StickTop, Tip.None),
        "//--" => (true, Tip.StickTop, Tip.None),
        "\\\\-" => (false, Tip.StickBottom, Tip.None),
        "\\\\--" => (true, Tip.StickBottom, Tip.None),
        _ => (false, Tip.None, Tip.Arrow),
    };
}
