using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.State;

/// <summary>Which way a diagram, or a composite state in it, is laid out: where a transition written left to right points.</summary>
public enum StateWay
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

/// <summary>What a state is drawn as.</summary>
public enum StateShape
{
    /// <summary>A box, with what is written on it inside.</summary>
    Plain,

    /// <summary>The filled dot the diagram starts at, which <c>[*]</c> writes where a transition leaves it.</summary>
    Start,

    /// <summary>The ringed dot the diagram stops at, which <c>[*]</c> writes where a transition reaches it.</summary>
    Stop,

    /// <summary>The bar work forks from.</summary>
    Fork,

    /// <summary>The bar work joins at.</summary>
    Join,

    /// <summary>The diamond a choice between paths is drawn as.</summary>
    Choice,

    /// <summary>The line dividing two regions of a composite state, which run at the same time.</summary>
    Divider,
}

/// <summary>
/// One state, read: where it was written, what it is called, what is drawn on it, what it is drawn as, the composite state it
/// belongs to and what it is styled with.
/// </summary>
/// <param name="Part">The whole state as it was written, which is what a press on it means.</param>
/// <param name="Id">What it is called, which is what a transition, a <c>class</c>, a <c>style</c> and a note name it by.</param>
/// <param name="Said">The words drawn on it: what is written on it, or what it is called where nothing else says anything.</param>
/// <param name="Group">The composite state it was first written in, or null for one written outside them all.</param>
/// <param name="Order">Where it comes among the states, in the order they are first written.</param>
public sealed record StateNode(
    ContentPart Part,
    string Id,
    ContentPart? Said,
    StateShape Shape,
    string? Group,
    MermaidStyle Style,
    int Order)
{
    /// <summary>The hole standing where what is written on it goes, where holes were asked for and nothing is written there yet.</summary>
    public ContentPart? SaidHole { get; init; }

    /// <summary>Where a <c>click</c> line says pressing it leads, which nothing here follows.</summary>
    public string? Href { get; init; }

    /// <summary>What a <c>click</c> line says it says while pointed at.</summary>
    public string? Tip { get; init; }

    /// <summary>Whether it is one of the dots the diagram starts and stops at, which hold no words and are drawn small.</summary>
    public bool Marker => Shape is StateShape.Start or StateShape.Stop;

    /// <summary>
    /// Which region of the composite state it is in, counted from one: a <c>--</c> line divides a composite state into regions that run
    /// at the same time, and what is written after one is in the next. A divider is in the region it closes.
    /// </summary>
    public int Region { get; init; } = 1;
}

/// <summary>One transition, read: the states it joins and what is written on it.</summary>
/// <param name="Part">The transition as it was written, which is what a press on it means.</param>
/// <param name="Order">Where it comes among the transitions, in the order they are written.</param>
public sealed record StateStep(ContentPart Part, string From, string To, ContentPart? Said, int Order)
{
    /// <summary>The hole standing where what is written on it goes.</summary>
    public ContentPart? SaidHole { get; init; }
}

/// <summary>
/// One composite state, read: the box drawn round every state first written inside it, what is written at the top of it, the
/// composite state it is itself inside, and the way its own states are laid out where a <c>direction</c> line says one.
/// </summary>
/// <param name="Key">What the nesting calls it, which is what a state says it is inside.</param>
/// <param name="Id">What it is called, which a transition and a <c>style</c> line name it by.</param>
public sealed record StateGroup(
    ContentPart Part,
    string Key,
    string Id,
    ContentPart? Said,
    string? Parent,
    StateWay? Way,
    MermaidStyle Style,
    int Order)
{
    /// <summary>The hole standing where what is written on it goes.</summary>
    public ContentPart? SaidHole { get; init; }

    /// <summary>
    /// The whole of it as it was written, from the line that opened it through the <c>}</c> that closed it — the opening line
    /// alone, where nothing closed it.
    /// </summary>
    public ISourcePart Whole { get; init; } = default(SourceSpan);

    /// <summary>Which region of the composite state it is in, counted from one — see <see cref="StateNode.Region"/>.</summary>
    public int Region { get; init; } = 1;
}

/// <summary>One note, read: the state it is written beside, which side of it, and what it says.</summary>
/// <param name="Part">The whole note as it was written, the lines it runs across and all.</param>
/// <param name="Of">The state it is written beside, or its own name where it floats.</param>
/// <param name="Left">Whether it is written to the left of that state rather than the right of it.</param>
/// <param name="Said">What it says, a part for each line it is written across.</param>
public sealed record StateNote(ContentPart Part, string Of, bool Left, IReadOnlyList<ContentPart> Said, int Order)
{
    /// <summary>The hole standing where what it says goes.</summary>
    public ContentPart? SaidHole { get; init; }

