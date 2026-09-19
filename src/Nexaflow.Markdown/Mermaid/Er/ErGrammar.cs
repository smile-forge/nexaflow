using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Er.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Er;

/// <summary>
/// What an <c>erDiagram</c> block says: the entities written in it, the attributes inside them, how many of each entity the
/// other has at either end of a relationship, the subgraphs they are boxed into, and the lines that lay it out and style it.
///
/// The rules are Mermaid's. An entity is a name, said again wherever it is used, with its attributes written between braces —
/// which makes that a statement written across several lines (<see cref="MermaidStretch"/>). A relationship is two entities
/// with how many of each the other has written between them, as a pair of symbols or in words.
///
/// <strong>What a name may hold is Mermaid's own rule</strong>: letters and digits, the dash, the dot, the underscore and the
/// star, and anything outside ASCII. A name holding anything else is written in quotes.
/// </summary>
public sealed class ErGrammar : IMermaidGrammar
{
    public const string SubgraphWord = "subgraph";
    public const string EndWord = "end";
    public const string DirectionWord = "direction";

    /// <summary>What a relationship that identifies what it reaches may be written as, rather than <c>--</c>.</summary>
    public const string ToWord = "to";

    /// <summary>And what one that does not may be written as, rather than <c>..</c>.</summary>
    public const string OptionallyWord = "optionally to";

    /// <summary>What opens an entity's attributes, and what closes them.</summary>
    public const string Opening = "{";
    public const string Closing = "}";

    /// <summary>What gives an entity a class where it is named.</summary>
    public const string Given = ":::";

    /// <summary>
    /// How many of the entity at one end the other has, drawn as crow's feet. Each is written one way round at the near end and
    /// the other way round at the far end, and either is read at either end, as Mermaid's own lexer takes them.
    /// </summary>
    public static readonly string[] Cards = ["|o", "o|", "||", "}o", "o{", "}|", "|{"];

    /// <summary>The line between them: solid where the relationship identifies what it reaches, and dotted where it does not.</summary>
    public static readonly string[] Solid = ["--"];
    public static readonly string[] Dotted = ["..", "-.", ".-"];

    /// <summary>What either end may be written in words instead, longest first so <c>one or more</c> is not read as <c>one</c>.</summary>
    public static readonly string[] Counts =
        [.. new[]
        {
            "zero or one", "one or zero", "zero or more", "zero or many", "one or more", "one or many",
            "many(0)", "many(1)", "only one", "many", "0+", "1+", "one", "1",
        }.OrderByDescending(said => said.Length)];

    /// <summary>The keys an attribute may be.</summary>
    public static readonly string[] Keys = ["PK", "FK", "UK"];

    /// <summary>The ways a diagram is laid out.</summary>
    public static readonly string[] Ways = ["TB", "TD", "BT", "RL", "LR"];

    /// <summary>The <c>classDef</c>, <c>class</c> and <c>style</c> lines, which every diagram with them reads the one way.</summary>
    public static readonly MermaidStyling Styling = new(Bare, ErRoles.Id, ErRoles.Class, "entity");

    private const string EntityShape = "An entity is written CUSTOMER, CUSTOMER[\"The customer\"], or CUSTOMER { its attributes }.";
    private const string AttributeShape = "An attribute is written string name, or string name PK, FK \"what it is for\".";
    private const string RelationShape = "A relationship joins two entities: CUSTOMER ||--o{ ORDER : places.";
    private const string SubgraphShape = "A subgraph boxes the entities written between subgraph Name and end.";
    private const string DirectionShape = "A direction line lays the diagram out: direction LR.";
    private const string StrayShape = "A } closes the attributes an entity opened with {.";

    /// <inheritdoc/>
    /// <remarks>Nothing follows the keyword: an ER diagram is laid out by a <c>direction</c> line of its own.</remarks>
    public ContentNode? Header(string arguments)
    {
        var line = MermaidLine.Of(arguments);
        if (line.Done) return null;

        return ContentNode.Shown(arguments, "Nothing follows erDiagram — direction LR lays it out.", MermaidRoles.Arguments);
    }

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        if (line.Written.Trim() is Closing or Closing + ";") return line.Shown(StrayShape);

