using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.State.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.State;

/// <summary>
/// What a <c>stateDiagram</c> block says — or a <c>stateDiagram-v2</c> one, which Mermaid reads the same way: the states, the
/// transitions between them, the composite states those are gathered into, the notes written beside them, and the lines that lay it
/// out and style it.
///
/// <para>
/// The rules are Mermaid's. A state is an id — <c>[*]</c> for where the diagram starts and stops — said again wherever it is used,
/// and what is written on one comes after a colon, or in quotes before an <c>as</c>. A transition is an arrow between two of them.
/// A composite state opens with a brace and closes with one of its own, so what is inside it is a fact about the whole block rather
/// than about any one line (<see cref="ResolveComposites"/>), and so is what a note written across several lines says
/// (<see cref="ResolveNotes"/>).
/// </para>
/// <para>
/// <strong>An id holds no dash.</strong> Mermaid's own rule, and what lets <c>a-->b</c> be read with no space in it: the arrow ends
/// the id before it, which is why a state may not be called <c>left-hand</c> where a flowchart's node may.
/// </para>
/// </summary>
public sealed class StateGrammar : IMermaidGrammar
{
    public const string StateWord = "state";
    public const string NoteWord = "note";
    public const string EndWord = "end";
    public const string AsWord = "as";
    public const string OfWord = "of";
    public const string DirectionWord = "direction";
    public const string ClickWord = "click";
    public const string HrefWord = "href";
    public const string HideWord = "hide";
    public const string ScaleWord = "scale";
    public const string WidthWord = "width";
    public const string EmptyWord = "empty";
    public const string DescriptionWord = "description";

    /// <summary>The arrow a transition is drawn as, which is the only one Mermaid writes here.</summary>
    public const string Arrow = "-->";

    /// <summary>What divides a composite state into regions running at the same time.</summary>
    public const string Divider = "--";

    /// <summary>What opens a composite state's own states, and what closes them.</summary>
    public const string Opening = "{";
    public const string Closing = "}";

    /// <summary>What gives a state a class where it is named.</summary>
    public const string Given = ":::";

    /// <summary>The ways a diagram, or a composite state in it, is laid out.</summary>
    public static readonly string[] Ways = ["TB", "TD", "BT", "RL", "LR"];

    /// <summary>What a state is drawn as where it is written with one of <see cref="Marks"/> round it.</summary>
    public static readonly string[] Drawn = ["fork", "join", "choice"];

    /// <summary>Which side of a state a note is written beside.</summary>
    public static readonly string[] Sides = ["left", "right"];

    /// <summary>
    /// What Mermaid calls the states <c>[*]</c> stands for, which a <c>class</c> or a <c>style</c> line may name though neither is
    /// written anywhere: a diagram has a start and a stop whether or not it says so.
    /// </summary>
    public static readonly string[] Pseudo = ["start", "end"];

    /// <summary>What a state's drawing is written between — Mermaid reads both.</summary>
    public static readonly (string Open, string Close)[] Marks = [("<<", ">>"), ("[[", "]]")];

    /// <summary>Where the link a <c>click</c> line writes opens.</summary>
    public static readonly string[] Targets = ["_self", "_blank", "_parent", "_top"];

    /// <summary>
    /// The characters a bare id ends at: the colon a description or a class opens with, the dash an arrow is drawn with, the braces
    /// a composite state is written between, the angles a drawing is written between, the comma between ids, the semicolon closing a
    /// line, and the quote a name in quotes opens with. A bracket is not one of them — <c>[*]</c> is what the diagram starts and
    /// stops at, and a drawing written <c>[[fork]]</c> is read after the space that ends the name before it.
    /// </summary>
    public const string Stops = ":;,-{}<>\" \t\r\n";

    /// <summary>The <c>classDef</c>, <c>class</c> and <c>style</c> lines, which every diagram with them reads the one way.</summary>
    public static readonly MermaidStyling Styling = new(Bare, StateRoles.Id, StateRoles.Class, "state");

