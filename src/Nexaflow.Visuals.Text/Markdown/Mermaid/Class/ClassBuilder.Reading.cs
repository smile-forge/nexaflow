using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Class;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Class;

/// <summary>
/// What a class diagram is drawn from — the classes, their members, the relations between them, the namespaces they are boxed
/// into and the notes beside them — and how a <c>classDiagram</c> block's tree is walked into it. Another builder drawing the
/// same thing from a tree of its own walks that tree instead (<see cref="Read"/>).
/// </summary>
internal partial class ClassBuilder
{
    // ── What is drawn ───────────────────────────────────────────────────────

    /// <summary>One member of a class: where it was written, and what it draws.</summary>
    /// <param name="Says">What is drawn for it.</param>
    /// <param name="Method">Whether it is drawn in the band under the fields.</param>
    protected sealed record Member(ContentPart Part, string Says, bool Method)
    {
        /// <summary>Whether it is drawn underlined, as something the class holds rather than anything made of it.</summary>
        public bool Fixed { get; init; }

        /// <summary>Whether it is drawn in italics.</summary>
        public bool Abstract { get; init; }

        /// <summary>Where pressing it leads.</summary>
        public string? Href { get; init; }

        /// <summary>The hole standing where it goes, where nothing is written there yet.</summary>
        public ContentPart? Hole { get; init; }

        /// <summary>Whether what is drawn is the characters written, which is what lets a caret stand in it.</summary>
        public bool Written { get; init; } = true;
    }

    /// <summary>One interface a class offers, drawn as a circle on a stub off it rather than as a class of its own.</summary>
    /// <param name="Part">The interface's name as it was written, which is what a press on the circle means.</param>
    /// <param name="Below">Whether it hangs under the class rather than sitting above it.</param>
    protected sealed record Lollipop(ContentPart Part, string Name, bool Below);

    /// <summary>One class: where it was first written, what it is called, what is drawn on it, and what it holds.</summary>
    /// <param name="part">What a press on its name means.</param>
    /// <param name="id">What it is called, which is what a relation, a note and a styling line name it by.</param>
    protected sealed class Node(ContentPart part, string id)
    {
        public ContentPart Part { get; } = part;

        public string Id { get; } = id;

        /// <summary>The key of the namespace it was first written in, or null for one written outside them all.</summary>
        public string? Group { get; set; }

        /// <summary>The words drawn for its name: its label where one is written, and otherwise its id.</summary>
        public ContentPart? Said { get; set; }

        public ContentPart? SaidHole { get; set; }

        /// <summary>The type parameters written after its name, drawn between angle brackets after it.</summary>
        public string? Generic { get; set; }

        /// <summary>What it says it is — <c>interface</c> — drawn in guillemets over its name.</summary>
        public ContentPart? Kind { get; set; }

        public List<Member> Members { get; } = [];

        public List<Lollipop> Lollipops { get; } = [];

        /// <summary>The whole of it as it was written, which is what a press on its box means.</summary>
        public ISourcePart Whole { get; set; } = default(SourceSpan);

        /// <summary>Where pressing it leads, and what it says while pointed at.</summary>
        public string? Href { get; set; }

        public string? Tip { get; set; }

        public MermaidStyle Style { get; set; } = MermaidStyle.None;

        /// <summary>The members drawn above the line.</summary>
        public IEnumerable<Member> Fields => Members.Where(member => !member.Method);

        /// <summary>The members drawn below it.</summary>
        public IEnumerable<Member> Methods => Members.Where(member => member.Method);
    }

    /// <summary>One relation: the classes it joins, what each of its ends draws, and what is written on it.</summary>
    /// <param name="Dotted">Whether its line is drawn dotted.</param>
    protected sealed record Relation(ContentPart Part, string From, string To, DiagramHead Head, DiagramHead Tail, bool Dotted)
    {
        /// <summary>How many of the class at the near end the far one has, where a count is written there.</summary>
        public ContentPart? Near { get; init; }

        public ContentPart? Far { get; init; }

        /// <summary>What is written on it, over the middle of its line.</summary>
        public ContentPart? Said { get; init; }

        public ContentPart? SaidHole { get; init; }
    }

    /// <summary>One box drawn round the classes written inside it, and what is written at the top of it.</summary>
    /// <param name="Key">What a class says it is inside.</param>
    /// <param name="Name">What is drawn at the top of it, where no label is written.</param>
    /// <param name="Parent">The key of the box it is itself inside, or null.</param>
    protected sealed record Namespace(ContentPart Part, string Key, string Name, string? Parent)
    {
        /// <summary>The label drawn instead of its name, where one is written.</summary>
        public ContentPart? Said { get; init; }

