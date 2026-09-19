using System.Text;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Class;

/// <summary>Which way a class diagram is laid out: where a relation written left to right points.</summary>
public enum ClassWay
{
    /// <summary><c>TB</c> and <c>TD</c>.</summary>
    Down,

    /// <summary><c>BT</c>.</summary>
    Up,

    /// <summary><c>LR</c>.</summary>
    Right,

    /// <summary><c>RL</c>.</summary>
    Left,
}

/// <summary>What one end of a relation draws.</summary>
public enum ClassEnd
{
    /// <summary>Nothing at all, which is the plain line a link is.</summary>
    None,

    /// <summary>The hollow triangle inheritance and realization are drawn with — <c>&lt;|</c>.</summary>
    Extension,

    /// <summary>The filled diamond composition is drawn with — <c>*</c>.</summary>
    Composition,

    /// <summary>The hollow diamond aggregation is drawn with — <c>o</c>.</summary>
    Aggregation,

    /// <summary>The open arrow an association is drawn with — <c>&gt;</c>.</summary>
    Association,

    /// <summary>The circle an interface is offered as — <c>()</c>.</summary>
    Lollipop,
}

/// <summary>
/// One member of a class, read: the line it was written on, what it says once its type parameters are angled and its classifiers
/// taken off, and what those classifiers said.
/// </summary>
/// <param name="Part">The member as it was written, which is what a press on it means.</param>
/// <param name="Says">What is drawn: <c>~T~</c> as <c>&lt;T&gt;</c>, without the <c>*</c> or <c>$</c> ending it.</param>
/// <param name="Method">Whether it is a method, which Mermaid decides by the brackets after its name.</param>
public sealed record ClassMember(ContentPart Part, string Says, bool Method, int Order)
{
    /// <summary>Whether a <c>$</c> ended it, which draws it underlined.</summary>
    public bool Fixed { get; init; }

    /// <summary>Whether a <c>*</c> ended it, which draws it in italics.</summary>
    public bool Abstract { get; init; }

    /// <summary>
    /// Where pressing it leads, from the <c>@@</c> ending it — which nothing in Mermaid writes, and the Code feature writes to
    /// point each member at the line it is declared on.
    /// </summary>
    public string? Href { get; init; }

    /// <summary>The hole standing where it goes, where holes were asked for and nothing is written there yet.</summary>
    public ContentPart? Hole { get; init; }

    /// <summary>Whether what is drawn is the characters written, which is what lets a caret stand in it.</summary>
    public bool Written => Href is null && Says == Part.Text.Trim();

    /// <summary>What the token ending a member says it leads to, and the member without it.</summary>
    public const string Link = " @@";

    /// <summary>One member, read from the text it was written as.</summary>
    public static ClassMember Of(ContentPart part, string text, int order)
    {
        var (said, href) = text.IndexOf(Link, StringComparison.Ordinal) is var at and >= 0
            ? (text[..at], text[(at + Link.Length)..].Trim())
            : (text, null);

        said = said.Trim();

        // Mermaid writes a classifier after the brackets or after what the method gives back, so both are taken off here.
        var (kept, quick, loose) = Classified(said);
        var method = kept.Contains('(', StringComparison.Ordinal);

        return new ClassMember(part, Angled(method ? Returned(kept) : kept), method, order)
        {
            Fixed = quick,
            Abstract = loose,
            Href = string.IsNullOrEmpty(href) ? null : href,
        };
    }

    /// <summary>
    /// A member with the <c>$</c> that says the class holds it and the <c>*</c> that says nothing here does taken off —
    /// written at the end of it, or hard against the brackets of a method that gives something back.
    /// </summary>
    private static (string Said, bool Fixed, bool Abstract) Classified(string said)
    {
        if (said.EndsWith('$')) return (said[..^1].TrimEnd(), true, false);
        if (said.EndsWith('*')) return (said[..^1].TrimEnd(), false, true);

        var close = said.LastIndexOf(')');
        if (close < 0 || close + 1 >= said.Length) return (said, false, false);

        var mark = said[close + 1];
        if (mark is not ('$' or '*')) return (said, false, false);

        return (said[..(close + 1)] + said[(close + 2)..], mark == '$', mark == '*');
    }