    /// <summary>Whether it floats with a name of its own rather than being written beside a state.</summary>
    public bool Floating { get; init; }
}

/// <summary>
/// A <c>stateDiagram</c> block, read: the states written in it, the transitions between them, the composite states they are
/// gathered into, and the notes written beside them. Its title is the block's (<see cref="MermaidBlock.Title"/>).
///
/// <para>
/// A state written twice is one state: the second writing says more about the one the first made — what is drawn on it, what it is
/// drawn as — rather than making another, which is what lets a transition name the states a line above wrote. A state belongs to
/// the composite state it was first written in.
/// </para>
/// <para>
/// <strong>Every <c>[*]</c> in one scope is the same dot.</strong> A transition leaving <c>[*]</c> leaves the dot that scope starts
/// at and one reaching <c>[*]</c> reaches the dot it stops at, so a diagram that writes <c>[*]</c> four times draws two dots, which
/// is how Mermaid reads it. They are called <c>start</c> and <c>end</c>, the names a <c>class</c> line styles them by.
/// </para>
/// </summary>
public sealed class StateDiagram
{
    /// <summary>What <c>[*]</c> is written as, which is the same dot wherever it is written in one scope.</summary>
    public const string Edge = "[*]";

    private StateDiagram(MermaidBlock block, StateConfig config, StateWay way, bool plain, IReadOnlyList<StateNode> nodes,
                         IReadOnlyList<StateStep> steps, IReadOnlyList<StateGroup> groups, IReadOnlyList<StateNote> notes)
    {
        Block = block;
        Config = config;
        Way = way;
        Plain = plain;
        Nodes = nodes;
        Steps = steps;
        Groups = groups;
        Notes = notes;
    }

    /// <summary>Reads a block: parsed, then worked over by its stages (<see cref="MermaidParser.Read"/>).</summary>
    

    /// <summary>Reads a tree the stages have already been over.</summary>
    public static StateDiagram Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    /// <summary>The block this was read from — its front matter, its header, its title, everything written in it.</summary>
    public MermaidBlock Block { get; }

    /// <summary>What the front matter asks for.</summary>
    public StateConfig Config { get; }

    /// <summary>The way the whole diagram is laid out.</summary>
    public StateWay Way { get; }

    /// <summary>Whether a state with nothing written on it is drawn as its id alone — <c>hide empty description</c>.</summary>
    public bool Plain { get; }

    /// <summary>The states, in the order they are first written.</summary>
    public IReadOnlyList<StateNode> Nodes { get; }

    /// <summary>The transitions, in the order they are written.</summary>
    public IReadOnlyList<StateStep> Steps { get; }

    /// <summary>The composite states, each before the ones nested in it.</summary>
    public IReadOnlyList<StateGroup> Groups { get; }

    /// <summary>The notes, in the order they are written.</summary>
    public IReadOnlyList<StateNote> Notes { get; }