        /// <summary>The whole of it as it was written, which is what a press on the room round its classes means.</summary>
        public ISourcePart Whole { get; init; } = default(SourceSpan);
    }

    /// <summary>One note: the class it is written beside — or empty, where it floats — and what it says.</summary>
    protected sealed record Note(ContentPart Part, string Of, ContentPart? Said, ContentPart? SaidHole);

    /// <summary>Everything a class diagram draws. A class written twice is one class: <see cref="Find"/> it before making another.</summary>
    protected sealed class Diagram(ClassConfig config)
    {
        private readonly Dictionary<string, Node> known = new(StringComparer.Ordinal);

        public ClassConfig Config { get; } = config;

        public DiagramWay Way { get; set; } = DiagramWay.Down;

        /// <summary>The classes, in the order they are first written.</summary>
        public List<Node> Nodes { get; } = [];

        public List<Relation> Relations { get; } = [];

        /// <summary>The namespaces, each before the ones nested in it.</summary>
        public List<Namespace> Spaces { get; } = [];

        public List<Note> Notes { get; } = [];

        public Node? Find(string id) => id.Length == 0 ? null : known.GetValueOrDefault(id);

        /// <summary>A class not written before, now written for the first time.</summary>
        public Node Made(ContentPart part, string id, string? group)
        {
            var node = new Node(part, id) { Group = group };

            Nodes.Add(node);
            known[id] = node;
            return node;
        }

        /// <summary>The classes written inside a namespace — those written outside them all, for null.</summary>
        public IEnumerable<Node> Inside(string? space) =>
            Nodes.Where(node => string.Equals(node.Group, space, StringComparison.Ordinal));

        /// <summary>The namespaces opened inside one — the outermost ones, for null.</summary>
        public IEnumerable<Namespace> Within(string? space) =>
            Spaces.Where(nested => string.Equals(nested.Parent, space, StringComparison.Ordinal));
    }

    /// <summary>
    /// What the block draws, walked down its tree in the order it is written — here, a <c>classDiagram</c>'s: each namespace its
    /// stages gathered with what is written in it, each member as its stages said it draws, and each class's style said on the
    /// name first writing it.
    /// </summary>
    protected virtual Diagram Read(ContentPart root, ClassConfig config) => new Lines(config).Read(root);

    /// <summary>A <c>classDiagram</c>'s lines, being walked.</summary>
    private sealed class Lines(ClassConfig config)
    {
        private readonly Diagram diagram = new(config);

        /// <summary>What each name is styled with, said on the one first writing it.</summary>
        private readonly Dictionary<string, MermaidStyle> styles = new(StringComparer.Ordinal);

        private readonly List<Held> held = [];

        public Diagram Read(ContentPart root)
        {
            Walk(root, null);

            foreach (var node in diagram.Nodes) node.Style = styles.GetValueOrDefault(node.Id, MermaidStyle.None);
            diagram.Spaces.AddRange(Boxed());

            return diagram;
        }

        /// <summary>Everything written in one part of the block — the whole of it, or one namespace — inside the namespace given.</summary>
        private void Walk(ContentPart holder, string? inside)
        {
            foreach (var part in holder.Children)
            {
                if (part.Kind == MermaidKinds.Group)
                {
                    if (part.Children.FirstOrDefault()?.Stated() is { Kind: ClassKinds.Namespace } opens)
                        Walk(part, Opened(part, opens, inside));

                    continue;
                }

                if (part.Stated() is not { } stated) continue;
                Styled(stated);

                switch (stated.Kind)
                {
                    case ClassKinds.Body:
                        Bodied(stated, inside);
                        break;

                    case ClassKinds.Class:
                        Declared(stated, inside);
                        break;

                    case ClassKinds.Says:
                        Membered(stated, inside);
                        break;

                    case ClassKinds.Annotation when Declared(stated, inside) is { } annotated:
                        annotated.Kind = Worded(stated, ClassRoles.Kind) ?? annotated.Kind;
                        break;

                    case ClassKinds.Relation:
                        Related(stated, inside);
                        break;

                    case ClassKinds.Note:
                        Noted(stated);
                        break;

                    case ClassKinds.Direction when Wayward(Setting(stated, ClassRoles.Towards)) is { } way:
                        diagram.Way = way;
                        break;

                    case ClassKinds.Click:
                        Clicked(stated);
                        break;
                }
            }
        }

        /// <summary>What each name a line writes is styled with, where it is the first writing of that name.</summary>
        private void Styled(ContentPart stated)
        {
            foreach (var named in stated.SelfAndDescendants().Where(part => part.Kind == ClassKinds.Named))
                if (Name(named) is { } name && name.Words()?.Text is { Length: > 0 } id)
                    styles.TryAdd(id, StyleOf(name));
        }