        return MermaidLine.Keyword(line.Written, Bare, [SubgraphWord, EndWord, DirectionWord, .. MermaidStyling.Words]) switch
        {
            SubgraphWord => Opened(line),
            EndWord => Ended(line),
            DirectionWord => Towards(line),
            MermaidStyling.ClassDefWord => Styling.Defined(line, ErKinds.ClassDef),
            MermaidStyling.ClassWord => Styling.Applied(line, ErKinds.CssClass),
            MermaidStyling.StyleWord => Styling.Styled(line, ErKinds.Style),
            _ => Joined(line),
        };
    }

    /// <inheritdoc/>
    /// <remarks>An entity whose attributes are written on the lines between its braces, until the <c>}</c> closing them.</remarks>
    public IEnumerable<MermaidStretch> Stretches =>
        [new MermaidStretch(ErKinds.Block, Begun, Shutting, Within, Shutter, "These attributes are never closed: } closes them.")];

    /// <inheritdoc/>
    /// <remarks>
    /// Under an attribute, nothing in particular — the attributes are the entity's own, and a line opening another here would
    /// swallow everything under it. Everywhere else a relationship, which is what an ER diagram is mostly made of.
    /// </remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => above?.Kind switch
    {
        ErKinds.Attribute or ErKinds.Opens or ErKinds.Shut => null,
        ErKinds.Direction or ErKinds.ClassDef or ErKinds.CssClass or ErKinds.Style => null,
        _ => ("\"\" ||--o{ \"\" : \"\"", 1),
    };

    /// <inheritdoc/>
    /// <remarks>
    /// What is written on a relationship and what an attribute says it is for hold anything but a comment. A name, a class, a
    /// type and a key are written bare — in quotes, where one holds what a bare name cannot — and a way is letters.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        if (part.Role is ErRoles.Id or ErRoles.Class or ErRoles.Field) return MermaidWriting.Only(caret, text, Bare);
        if (part.Role is ErRoles.Type) return MermaidWriting.Only(caret, text, Typed);
        if (part.Role is ErRoles.Key or ErRoles.Towards) return MermaidWriting.Only(caret, text, char.IsAsciiLetter);

        // A subgraph's name is written in words, and the brackets past it open the label drawn instead of it.
        if (part.Role is ErRoles.Space) return MermaidWriting.Only(caret, text, character => character is not ('[' or ']'));

        return null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// An entity is declared where it is first written and used wherever it is written again — either end of a relationship, a
    /// <c>class</c> line, a <c>style</c> line.
    /// </remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var said = new Dictionary<string, List<ContentPart>>(StringComparer.Ordinal);

        foreach (var name in block.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Name))
        {
            if (name.Words() is not { Role: ErRoles.Id, Length: > 0 } words) continue;

            if (!said.TryGetValue(words.Text, out var places)) said[words.Text] = places = [];
            places.Add(name);
        }

        return [.. said.Select(name => new MermaidName(name.Key, name.Value[0], [.. name.Value.Skip(1)]))];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A name carried to every use of it has to read in all of them, and a <c>style</c> line names one bare, so what a bare
    /// name cannot hold is dropped rather than quoted.
    /// </remarks>
    public string Naming(string name) => new([.. name.Where(Bare)]);

    /// <inheritdoc/>
    /// <remarks>
    /// Which subgraph each line is in (<see cref="ResolveGroups"/>), and whether what is styled is written at all
    /// (<see cref="ResolveStyles"/>).
    /// </remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block) => [new ResolveGroups(), new ResolveStyles()];

    /// <inheritdoc/>
    /// <remarks>Between a name's quotes, and where what is written on a relationship is still to come after its colon.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind is MermaidKinds.Quoted or MermaidKinds.Name or ErKinds.Said;

    /// <summary>
    /// Whether a character carries a bare name on: Mermaid's own rule — letters and digits, the dash, the dot, the underscore
    /// and the star, and anything outside ASCII, which is what makes a name in any language read.
    /// </summary>
    public static bool Bare(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '-' or '*' or '.' || character > '';

    /// <summary>Whether a character carries an attribute's type on, which holds the brackets an array or a size is written in.</summary>
    public static bool Typed(char character) =>
        Bare(character) || character is '(' or ')' or '[' or ']' or '?' or '~' or ',';

    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>An entity on its own, an entity opening its attributes, or a relationship between two of them.</summary>
    private static ContentNode Joined(MermaidLine line)
    {
        line.Room();
        if (!Named(line)) return line.Shown(EntityShape);

        line.Space();

        if (line.Token(Opening, Roles.Open))
        {
            line.Space();
            return line.Done ? line.Read(ErKinds.Opens) : line.Shown(EntityShape);
        }

        if (line.Done || line.Sees(";")) return line.Closed(ErKinds.Entity, EntityShape);

        if (!Counted(line)) return line.Shown(RelationShape);
        line.Room();

        if (!Lined(line)) return line.Shown(RelationShape);
        line.Room();

        if (!Counted(line)) return line.Shown(RelationShape);
        line.Room();

        if (!Named(line)) return line.Shown(RelationShape);

        Said(line);
        return line.Closed(ErKinds.Relation, RelationShape);
    }

    /// <summary>A subgraph, which boxes the entities written until the <c>end</c> closing it.</summary>
    private static ContentNode Opened(MermaidLine line)
    {
        line.Word(SubgraphWord, letter: Bare);
        line.Room();

        line.Open();
        line.Words(ErRoles.Space, until: "[");
        line.Close(MermaidKinds.Name, ErRoles.Space);

        // The name is read less the space before its label, which is the line's own rather than the name's.
        line.Space();
        if (line.Next == '[' && !line.Label("[", "]", ErRoles.Label)) return line.Shown(SubgraphShape);

        return line.Closed(ErKinds.Subgraph, SubgraphShape);
    }

    private static ContentNode Ended(MermaidLine line)
    {
        line.Word(EndWord, letter: Bare);

        return line.Closed(ErKinds.Ends, "A subgraph is closed by end, with nothing after it.");
    }

    /// <summary>The way the diagram is laid out.</summary>
    private static ContentNode Towards(MermaidLine line)
    {
        line.Word(DirectionWord, letter: Bare);
        line.Room();
        line.Setting(ErRoles.Towards, Wayward, until: " \t");

        return line.Closed(ErKinds.Direction, DirectionShape);
    }

    // ── The attributes between the braces ───────────────────────────────────

    /// <summary>A line opening an entity's attributes, which is what says the lines under it are one statement with it.</summary>
    private static ContentNode? Begun(string text) =>
        MermaidLine.Keyword(text, Bare, [SubgraphWord, EndWord, DirectionWord, .. MermaidStyling.Words]) is null
        && Joined(MermaidLine.Of(text)) is { Kind: ErKinds.Opens } opened
            ? opened
            : null;

    private static bool Shutting(string text) => MermaidLine.Of(text).Written.Trim() is Closing or Closing + ";";

    /// <summary>A line between the braces: what the attribute holds, what it is called, the keys it is, and what it is for.</summary>
    private static ContentNode Within(string text)
    {
        var line = MermaidLine.Of(text);
        line.Room();

        // A line with nothing written on it yet is one an attribute is still to be written on.
        if (line.Done) return line.Read(ErKinds.Attribute);

        if (!line.Name(ErRoles.Type, Typed)) return line.Shown(AttributeShape);

        line.Space();
        if (Rest(line) && !line.Name(ErRoles.Field, Bare)) return line.Shown(AttributeShape);

        line.Space();
        if (Rest(line) && !line.Names(Keyed, ErRoles.Key, "key")) return line.Shown(AttributeShape);

        line.Space();
        if (!line.Done && !line.Quoted(ErRoles.Comment, what: "comment")) return line.Shown(AttributeShape);

        return line.Closed(ErKinds.Attribute, AttributeShape);
    }

    private static ContentNode Shutter(string text)
    {
        var line = MermaidLine.Of(text);

        line.Room();
        line.Token(Closing, Roles.Close);

        return line.Closed(ErKinds.Shut, StrayShape);
    }

    /// <summary>Whether anything but the comment closing the line is still to be read.</summary>
    private static bool Rest(MermaidLine line) => !line.Done && line.Next != '"';

    private static bool Keyed(MermaidLine line) => line.Name(ErRoles.Key, char.IsAsciiLetter, Keying);

    // ── The pieces a line is made of ────────────────────────────────────────

    /// <summary>One entity where it is named: its name, the label drawn instead of it, and the classes <c>:::</c> gives it.</summary>
    private static bool Named(MermaidLine line)
    {
        var mark = line.Save();
        line.Open();

        if (!line.Name(ErRoles.Id, Bare)) return line.Undo(mark);

        if (line.Next == '[' && !line.Label("[", "]", ErRoles.Label)) return line.Undo(mark);

        if (line.Sees(Given))
        {
            line.Token(Given);
            if (!line.Names(Classed, ErRoles.Class, "class")) return line.Undo(mark);
        }

        line.Close(ErKinds.Named, ErRoles.Id);
        return true;
    }

    private static bool Classed(MermaidLine line) => line.Name(ErRoles.Class, Bare);

    /// <summary>How many of the entity at one end the other has: a pair of symbols, or the same said in words.</summary>
    private static bool Counted(MermaidLine line)
    {
        foreach (var mark in Cards)
            if (line.Token(mark, ErRoles.Card)) return true;

        foreach (var said in Counts)
            if (line.Word(said, MermaidKinds.Setting, ErRoles.Card, Bare)) return true;

        return line.Fail(RelationShape);
    }

    /// <summary>The line between them, which says whether the relationship identifies what it reaches.</summary>
    private static bool Lined(MermaidLine line)
    {
        foreach (var mark in Solid.Concat(Dotted))
            if (line.Token(mark, ErRoles.Arrow)) return true;

        if (line.Word(OptionallyWord, MermaidKinds.Setting, ErRoles.Arrow, Bare)) return true;
        if (line.Word(ToWord, MermaidKinds.Setting, ErRoles.Arrow, Bare)) return true;

        return line.Fail(RelationShape);
    }

    /// <summary>What a relationship is called, after the colon opening it — whether anything is written there.</summary>
    private static bool Said(MermaidLine line)
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

        if (line.Next == '"') line.Quoted(ErRoles.Said, what: "label");
        else line.Words(ErRoles.Said);

        line.Close(ErKinds.Said, ErRoles.Said);
        return true;
    }

    private static string? Keying(string said) =>
        Keys.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "An attribute is a PK, an FK or a UK.";

    private static string? Wayward(string said) =>
        Ways.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "An ER diagram is laid out TB, TD, BT, RL or LR.";
}