    private const string StateShape = "A state is an id, an id and what is written on it, or state \"Written on it\" as id.";
    private const string TransitionShape = "A transition joins two states: a --> b, or a --> b : what it says.";
    private const string CompositeShape = "A composite state opens with state id { and closes with }.";
    private const string DrawnShape = "A state is drawn as <<fork>>, <<join>> or <<choice>>.";
    private const string DividerShape = "Regions of a composite state running at the same time are divided by -- alone on its line.";
    private const string NoteShape = "A note is written note left of id : what it says, or note \"What it says\" as id.";
    private const string NoteEndShape = "A note written across several lines is closed by end note.";
    private const string DirectionShape = "A direction line lays out the diagram, or the composite state it is in: direction LR.";
    private const string ClickShape = "A click line says where a state leads: click a \"https://example.com\" \"Tooltip\".";
    private const string HideShape = "A state with nothing written on it is drawn as its id alone by hide empty description.";
    private const string ScaleShape = "A scale line says how wide to draw it: scale 350 width.";

    /// <inheritdoc/>
    /// <remarks>Nothing follows the keyword: a state diagram is laid out by a <c>direction</c> line of its own.</remarks>
    public ContentNode? Header(string arguments)
    {
        var line = MermaidLine.Of(arguments);
        if (line.Done) return null;

        return ContentNode.Shown(arguments, "Nothing follows stateDiagram — direction LR lays it out.", MermaidRoles.Arguments);
    }

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        if (line.Past == '}') return Shut(line);
        if (line.Written.Trim() == Divider) return Divided(line);