        /// <summary>A <c>class Foo { … }</c>: the class, and the members and annotation written between its braces.</summary>
        private void Bodied(ContentPart stated, string? inside)
        {
            if (stated.Inner(ClassKinds.Opens) is not { } opens || Declared(opens, inside) is not { } node) return;

            node.Whole = new SourceSpan(stated.Start, stated.End - stated.Start);

            foreach (var part in stated.SelfAndDescendants())
            {
                if (part.Kind == ClassKinds.Annotation && Worded(part, ClassRoles.Kind) is { } kind) node.Kind ??= kind;
                if (part.Kind == ClassKinds.Member && Member(part) is { } member) node.Members.Add(member);
            }
        }

        /// <summary>A <c>class</c> line: the class it declares, its label, its type parameters and its annotation.</summary>
        private Node? Declared(ContentPart stated, string? inside)
        {
            if (stated.Inner(ClassKinds.Named) is not { } named || Gathered(named, inside) is not { } node) return null;

            node.Said = stated.Inner(MermaidKinds.Label)?.Words() ?? node.Said;
            node.SaidHole = stated.Inner(MermaidKinds.Label)?.Hole() ?? node.SaidHole;
            node.Kind ??= Worded(stated, ClassRoles.Kind);

            return node;
        }

        /// <summary>A <c>Foo : +int age</c> line, which gives a class one member.</summary>
        private void Membered(ContentPart stated, string? inside)
        {
            if (Declared(stated, inside) is { } node && stated.Inner(ClassKinds.Member) is { } written && Member(written) is { } member)
                node.Members.Add(member);
        }

        /// <summary>One member as its stages said it draws — or the hole where one is still to be written.</summary>
        private static Member? Member(ContentPart written)
        {
            if (Worded(written, ClassRoles.Member) is { Length: > 0 } words)
                return words.Node is MemberNode read
                    ? new Member(words, read.Says, read.Method) { Fixed = read.Fixed, Abstract = read.Abstract, Href = read.Href, Written = read.Written }
                    : new Member(words, words.Text.Trim(), Method: false);

            return written.Hole() is { } hole ? new Member(written, string.Empty, Method: false) { Hole = hole } : null;
        }

        /// <summary>A relation: the classes either end names, what each end draws, and what is written on it.</summary>
        /// <remarks>
        /// An end written <c>()</c> names an interface rather than a class, so that side is a lollipop on the class at the other
        /// end and neither a class nor a relation is made for it.
        /// </remarks>
        private void Related(ContentPart stated, string? inside)
        {
            var named = stated.Children.Where(child => child.Kind == ClassKinds.Named).ToList();
            if (named.Count < 2) return;

            var arrow = stated.Inner(ClassKinds.Arrow);
            var head = Headed(arrow?.Children.FirstOrDefault(piece => piece.Role == ClassRoles.Head)?.Text);
            var tail = Headed(arrow?.Children.FirstOrDefault(piece => piece.Role == ClassRoles.Tail)?.Text);

            if (head == DiagramHead.Circle || tail == DiagramHead.Circle)
            {
                Offered(named, inside, below: tail == DiagramHead.Circle);
                return;
            }

            var from = Gathered(named[0], inside);
            var to = Gathered(named[1], inside);
            if (from is null || to is null) return;

            var counts = stated.Children.Where(child => child.Kind == MermaidKinds.Quoted).ToList();

            diagram.Relations.Add(new Relation(stated, from.Id, to.Id, head, tail,
                                               arrow?.Children.FirstOrDefault(piece => piece.Role == ClassRoles.Line)?.Text == "..")
            {
                Near = counts.Count > 0 ? counts[0].Words() : null,
                Far = counts.Count > 1 ? counts[1].Words() : null,
                Said = stated.Inner(ClassKinds.Said)?.Words(),
                SaidHole = stated.Inner(ClassKinds.Said)?.Hole(),
            });
        }

        /// <summary>The interface one end of a relation offers, hung on the class at the other end.</summary>
        private void Offered(IReadOnlyList<ContentPart> named, string? inside, bool below)
        {
            var owner = Gathered(named[below ? 0 : 1], inside);
            var offered = Name(named[below ? 1 : 0]).Words();

            if (owner is null || offered is not { Length: > 0 }) return;

            owner.Lollipops.Add(new Lollipop(offered, offered.Text, below));
        }