    /// <summary>
    /// What a method gives back, which Mermaid draws after a colon: <c>getId() int</c> is drawn <c>getId() : int</c>, and a
    /// method giving nothing back is drawn as it was written.
    /// </summary>
    private static string Returned(string said)
    {
        var close = said.LastIndexOf(')');
        if (close < 0 || close == said.Length - 1) return said;

        var gives = said[(close + 1)..].Trim();

        return gives.Length == 0 ? said : said[..(close + 1)] + " : " + gives;
    }

    /// <summary>
    /// Type parameters written between tildes, drawn between angle brackets as Mermaid draws them — nested ones and all, so
    /// <c>List~List~int~~</c> comes out <c>List&lt;List&lt;int&gt;&gt;</c>. Text whose tildes do not pair off is left as
    /// written, since a lone one is a member's package visibility rather than a type.
    /// </summary>
    public static string Angled(string text)
    {
        var tildes = text.Count(character => character == '~');
        if (tildes == 0 || tildes % 2 != 0) return text;

        var built = new StringBuilder(text.Length);
        var depth = 0;

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] != '~')
            {
                built.Append(text[at]);
                continue;
            }

            // A tilde closes what is open where nothing more of the type follows it — the end, another tilde, or a space.
            var next = at + 1 < text.Length ? text[at + 1] : '\0';
            var closes = depth > 0 && next is '\0' or '~' or ',' or ')' or ' ' or '\t';

            built.Append(closes ? '>' : '<');
            depth += closes ? -1 : 1;
        }

        return depth == 0 ? built.ToString() : text;
    }
}

/// <summary>
/// One interface a class offers, written with <c>()</c> at that end of a relation. Mermaid draws it as a small circle on the
/// class itself rather than as a class of its own with a line to it, so it is neither a node nor a relation here either.
/// </summary>
/// <param name="Part">The interface's name as it was written, which is what a press on the circle means.</param>
/// <param name="Below">Whether it hangs under the class — what <c>A --() bar</c> writes — rather than sitting above it.</param>
public sealed record ClassLollipop(ContentPart Part, string Name, bool Below);

/// <summary>One class, read: where it was written, what it is called, what is drawn on it, and what it holds.</summary>
/// <param name="Part">The class as it was written, which is what a press on its name means.</param>
/// <param name="Id">What it is called, which is what a relation, a note and a styling line name it by.</param>
/// <param name="Said">The words drawn for its name: its label where one is written, and otherwise its id.</param>
/// <param name="Group">The namespace it was first written in, or null for one written outside them all.</param>
public sealed record ClassNode(
    ContentPart Part,
    string Id,
    ContentPart? Said,
    string? Group,
    MermaidStyle Style,
    int Order)
{
    /// <summary>The type parameters written after its name, drawn between angle brackets after it.</summary>
    public string? Generic { get; init; }

    /// <summary>What <c>&lt;&lt;interface&gt;&gt;</c> says it is, drawn in guillemets over its name.</summary>
    public ContentPart? Kind { get; init; }

    /// <summary>Its members, in the order they are written.</summary>
    public IReadOnlyList<ClassMember> Members { get; init; } = [];

    /// <summary>The interfaces it offers, drawn as circles above and below it.</summary>
    public IReadOnlyList<ClassLollipop> Lollipops { get; init; } = [];

    /// <summary>The hole standing where its name goes.</summary>
    public ContentPart? SaidHole { get; init; }

    /// <summary>The whole of it as it was written, from the line declaring it through the <c>}</c> closing its members.</summary>
    public ISourcePart Whole { get; init; } = default(SourceSpan);

    /// <summary>Where a <c>click</c> line says pressing it leads.</summary>
    public string? Href { get; init; }

    /// <summary>What a <c>click</c> line says it says while pointed at.</summary>
    public string? Tip { get; init; }

    /// <summary>The members drawn above the line, which are the ones with no brackets after their name.</summary>
    public IEnumerable<ClassMember> Fields => Members.Where(member => !member.Method);

    /// <summary>The members drawn below it.</summary>
    public IEnumerable<ClassMember> Methods => Members.Where(member => member.Method);
}