    /// <summary>The state an id names, or null where nothing is called that.</summary>
    public StateNode? Find(string id) =>
        id.Length == 0 ? null : Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));

    /// <summary>The states written inside a composite state — those written outside them all, for null.</summary>
    public IEnumerable<StateNode> Inside(string? group) =>
        Nodes.Where(node => string.Equals(node.Group, group, StringComparison.Ordinal));

    /// <summary>The composite states opened inside one — the outermost ones, for null.</summary>
    public IEnumerable<StateGroup> Within(string? group) =>
        Groups.Where(nested => string.Equals(nested.Parent, group, StringComparison.Ordinal));

    /// <summary>The composite state a key names, or null where none does.</summary>
    public StateGroup? Group(string? key) =>
        key is null ? null : Groups.FirstOrDefault(group => string.Equals(group.Key, key, StringComparison.Ordinal));

    /// <summary>Reads a block that has already been read — the shared parse, worked over by the state diagram's own stages.</summary>
    public static StateDiagram Of(MermaidBlock block)
    {
        var nodes = new List<Made>();
        var known = new Dictionary<string, Made>(StringComparer.Ordinal);
        var groups = new List<Held>();
        var open = new Stack<Held>();
        var steps = new List<Joined>();
        var notes = new List<Marked>();
        var ways = new Dictionary<string, StateWay>(StringComparer.Ordinal);

        var classes = new Dictionary<string, MermaidStyle>(StringComparer.Ordinal);
        var taken = new List<(IReadOnlyList<string> Ids, string Class)>();
        var written = new List<(IReadOnlyList<string> Ids, MermaidStyle Style)>();
        var plain = false;

        // Which region of each composite state the lines are being written in, counted from one: a -- line starts the next.
        var regions = new Dictionary<string, int>(StringComparer.Ordinal);
        int Region(string? scope) => regions.GetValueOrDefault(scope ?? string.Empty, 1);

        foreach (var line in block.Reading.Root.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { } stated) continue;
            var inside = Keyed(stated.Fact(StateRoles.Inside));

            switch (stated.Kind)
            {
                case StateKinds.Opens:
                    var group = Opens(stated, inside, Region(inside), groups.Count);
                    groups.Add(group);
                    open.Push(group);
                    break;

                case StateKinds.Ends:
                    if (open.Count > 0) open.Pop().Closed = stated;
                    break;

                case StateKinds.State:
                    Said(stated, inside, Region(inside), nodes, known);
                    break;

                case StateKinds.Transition:
                    Stepped(stated, inside, Region(inside), nodes, known, steps);
                    break;

                case StateKinds.Concurrent:
                    Made.Divider(stated, inside, Region(inside), nodes, known);
                    regions[inside ?? string.Empty] = Region(inside) + 1;
                    break;

                case StateKinds.Note:
                    notes.Add(Noted(stated, notes.Count));
                    break;

                case StateKinds.Direction:
                    if (Wayward(Setting(stated, StateRoles.Towards)) is { } towards) ways[inside ?? string.Empty] = towards;
                    break;

                case StateKinds.ClassDef:
                    var declared = MermaidStyle.None.With(stated.Inner(MermaidKinds.Properties));
                    foreach (var name in StateGrammar.Styling.Classes(stated)) classes[name] = declared;
                    break;

                case StateKinds.Class:
                    foreach (var given in StateGrammar.Styling.Givens(stated))
                        taken.Add((StateGrammar.Styling.Ids(stated), given));
                    break;

                case StateKinds.Style:
                    written.Add((StateGrammar.Styling.Ids(stated), MermaidStyle.None.With(stated.Inner(MermaidKinds.Properties))));
                    break;

                case StateKinds.Click:
                    Clicked(stated, known);
                    break;

                case StateKinds.Hide:
                    plain = true;
                    break;
            }
        }

        // A composite state is drawn as the box round its own states rather than as a state of its own.
        var boxed = groups.Select(held => held.Id).Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);
        nodes.RemoveAll(node => boxed.Contains(node.Id));
        foreach (var id in boxed) known.Remove(id);

        foreach (var node in nodes) foreach (var name in node.Classes) taken.Add((new[] { node.Id }, name));

        var styles = MermaidStyling.Styles(known.Keys.Concat(groups.Select(held => held.Id)).Where(id => id.Length > 0),
                                           classes, taken, written);

        return new StateDiagram(block, StateConfig.Read(block.Config), ways.GetValueOrDefault(string.Empty, StateWay.Down), plain,
                                [.. nodes.Select(node => Frozen(node, styles))],
                                [.. steps.Select(step => step.Frozen())],
                                [.. groups.Select(held => Frozen(held, styles, ways))],
                                [.. notes.Select(note => note.Frozen())]);
    }

    // ── Reading the lines ───────────────────────────────────────────────────

    /// <summary>A state written on its own: what is drawn on it, what it is drawn as, and the class it is given.</summary>
    private static void Said(ContentPart stated, string? inside, int region, List<Made> nodes, Dictionary<string, Made> known)
    {
        if (stated.Inner(StateKinds.Named) is not { } named) return;

        var made = Gathered(named, inside, region, nodes, known);
        if (made is null) return;

    made.Said = Inner(stated, StateKinds.Said) ?? Inner(stated, MermaidKinds.Quoted) ?? made.Said;
        made.SaidHole = Hole(stated, StateKinds.Said) ?? Hole(stated, MermaidKinds.Quoted) ?? made.SaidHole;

        if (Setting(stated, StateRoles.Kind) is { Length: > 0 } drawn) made.Shape = Shaped(drawn);
    }

    /// <summary>A transition: the states either side of it, and what is written on it.</summary>
    private static void Stepped(ContentPart stated, string? inside, int region, List<Made> nodes, Dictionary<string, Made> known,
                                List<Joined> steps)
    {
        var named = stated.Children.Where(child => child.Kind == StateKinds.Named).ToList();
        if (named.Count < 2) return;

        var from = Gathered(named[0], inside, region, nodes, known, leaving: true);
        var to = Gathered(named[1], inside, region, nodes, known, leaving: false);
        if (from is null || to is null) return;

        steps.Add(new Joined(stated, from.Id, to.Id, steps.Count)
        {
            Said = Inner(stated, StateKinds.Said),
            SaidHole = Hole(stated, StateKinds.Said),
        });
    }

    /// <summary>
    /// The state a name says, made where it has not been written before. <c>[*]</c> is the dot its own scope starts or stops at,
    /// whichever way the transition it is written on runs — its own region's, where a composite state is divided into regions.
    /// </summary>
    private static Made? Gathered(ContentPart named, string? inside, int region, List<Made> nodes, Dictionary<string, Made> known,
                                  bool? leaving = null)
    {
        var name = named.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);
        if (name?.Words() is not { } words) return null;

        var id = words.Text;
        var shape = StateShape.Plain;

        if (id == Edge)
        {
            shape = leaving is false ? StateShape.Stop : StateShape.Start;
            id = Marker(shape, inside, region);
        }

        if (!known.TryGetValue(id, out var made))
        {
            made = new Made(named, id, inside, nodes.Count) { Shape = shape, Said = words, Region = region };
            if (shape is StateShape.Start or StateShape.Stop) made.Said = null;

            nodes.Add(made);
            known[id] = made;
        }

        if (named.Children.Where(child => child.Kind == MermaidKinds.Name).Skip(1).FirstOrDefault()?.Words() is { } given)
            made.Classes.Add(given.Text);

        return made;
    }

    /// <summary>
    /// What the dot a scope starts or stops at is called, which is what a <c>class</c> line styles it by. Each region past the first of
    /// a composite state has dots of its own, as Mermaid draws them.
    /// </summary>
    private static string Marker(StateShape shape, string? inside, int region)
    {
        var name = shape == StateShape.Start ? StateGrammar.Pseudo[0] : StateGrammar.Pseudo[1];

        return inside is null ? name : region > 1 ? $"{name}@{inside}#{region}" : $"{name}@{inside}";
    }

    /// <summary>A composite state opening.</summary>
    private static Held Opens(ContentPart stated, string? parent, int region, int order)
    {
        var named = stated.Inner(StateKinds.Named);
        var name = named?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);

        return new Held(stated, stated.Fact(StateRoles.Opened) ?? string.Empty, name.Words()?.Text ?? string.Empty, order)
        {
            Parent = parent,
            Region = region,

            // A composite state written with what is on it first is called by its id and drawn with those words.
            Said = Inner(stated, MermaidKinds.Quoted) ?? name.Words(),
            SaidHole = Hole(stated, MermaidKinds.Quoted),
        };
    }

    /// <summary>A note: the state it is beside, which side, and what it says.</summary>
    private static Marked Noted(ContentPart stated, int order)
    {
        var opens = stated.Inner(StateKinds.NoteOpens) ?? stated;
        var named = opens.Inner(StateKinds.Named) ?? stated.Inner(StateKinds.Named);
        var name = named?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);

        var said = new List<ContentPart>();
        if (Inner(opens, StateKinds.Said) is { } one) said.Add(one);
        if (Inner(stated, MermaidKinds.Quoted) is { } floating) said.Add(floating);

        foreach (var text in stated.SelfAndDescendants().Where(part => part.Kind == StateKinds.NoteText))
            if (text.Words() is { } words) said.Add(words);

        return new Marked(stated, name.Words()?.Text ?? string.Empty, order)
        {
            Left = string.Equals(Setting(opens, StateRoles.Side), "left", StringComparison.OrdinalIgnoreCase),
            Floating = stated.Inner(MermaidKinds.Quoted) is not null && opens.Inner(StateRoles.Side) is null,
            Said = said,
            SaidHole = Hole(opens, StateKinds.Said) ?? Hole(stated, MermaidKinds.Quoted),
        };
    }

    /// <summary>Where pressing a state leads, and what it says while pointed at.</summary>
    private static void Clicked(ContentPart stated, IReadOnlyDictionary<string, Made> known)
    {
        var named = stated.Inner(StateKinds.Named);
        var name = named?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);
        if (name.Words()?.Text is not { Length: > 0 } id || !known.TryGetValue(id, out var made)) return;

        var quoted = stated.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Quoted).ToList();
        if (quoted.Count > 0) made.Href = quoted[0].Words()?.Text;
        if (quoted.Count > 1) made.Tip = quoted[1].Words()?.Text;
    }

    // ── What the pieces say ─────────────────────────────────────────────────

    private static ContentPart? Inner(ContentPart stated, string kind) => stated.Inner(kind)?.Words();

    private static ContentPart? Hole(ContentPart stated, string kind) => stated.Inner(kind)?.Hole();

    

    private static string? Setting(ContentPart stated, string role) =>
        stated.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidKinds.Setting && part.Role == role)?.Text;

    private static string? Keyed(string? fact) => string.IsNullOrEmpty(fact) ? null : fact;

    private static StateShape Shaped(string drawn) => drawn.ToLowerInvariant() switch
    {
        "fork" => StateShape.Fork,
        "join" => StateShape.Join,
        "choice" => StateShape.Choice,
        _ => StateShape.Plain,
    };

    /// <summary>Which way a word lays a diagram out, or null where it lays it out no way at all.</summary>
    public static StateWay? Wayward(string? said) => said?.ToUpperInvariant() switch
    {
        "TB" or "TD" => StateWay.Down,
        "BT" => StateWay.Up,
        "LR" => StateWay.Right,
        "RL" => StateWay.Left,
        _ => null,
    };

    private static StateNode Frozen(Made made, IReadOnlyDictionary<string, MermaidStyle> styles) =>
        new(made.Part, made.Id, made.Said, made.Shape, made.Group, styles.GetValueOrDefault(made.Id, MermaidStyle.None), made.Order)
        {
            SaidHole = made.SaidHole,
            Href = made.Href,
            Tip = made.Tip,
            Region = made.Region,
        };

    private static StateGroup Frozen(Held held, IReadOnlyDictionary<string, MermaidStyle> styles,
                                     IReadOnlyDictionary<string, StateWay> ways) =>
        new(held.Part, held.Key, held.Id, held.Said, held.Parent, ways.TryGetValue(held.Key, out var way) ? way : null,
            styles.GetValueOrDefault(held.Id, MermaidStyle.None), held.Order)
        {
            SaidHole = held.SaidHole,
            Region = held.Region,
            Whole = new SourceSpan(held.Part.Start, (held.Closed?.End ?? held.Part.End) - held.Part.Start),
        };

    // ── What it is read into ────────────────────────────────────────────────

    /// <summary>A state being read, before everything said about it is gathered.</summary>
    private sealed class Made(ContentPart part, string id, string? group, int order)
    {
        public ContentPart Part { get; } = part;

        public string Id { get; } = id;

        public string? Group { get; } = group;

        public int Order { get; } = order;

        public StateShape Shape { get; set; } = StateShape.Plain;

        public ContentPart? Said { get; set; }

        public ContentPart? SaidHole { get; set; }

        public string? Href { get; set; }

        public string? Tip { get; set; }

        public List<string> Classes { get; } = [];

        public int Region { get; init; } = 1;

        /// <summary>The line dividing two regions of a composite state, which is a state of the layout and nothing else.</summary>
        public static void Divider(ContentPart stated, string? inside, int region, List<Made> nodes, Dictionary<string, Made> known)
        {
            var id = $"--@{nodes.Count}";
            var made = new Made(stated, id, inside, nodes.Count) { Shape = StateShape.Divider, Region = region };

            nodes.Add(made);
            known[id] = made;
        }
    }

    /// <summary>A transition being read.</summary>
    private sealed class Joined(ContentPart part, string from, string to, int order)
    {
        public ContentPart? Said { get; init; }

        public ContentPart? SaidHole { get; init; }

        public StateStep Frozen() => new(part, from, to, Said, order) { SaidHole = SaidHole };
    }

    /// <summary>A composite state being read.</summary>
    private sealed class Held(ContentPart part, string key, string id, int order)
    {
        public ContentPart Part { get; } = part;

        public string Key { get; } = key;

        public string Id { get; } = id;

        public int Order { get; } = order;

        public string? Parent { get; init; }

        public int Region { get; init; } = 1;

        public ContentPart? Said { get; init; }

        public ContentPart? SaidHole { get; init; }

        /// <summary>The <c>}</c> that closed it, or null for one nothing closes.</summary>
        public ContentPart? Closed { get; set; }
    }

    /// <summary>A note being read.</summary>
    private sealed class Marked(ContentPart part, string of, int order)
    {
        public bool Left { get; init; }

        public bool Floating { get; init; }

        public IReadOnlyList<ContentPart> Said { get; init; } = [];

        public ContentPart? SaidHole { get; init; }

        public StateNote Frozen() => new(part, of, Left, Said, order) { SaidHole = SaidHole, Floating = Floating };
    }
}