        /// <summary>The class a name says, made where it has not been written before.</summary>
        private Node? Gathered(ContentPart named, string? inside)
        {
            if (Name(named).Words() is not { Length: > 0 } words) return null;

            var node = diagram.Find(words.Text);
            if (node is null)
            {
                node = diagram.Made(named, words.Text, inside);
                node.Said = words;
                node.Whole = new SourceSpan(named.Start, named.End - named.Start);
            }

            node.Generic ??= Worded(named, ClassRoles.Generic)?.Text;
            return node;
        }

        /// <summary>A namespace opening: known by where it stands among the namespaces written.</summary>
        private string Opened(ContentPart group, ContentPart opens, string? parent)
        {
            var closing = group.Children[^1].Stated() is { Kind: ClassKinds.Ends } ends ? ends : opens;
            var key = held.Count.ToString(CultureInfo.InvariantCulture);

            held.Add(new Held(opens, key, Name(opens)?.Words()?.Text ?? string.Empty, parent, opens.Inner(MermaidKinds.Label)?.Words(),
                              new SourceSpan(opens.Start, closing.End - opens.Start)));
            return key;
        }

        /// <summary>A note: the class it is beside, and what it says.</summary>
        private void Noted(ContentPart stated)
        {
            var said = stated.Children.LastOrDefault(child => child.Kind == MermaidKinds.Quoted);

            diagram.Notes.Add(new Note(stated, Name(stated.Inner(ClassKinds.Named))?.Words()?.Text ?? string.Empty, said?.Words(), said?.Hole()));
        }

        /// <summary>Where pressing a class written above leads, and what it says while pointed at.</summary>
        private void Clicked(ContentPart stated)
        {
            if (Name(stated.Inner(ClassKinds.Named))?.Words()?.Text is not { Length: > 0 } id || diagram.Find(id) is not { } node) return;

            var quoted = stated.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Quoted).ToList();
            if (quoted.Count > 0) node.Href = quoted[0].Words()?.Text ?? node.Href;
            if (quoted.Count > 1) node.Tip = quoted[1].Words()?.Text ?? node.Tip;
        }

        /// <summary>
        /// The box each namespace is drawn as. A name with dots in it is a box for each part of it, one inside the next, where the
        /// front matter lets it nest — and one box called the whole name where it does not — and two namespaces sharing their
        /// outer names share the boxes for them.
        /// </summary>
        private IEnumerable<Namespace> Boxed()
        {
            var boxes = new Dictionary<string, Namespace>(StringComparer.Ordinal);

            foreach (var space in held)
            {
                var parts = config.Hierarchical ? space.Id.Split('.', StringSplitOptions.RemoveEmptyEntries) : [space.Id];
                var parent = space.Parent;
                var key = space.Parent;

                for (var at = 0; at < parts.Length; at++)
                {
                    var last = at == parts.Length - 1;
                    key = last ? space.Key : $"{key}/{parts[at]}";

                    if (!last && boxes.ContainsKey(key))
                    {
                        parent = key;
                        continue;
                    }

                    boxes[key] = new Namespace(space.Part, key, parts[at], parent) { Said = last ? space.Said : null, Whole = space.Whole };
                    parent = key;
                }
            }

            return boxes.Values;
        }

        private static ContentPart? Name(ContentPart? named) => named?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);

        /// <summary>What a line says in a role, wherever it is written inside it.</summary>
        private static ContentPart? Worded(ContentPart part, string role) =>
            part.SelfAndDescendants().FirstOrDefault(inner => inner.Kind == MermaidKinds.Words && inner.Role == role);

        private static string? Setting(ContentPart stated, string role) =>
            stated.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidKinds.Setting && part.Role == role)?.Text;

        /// <summary>What one end of an arrow draws.</summary>
        private static DiagramHead Headed(string? mark) => mark switch
        {
            "<|" or "|>" => DiagramHead.Triangle,
            "*" => DiagramHead.Diamond,
            "o" => DiagramHead.HollowDiamond,
            "<" or ">" => DiagramHead.Open,
            "()" => DiagramHead.Circle,
            _ => DiagramHead.None,
        };

        /// <summary>Which way a word lays a diagram out, or null where it lays it out no way at all.</summary>
        private static DiagramWay? Wayward(string? said) => said?.ToUpperInvariant() switch
        {
            "TB" or "TD" => DiagramWay.Down,
            "BT" => DiagramWay.Up,
            "LR" => DiagramWay.Right,
            "RL" => DiagramWay.Left,
            _ => null,
        };

        /// <summary>A namespace as it was written, before the boxes it comes to are worked out.</summary>
        private sealed record Held(ContentPart Part, string Key, string Id, string? Parent, ContentPart? Said, ISourcePart Whole);
    }
}