/// <summary>One relation, read: the classes it joins, what each of its ends draws, and what is written on it.</summary>
/// <param name="Part">The relation as it was written, which is what a press on its line means.</param>
/// <param name="Dotted">Whether it is drawn dotted, which is what <c>..</c> writes.</param>
public sealed record ClassRelation(
    ContentPart Part,
    string From,
    string To,
    ClassEnd Head,
    ClassEnd Tail,
    bool Dotted,
    int Order)
{
    /// <summary>How many of the class at the near end the far one has, where a count is written there.</summary>
    public ContentPart? Near { get; init; }

    /// <summary>How many of the class at the far end the near one has.</summary>
    public ContentPart? Far { get; init; }

    /// <summary>What is written on it, over the middle of its line.</summary>
    public ContentPart? Said { get; init; }

    /// <summary>The hole standing where that goes.</summary>
    public ContentPart? SaidHole { get; init; }
}

/// <summary>
/// One namespace, read: the box drawn round every class first written inside it, what is written at the top of it, and the
/// namespace it is itself inside.
/// </summary>
/// <param name="Key">What the nesting calls it, which is what a class says it is inside.</param>
/// <param name="Id">What it is called, dots and all, which is what a <c>namespace</c> line wrote.</param>
/// <param name="Name">What is drawn at the top of it: its label, or the part of its id this box stands for.</param>
public sealed record ClassSpace(
    ContentPart Part,
    string Key,
    string Id,
    string Name,
    string? Parent,
    int Order)
{
    /// <summary>The label drawn instead of its name, where one is written.</summary>
    public ContentPart? Said { get; init; }

    /// <summary>The whole of it as it was written, from the line that opened it through the <c>}</c> that closed it.</summary>
    public ISourcePart Whole { get; init; } = default(SourceSpan);
}

/// <summary>One note, read: the class it is written beside, and what it says.</summary>
/// <param name="Of">The class it is written beside, or empty where it floats beside nothing.</param>
public sealed record ClassNote(ContentPart Part, string Of, ContentPart? Said, int Order)
{
    /// <summary>The hole standing where what it says goes.</summary>
    public ContentPart? SaidHole { get; init; }
}

/// <summary>
/// A <c>classDiagram</c> block, read: the classes written in it, their members, the relations between them, the namespaces they
/// are boxed into, and the notes beside them. Its title is the block's (<see cref="MermaidBlock.Title"/>).
///
/// A class written twice is one class: the second writing says more about the one the first made — its members, its annotation,
/// its label — rather than making another, which is what lets a relation name the classes a line above wrote.
/// </summary>
public sealed class ClassDiagram
{
    private ClassDiagram(MermaidBlock block, ClassConfig config, ClassWay way, IReadOnlyList<ClassNode> nodes,
                         IReadOnlyList<ClassRelation> relations, IReadOnlyList<ClassSpace> spaces, IReadOnlyList<ClassNote> notes)
    {
        Block = block;
        Config = config;
        Way = way;
        Nodes = nodes;
        Relations = relations;
        Spaces = spaces;
        Notes = notes;
    }

    /// <summary>Reads a block: parsed, then worked over by its stages (<see cref="MermaidParser.Read"/>).</summary>
    public static ClassDiagram Read(string? block) => Of(MermaidParser.Read(block));

    /// <summary>Reads a tree the stages have already been over.</summary>
    public static ClassDiagram Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    /// <summary>The block this was read from — its front matter, its header, its title, everything written in it.</summary>
    public MermaidBlock Block { get; }

    /// <summary>What the front matter asks for.</summary>
    public ClassConfig Config { get; }

    /// <summary>The way the whole diagram is laid out.</summary>
    public ClassWay Way { get; }

    /// <summary>The classes, in the order they are first written.</summary>
    public IReadOnlyList<ClassNode> Nodes { get; }

    /// <summary>The relations, in the order they are written.</summary>
    public IReadOnlyList<ClassRelation> Relations { get; }

    /// <summary>The namespaces, each before the ones nested in it.</summary>
    public IReadOnlyList<ClassSpace> Spaces { get; }