        return MermaidLine.Keyword(line.Written, Bare,
                                   [StateWord, NoteWord, EndWord, DirectionWord, ClickWord, HideWord, ScaleWord,
                                    .. MermaidStyling.Words]) switch
        {
            StateWord => Stated(line),
            NoteWord => Noted(line),
            EndWord => Ended(line),
            DirectionWord => Towards(line),
            ClickWord => Clicked(line),
            HideWord => Hidden(line),
            ScaleWord => Scaled(line),
            MermaidStyling.ClassDefWord => Styling.Defined(line, StateKinds.ClassDef),
            MermaidStyling.ClassWord => Styling.Applied(line, StateKinds.Class),
            MermaidStyling.StyleWord => Styling.Styled(line, StateKinds.Style),
            _ => Related(line),
        };
    }

    /// <inheritdoc/>
    /// <remarks>A note saying nothing on its own, whose words are written on the lines under it until an <c>end note</c>.</remarks>
    public IEnumerable<MermaidStretch> Stretches =>
        [new MermaidStretch(StateKinds.Note, Noting, Ending, Within, Ended, "This note is never closed: end note closes it.")];

    /// <summary>A <c>note</c> line saying nothing on its own, which opens a note written on the lines under it.</summary>
    private static ContentNode? Noting(string text) =>
        MermaidLine.Keyword(text, Bare, NoteWord) is not null && Noted(MermaidLine.Of(text)) is { Kind: StateKinds.NoteOpens } note
            ? note
            : null;

    private static bool Ending(string text) => Ended(text).Kind == StateKinds.NoteEnds;

    /// <summary>A line inside a note: what the note says, whatever it would read as on its own.</summary>
    private static ContentNode Within(string text) =>
        ContentNode.Branch(StateKinds.NoteText, [ContentNode.Leaf(MermaidKinds.Words, text, StateRoles.Label)], StateRoles.Label);

    /// <inheritdoc cref="Ended(MermaidLine)"/>
    private static ContentNode Ended(string text) => Ended(MermaidLine.Of(text));

    /// <inheritdoc/>
    /// <remarks>
    /// What is written on a state or a transition runs to the end of its line and holds anything but a comment. An id, a class and a
    /// way are written bare and cannot be quoted at all, so what they cannot hold is dropped.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        if (part.Role is StateRoles.Id or StateRoles.Class) return MermaidWriting.Only(caret, text, Bare);
        if (part.Role is StateRoles.Towards or StateRoles.Kind or StateRoles.Side)
            return MermaidWriting.Only(caret, text, char.IsAsciiLetter);

        if (part.Role is StateRoles.Width) return MermaidWriting.Only(caret, text, char.IsAsciiDigit);

        return null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A state is declared where it is first written and used wherever it is written again — either end of a transition, a
    /// <c>class</c> line, a <c>style</c> line, the state a note is written beside. What is written inside a note names nothing, a
    /// note's own words being read as words rather than as a name.
    /// </remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var said = new Dictionary<string, List<ContentPart>>(StringComparer.Ordinal);

        foreach (var name in block.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Name))
        {
            if (name.Words() is not { Role: StateRoles.Id, Length: > 0 } words) continue;

            if (!said.TryGetValue(words.Text, out var places)) said[words.Text] = places = [];
            places.Add(name);
        }

        return [.. said.Select(name => new MermaidName(name.Key, name.Value[0], [.. name.Value.Skip(1)]))];
    }

    /// <inheritdoc/>
    /// <remarks>An id is written bare, so what an id cannot hold is dropped.</remarks>
    public string Naming(string name) => new([.. name.Where(Bare)]);

    /// <inheritdoc/>
    /// <remarks>
    /// Which composite state each line is in (<see cref="ResolveComposites"/>), and whether what is styled is written at all
    /// (<see cref="ResolveStyles"/>).
    /// </remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block) => [new ResolveComposites(), new ResolveStyles()];

    /// <inheritdoc/>
    /// <remarks>Between a name's quotes, and where what is written on a state is still to come after its colon.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) => node.Kind is MermaidKinds.Quoted or StateKinds.Said;

    /// <summary>Whether a character carries a bare id, a class or a way on.</summary>
    public static bool Bare(char character) => !Stops.Contains(character, StringComparison.Ordinal);

    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>A state on its own or given what is written on it, and a transition from one state to another.</summary>
    private static ContentNode Related(MermaidLine line)
    {
        line.Room();
        if (!Named(line)) return line.Shown(StateShape);

        var mark = line.Save();
        line.Space();

        if (line.Token(Arrow, StateRoles.Arrow))
        {
            line.Space();
            if (!Named(line)) return line.Shown(TransitionShape);

            Says(line);
            return Closed(line, StateKinds.Transition, TransitionShape);
        }

        line.Restore(mark);
        Says(line);

        return Closed(line, StateKinds.State, StateShape);
    }

    /// <summary>A <c>state</c> line: a composite state, a state written with what is on it first, or one drawn as a fork or a choice.</summary>
    private static ContentNode Stated(MermaidLine line)
    {
        line.Word(StateWord, letter: Bare);
        line.Room();

        // state "What is written on it" as id
        if (line.Next == '"')
        {
            if (!line.Quoted(StateRoles.Label)) return line.Shown(StateShape);

            line.Room();
            if (!line.Word(AsWord, letter: Bare)) return line.Shown(StateShape);

            line.Room();
            if (!Named(line)) return line.Shown(StateShape);

            return Opened(line);
        }

        if (!Named(line)) return line.Shown(StateShape);

        var mark = line.Save();
        line.Space();

        if (Marked(line)) return Closed(line, StateKinds.State, DrawnShape);

        line.Restore(mark);
        return Opened(line);
    }

    /// <summary>What a state is drawn as, where it is written after the state's name.</summary>
    private static bool Marked(MermaidLine line)
    {
        foreach (var (open, close) in Marks)
        {
            if (!line.Sees(open)) continue;

            line.Token(open, Roles.Open);
            line.Setting(StateRoles.Kind, Sorted, until: close);
            line.Token(close, Roles.Close);

            return true;
        }

        return false;
    }

    /// <summary>The brace opening a composite state's own states, or nothing where the state stands on its own.</summary>
    private static ContentNode Opened(MermaidLine line)
    {
        var mark = line.Save();
        line.Space();

        if (!line.Token(Opening, Roles.Open))
        {
            line.Restore(mark);
            return Closed(line, StateKinds.State, StateShape);
        }

        line.Space();
        return line.Done ? line.Read(StateKinds.Opens) : line.Shown(CompositeShape);
    }

    /// <summary>The brace closing a composite state.</summary>
    private static ContentNode Shut(MermaidLine line)
    {
        line.Room();
        line.Token(Closing, Roles.Close);

        return Closed(line, StateKinds.Ends, CompositeShape);
    }

    /// <summary>The <c>--</c> dividing a composite state into regions running at the same time.</summary>
    private static ContentNode Divided(MermaidLine line)
    {
        line.Room();
        line.Token(Divider, StateRoles.Arrow);

        return Closed(line, StateKinds.Concurrent, DividerShape);
    }

    /// <summary>A note beside a state, or one floating with a name of its own.</summary>
    private static ContentNode Noted(MermaidLine line)
    {
        line.Word(NoteWord, letter: Bare);
        line.Room();

        // note "What it says" as id
        if (line.Next == '"')
        {
            if (!line.Quoted(StateRoles.Label)) return line.Shown(NoteShape);

            line.Room();
            if (!line.Word(AsWord, letter: Bare)) return line.Shown(NoteShape);

            line.Room();
            if (!Named(line)) return line.Shown(NoteShape);

            return Closed(line, StateKinds.Note, NoteShape);
        }

        line.Setting(StateRoles.Side, Sided, until: " \t");
        line.Room();

        if (!line.Word(OfWord, letter: Bare)) return line.Shown(NoteShape);

        line.Room();
        if (!Named(line)) return line.Shown(NoteShape);

        // A note saying nothing on its own is written on the lines under it, until an end note closes it.
        return Closed(line, Says(line) ? StateKinds.Note : StateKinds.NoteOpens, NoteShape);
    }

    /// <summary>The <c>end note</c> closing a note written across several lines.</summary>
    private static ContentNode Ended(MermaidLine line)
    {
        line.Word(EndWord, letter: Bare);
        line.Room();

        if (!line.Word(NoteWord, letter: Bare)) return line.Shown(NoteEndShape);

        return Closed(line, StateKinds.NoteEnds, NoteEndShape);
    }

    /// <summary>The way the diagram, or the composite state this line is in, is laid out.</summary>
    private static ContentNode Towards(MermaidLine line)
    {
        line.Word(DirectionWord, letter: Bare);
        line.Room();
        line.Setting(StateRoles.Towards, Wayward, until: Stops);

        return Closed(line, StateKinds.Direction, DirectionShape);
    }

    /// <summary>Where pressing a state leads, and what it says while pointed at.</summary>
    private static ContentNode Clicked(MermaidLine line)
    {
        line.Word(ClickWord, letter: Bare);
        line.Room();

        if (!Named(line)) return line.Shown(ClickShape);

        line.Room();
        if (line.Word(HrefWord, letter: Bare)) line.Room();

        if (!line.Quoted(StateRoles.Href)) return line.Shown(ClickShape);

        var mark = line.Save();
        line.Room();

        if (line.Next == '"' && !line.Quoted(StateRoles.Tip)) return line.Shown(ClickShape);
        if (line.Next != '"' && !line.Done) line.Restore(mark);

        return Closed(line, StateKinds.Click, ClickShape);
    }

    /// <summary>A state with nothing written on it drawn as its id alone.</summary>
    private static ContentNode Hidden(MermaidLine line)
    {
        line.Word(HideWord, letter: Bare);
        line.Room();

        if (!line.Word(EmptyWord, letter: Bare)) return line.Shown(HideShape);

        line.Room();
        if (!line.Word(DescriptionWord, letter: Bare)) return line.Shown(HideShape);

        return Closed(line, StateKinds.Hide, HideShape);
    }

    /// <summary>How wide to draw it, which Mermaid keeps from its first renderer.</summary>
    private static ContentNode Scaled(MermaidLine line)
    {
        line.Word(ScaleWord, letter: Bare);
        line.Room();
        line.Amount(StateRoles.Width, MermaidNumber.Positive(ScaleShape), until: Stops);
        line.Room();

        if (!line.Word(WidthWord, letter: Bare)) return line.Shown(ScaleShape);

        return Closed(line, StateKinds.Scale, ScaleShape);
    }

    // ── The pieces a line is made of ────────────────────────────────────────

    /// <summary>One state where it is named: its id, and the class <c>:::</c> gives it.</summary>
    private static bool Named(MermaidLine line)
    {
        var mark = line.Save();
        line.Open();

        if (!line.Name(StateRoles.Id, Bare)) return Back(line, mark);

        if (line.Sees(Given))
        {
            line.Token(Given);
            if (!line.Name(StateRoles.Class, Bare)) return Back(line, mark);
        }

        line.Close(StateKinds.Named, StateRoles.Id);
        return true;
    }

    /// <summary>What is written on a state or a transition, after the colon opening it — whether anything is.</summary>
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
        line.Words(StateRoles.Label);
        line.Close(StateKinds.Said, StateRoles.Label);

        return true;
    }

    /// <summary>The semicolon and the space a line may end with, and what is wrong where anything else is written there.</summary>
    private static ContentNode Closed(MermaidLine line, string kind, string shape)
    {
        line.Space();
        line.Token(";");
        line.Space();

        return line.Done ? line.Read(kind) : line.Shown(shape);
    }

    private static bool Back(MermaidLine line, MermaidLine.Mark mark)
    {
        var why = line.Reason;
        line.Restore(mark);
        if (why is not null) line.Fail(why);

        return false;
    }

    private static string? Wayward(string said) =>
        Ways.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A state diagram is laid out TB, TD, BT, RL or LR.";

    private static string? Sorted(string said) =>
        Drawn.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : DrawnShape;

    private static string? Sided(string said) =>
        Sides.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A note is written left of or right of the state it is beside.";
}