    /// <summary>The notes, in the order they are written.</summary>
    public IReadOnlyList<ClassNote> Notes { get; }

    /// <summary>The class an id names, or null where nothing is called that.</summary>
    public ClassNode? Find(string id) =>
        id.Length == 0 ? null : Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));

    /// <summary>The classes written inside a namespace — those written outside them all, for null.</summary>
    public IEnumerable<ClassNode> Inside(string? space) =>
        Nodes.Where(node => string.Equals(node.Group, space, StringComparison.Ordinal));

    /// <summary>The namespaces opened inside one — the outermost ones, for null.</summary>
    public IEnumerable<ClassSpace> Within(string? space) =>
        Spaces.Where(nested => string.Equals(nested.Parent, space, StringComparison.Ordinal));

    /// <summary>Which way a word lays a diagram out, or null where it lays it out no way at all.</summary>
    public static ClassWay? Wayward(string? said) => said?.ToUpperInvariant() switch
    {
        "TB" or "TD" => ClassWay.Down,
        "BT" => ClassWay.Up,
        "LR" => ClassWay.Right,
        "RL" => ClassWay.Left,
        _ => null,
    };

    /// <summary>What the two ends of an operator draw, and whether its line is dotted.</summary>
    public static (ClassEnd Head, ClassEnd Tail, bool Dotted) Drawn(string operation)
    {
        var dotted = operation.Contains("..", StringComparison.Ordinal);
        var at = operation.IndexOf(dotted ? ".." : "--", StringComparison.Ordinal);
        if (at < 0) return (ClassEnd.None, ClassEnd.None, dotted);

        return (Ended(operation[..at]), Ended(operation[(at + 2)..]), dotted);
    }

    private static ClassEnd Ended(string mark) => mark switch
    {
        "<|" or "|>" => ClassEnd.Extension,
        "*" => ClassEnd.Composition,
        "o" => ClassEnd.Aggregation,
        "<" or ">" => ClassEnd.Association,
        "()" => ClassEnd.Lollipop,
        _ => ClassEnd.None,
    };

    /// <summary>Reads a block that has already been read — the shared parse, worked over by the class diagram's own stages.</summary>
    public static ClassDiagram Of(MermaidBlock block)
    {
        var config = ClassConfig.Read(block.Config);

        var nodes = new List<Made>();
        var known = new Dictionary<string, Made>(StringComparer.Ordinal);
        var spaces = new List<Held>();
        var open = new Stack<Held>();
        var relations = new List<ClassRelation>();
        var notes = new List<ClassNote>();
        var way = ClassWay.Down;

        var classes = new Dictionary<string, MermaidStyle>(StringComparer.Ordinal);
        var taken = new List<(IReadOnlyList<string> Ids, string Class)>();
        var written = new List<(IReadOnlyList<string> Ids, MermaidStyle Style)>();

        foreach (var line in block.Reading.Root.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { } stated) continue;
            var inside = Keyed(stated.Fact(ClassRoles.Inside));

            switch (stated.Kind)
            {
                case ClassKinds.Body:
                    Bodied(stated, inside, nodes, known);
                    break;

                case ClassKinds.Class:
                    Declared(stated, inside, nodes, known);
                    break;

                case ClassKinds.Says:
                    Membered(stated, inside, nodes, known);
                    break;

                case ClassKinds.Annotation:
                    Annotated(stated, inside, nodes, known);
                    break;

                case ClassKinds.Relation:
                    Related(stated, inside, nodes, known, relations);
                    break;

                case ClassKinds.Namespace:
                    var space = Opened(stated, open.Count > 0 ? open.Peek().Key : null, spaces.Count);
                    spaces.Add(space);
                    open.Push(space);
                    break;

                case ClassKinds.Ends:
                    if (open.Count > 0) open.Pop().Closed = stated;
                    break;

                case ClassKinds.Note:
                    notes.Add(Noted(stated, notes.Count));
                    break;

                case ClassKinds.Direction:
                    way = Wayward(Setting(stated, ClassRoles.Towards)) ?? way;
                    break;

                case ClassKinds.ClassDef:
                    var declared = MermaidStyle.None.With(stated.Inner(MermaidKinds.Properties));
                    foreach (var name in ClassGrammar.Styling.Classes(stated)) classes[name] = declared;
                    break;

                case ClassKinds.CssClass:
                    taken.Add((ClassGrammar.Styling.Ids(stated), ClassGrammar.Styling.Given(stated) ?? string.Empty));
                    break;

                case ClassKinds.Style:
                    written.Add((ClassGrammar.Styling.Ids(stated), MermaidStyle.None.With(stated.Inner(MermaidKinds.Properties))));
                    break;

                case ClassKinds.Click:
                    Clicked(stated, known);
                    break;
            }
        }

        foreach (var node in nodes) foreach (var name in node.Classes) taken.Add((new[] { node.Id }, name));

        var styles = MermaidStyling.Styles(known.Keys, classes, taken, written);
        var boxes = Nested(spaces, config.Hierarchical);

        return new ClassDiagram(block, config, way,
                                [.. nodes.Select(made => Frozen(made, styles))],
                                relations,
                                [.. boxes.Values.OrderBy(box => box.Order)],
                                notes);
    }

    // ── Reading the lines ───────────────────────────────────────────────────

    /// <summary>A <c>class Foo { … }</c>: the class, and the members and annotation written between its braces.</summary>
    private static void Bodied(ContentPart stated, string? inside, List<Made> nodes, Dictionary<string, Made> known)
    {
        if (stated.Inner(ClassKinds.Opens) is not { } opens) return;

        var made = Declared(opens, inside, nodes, known);
        if (made is null) return;

        made.Whole = new SourceSpan(stated.Start, stated.End - stated.Start);

        foreach (var part in stated.SelfAndDescendants())
        {
            if (part.Kind == ClassKinds.Annotation && Worded(part, ClassRoles.Kind) is { } kind) made.Kind ??= kind;
            if (part.Kind != ClassKinds.Member) continue;

            if (Worded(part, ClassRoles.Member) is { Length: > 0 } words)
                made.Members.Add(ClassMember.Of(words, words.Text, made.Members.Count));
            else if (part.Hole() is { } hole)
                made.Members.Add(new ClassMember(part, string.Empty, Method: false, made.Members.Count) { Hole = hole });
        }
    }

    /// <summary>A <c>class</c> line: the class it declares, its label, its type parameters and its annotation.</summary>
    private static Made? Declared(ContentPart stated, string? inside, List<Made> nodes, Dictionary<string, Made> known)
    {
        if (stated.Inner(ClassKinds.Named) is not { } named) return null;

        var made = Gathered(named, inside, nodes, known);
        if (made is null) return made;

        made.Said = Inner(stated, MermaidKinds.Label) ?? made.Said;
        made.SaidHole = stated.Inner(MermaidKinds.Label)?.Hole() ?? made.SaidHole;
        made.Kind ??= Worded(stated, ClassRoles.Kind);

        return made;
    }

    /// <summary>A <c>Foo : +int age</c> line, which gives a class one member.</summary>
    private static void Membered(ContentPart stated, string? inside, List<Made> nodes, Dictionary<string, Made> known)
    {
        if (Declared(stated, inside, nodes, known) is not { } made || stated.Inner(ClassKinds.Member) is not { } member) return;

        if (Worded(member, ClassRoles.Member) is { Length: > 0 } words)
            made.Members.Add(ClassMember.Of(words, words.Text, made.Members.Count));
        else if (member.Hole() is { } hole)
            made.Members.Add(new ClassMember(member, string.Empty, Method: false, made.Members.Count) { Hole = hole });
    }

    /// <summary>An <c>&lt;&lt;interface&gt;&gt; Shape</c> line, which says what a class declared elsewhere is.</summary>
    private static void Annotated(ContentPart stated, string? inside, List<Made> nodes, Dictionary<string, Made> known)
    {
        if (Declared(stated, inside, nodes, known) is { } made) made.Kind = Worded(stated, ClassRoles.Kind) ?? made.Kind;
    }

    /// <summary>A relation: the classes either end names, what each end draws, and what is written on it.</summary>
    /// <remarks>
    /// An end written <c>()</c> names an interface rather than a class, so that side is a lollipop on the class at the other
    /// end and neither a class nor a relation is made for it.
    /// </remarks>
    private static void Related(ContentPart stated, string? inside, List<Made> nodes, Dictionary<string, Made> known,
                                List<ClassRelation> relations)
    {
        var named = stated.Children.Where(child => child.Kind == ClassKinds.Named).ToList();
        if (named.Count < 2) return;

        var (head, tail, dotted) = Drawn(Setting(stated, ClassRoles.Arrow, Kinds.Token) ?? string.Empty);

        if (head == ClassEnd.Lollipop || tail == ClassEnd.Lollipop)
        {
            Offered(named, inside, nodes, known, below: tail == ClassEnd.Lollipop);
            return;
        }

        var from = Gathered(named[0], inside, nodes, known);
        var to = Gathered(named[1], inside, nodes, known);
        if (from is null || to is null) return;

        var counts = stated.Children.Where(child => child.Kind == MermaidKinds.Quoted).ToList();

        relations.Add(new ClassRelation(stated, from.Id, to.Id, head, tail, dotted, relations.Count)
        {
            Near = counts.Count > 0 ? counts[0].Words() : null,
            Far = counts.Count > 1 ? counts[1].Words() : null,
            Said = Inner(stated, ClassKinds.Said),
            SaidHole = stated.Inner(ClassKinds.Said)?.Hole(),
        });
    }

    /// <summary>The interface one end of a relation offers, hung on the class at the other end.</summary>
    private static void Offered(IReadOnlyList<ContentPart> named, string? inside, List<Made> nodes,
                                Dictionary<string, Made> known, bool below)
    {
        var owner = Gathered(named[below ? 0 : 1], inside, nodes, known);
        var offered = named[below ? 1 : 0].Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words();

        if (owner is null || offered is not { Length: > 0 }) return;

        owner.Lollipops.Add(new ClassLollipop(offered, offered.Text, below));
    }

    /// <summary>The class a name says, made where it has not been written before.</summary>
    private static Made? Gathered(ContentPart named, string? inside, List<Made> nodes, Dictionary<string, Made> known)
    {
        var name = named.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);
        if (name.Words() is not { Length: > 0 } words) return null;

        if (!known.TryGetValue(words.Text, out var made))
        {
            made = new Made(named, words.Text, inside, nodes.Count) { Said = words };

            nodes.Add(made);
            known[words.Text] = made;
        }

        made.Generic ??= Worded(named, ClassRoles.Generic)?.Text;

        if (named.Children.Where(child => child.Kind == MermaidKinds.Name).Skip(1).FirstOrDefault()?.Words() is { } given)
            made.Classes.Add(given.Text);

        return made;
    }

    /// <summary>A namespace opening.</summary>
    private static Held Opened(ContentPart stated, string? parent, int order)
    {
        var name = stated.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);

        return new Held(stated, stated.Fact(ClassRoles.Opened) ?? string.Empty, name.Words()?.Text ?? string.Empty, order)
        {
            Parent = parent,
            Said = Inner(stated, MermaidKinds.Label),
        };
    }

    /// <summary>A note: the class it is beside, and what it says.</summary>
    private static ClassNote Noted(ContentPart stated, int order)
    {
        var named = stated.Inner(ClassKinds.Named);
        var name = named?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);
        var said = stated.Children.LastOrDefault(child => child.Kind == MermaidKinds.Quoted);

        return new ClassNote(stated, name.Words()?.Text ?? string.Empty, said?.Words(), order)
        {
            SaidHole = said?.Hole(),
        };
    }

    /// <summary>Where pressing a class leads, and what it says while pointed at.</summary>
    private static void Clicked(ContentPart stated, IReadOnlyDictionary<string, Made> known)
    {
        var named = stated.Inner(ClassKinds.Named);
        var name = named?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);
        if (name.Words()?.Text is not { Length: > 0 } id || !known.TryGetValue(id, out var made)) return;

        var quoted = stated.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Quoted).ToList();
        if (quoted.Count > 0) made.Href = quoted[0].Words()?.Text ?? made.Href;
        if (quoted.Count > 1) made.Tip = quoted[1].Words()?.Text ?? made.Tip;
    }

    // ── The boxes the namespaces come to ────────────────────────────────────

    /// <summary>
    /// The box each namespace is drawn as. A name with dots in it is a box for each part of it, one inside the next, where the
    /// front matter lets it nest — and one box called the whole name where it does not.
    /// </summary>
    private static Dictionary<string, ClassSpace> Nested(IReadOnlyList<Held> spaces, bool hierarchical)
    {
        var boxes = new Dictionary<string, ClassSpace>(StringComparer.Ordinal);
        var order = 0;

        foreach (var held in spaces)
        {
            var parts = hierarchical ? held.Id.Split('.', StringSplitOptions.RemoveEmptyEntries) : [held.Id];
            var parent = held.Parent;
            var key = held.Parent;

            for (var at = 0; at < parts.Length; at++)
            {
                var last = at == parts.Length - 1;
                key = last ? held.Key : $"{key}/{parts[at]}";

                if (!last && boxes.ContainsKey(key))
                {
                    parent = key;
                    continue;
                }

                boxes[key] = new ClassSpace(held.Part, key, held.Id, parts[at], parent, order++)
                {
                    Said = last ? held.Said : null,
                    Whole = new SourceSpan(held.Part.Start, (held.Closed?.End ?? held.Part.End) - held.Part.Start),
                };

                parent = key;
            }
        }

        return boxes;
    }

    // ── What the pieces say ─────────────────────────────────────────────────

    private static ContentPart? Inner(ContentPart stated, string kind) => stated.Inner(kind)?.Words();

    /// <summary>What a line says in a role, wherever it is written inside it.</summary>
    private static ContentPart? Worded(ContentPart part, string role) =>
        part.SelfAndDescendants().FirstOrDefault(inner => inner.Kind == MermaidKinds.Words && inner.Role == role);

    private static string? Setting(ContentPart stated, string role, string kind = MermaidKinds.Setting) =>
        stated.SelfAndDescendants().FirstOrDefault(part => part.Kind == kind && part.Role == role)?.Text;

    private static string? Keyed(string? fact) => string.IsNullOrEmpty(fact) ? null : fact;

    private static ClassNode Frozen(Made made, IReadOnlyDictionary<string, MermaidStyle> styles) =>
        new(made.Part, made.Id, made.Said, made.Group, styles.GetValueOrDefault(made.Id, MermaidStyle.None), made.Order)
        {
            Generic = made.Generic,
            Kind = made.Kind,
            Members = made.Members,
            Lollipops = made.Lollipops,
            SaidHole = made.SaidHole,
            Whole = made.Whole ?? new SourceSpan(made.Part.Start, made.Part.End - made.Part.Start),
            Href = made.Href,
            Tip = made.Tip,
        };

    // ── What it is read into ────────────────────────────────────────────────

    /// <summary>A class being read, before everything said about it is gathered.</summary>
    private sealed class Made(ContentPart part, string id, string? group, int order)
    {
        public ContentPart Part { get; } = part;

        public string Id { get; } = id;

        public string? Group { get; } = group;

        public int Order { get; } = order;

        public string? Generic { get; set; }

        public ContentPart? Kind { get; set; }

        public ContentPart? Said { get; set; }

        public ContentPart? SaidHole { get; set; }

        public ISourcePart? Whole { get; set; }

        public string? Href { get; set; }

        public string? Tip { get; set; }

        public List<ClassMember> Members { get; } = [];

        public List<ClassLollipop> Lollipops { get; } = [];

        public List<string> Classes { get; } = [];
    }

    /// <summary>A namespace being read.</summary>
    private sealed class Held(ContentPart part, string key, string id, int order)
    {
        public ContentPart Part { get; } = part;

        public string Key { get; } = key;

        public string Id { get; } = id;

        public int Order { get; } = order;

        public string? Parent { get; init; }

        public ContentPart? Said { get; init; }

        /// <summary>The <c>}</c> that closed it, or null for one nothing closes.</summary>
        public ContentPart? Closed { get; set; }
    }
}
