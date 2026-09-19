using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Requirement.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Requirement;

/// <summary>
/// What a <c>requirementDiagram</c> block says: the requirements written in it, the elements that meet them, the fields inside
/// each, what holds between them, and the lines that lay it out and style it.
///
/// The rules are SysML's, as Mermaid writes them. A requirement is a kind, a name and its fields between braces, which makes it
/// a statement written across several lines (<see cref="MermaidStretch"/>); a relation is two names with the word for what holds
/// between them written on the line joining them, either way round.
///
/// <strong>A name ends at the punctuation a relation is drawn with</strong>, so one holding a dash, a brace or a colon is
/// written in quotes — which is how Mermaid writes a name holding anything at all.
/// </summary>
public sealed class RequirementGrammar : IMermaidGrammar
{
    /// <summary>The kinds of requirement SysML names, each drawn in guillemets over the name.</summary>
    public static readonly string[] Requirements =
    [
        "requirement", "functionalRequirement", "interfaceRequirement",
        "performanceRequirement", "physicalRequirement", "designConstraint",
    ];

    /// <summary>What a thing the requirements are about is written as.</summary>
    public const string ElementWord = "element";

    public const string DirectionWord = "direction";

    /// <summary>What opens the fields of a requirement or an element, and what closes them.</summary>
    public const string Opening = "{";
    public const string Closing = "}";

    /// <summary>What gives something a class where it is named.</summary>
    public const string Given = ":::";

    /// <summary>The line a relation is drawn with, and the arrow at the end it reaches.</summary>
    public const string Joins = "-";
    public const string Onward = "->";

    /// <summary>The arrow of a relation written the other way round, with the end it reaches named first.</summary>
    public const string Backward = "<-";

    /// <summary>What may hold between two of them, drawn on the line joining them.</summary>
    public static readonly string[] Holdings =
        ["contains", "copies", "derives", "satisfies", "verifies", "refines", "traces"];

    /// <summary>The one of them drawn as a whole and its parts rather than as an arrow.</summary>
    public const string Holding = "contains";

    /// <summary>How much of a risk it is that a requirement is not met.</summary>
    public static readonly string[] Risks = ["low", "medium", "high"];

    /// <summary>How it is shown that one is met.</summary>
    public static readonly string[] Methods = ["analysis", "inspection", "test", "demonstration"];

    /// <summary>The ways a diagram is laid out.</summary>
    public static readonly string[] Ways = ["TB", "TD", "BT", "RL", "LR"];

    /// <summary>The fields a requirement holds.</summary>
    public const string IdKey = "id";
    public const string TextKey = "text";
    public const string RiskKey = "risk";
    public const string MethodKey = "verifymethod";

    /// <summary>And the fields an element holds.</summary>
    public const string TypeKey = "type";
    public const string RefKey = "docref";

    public static readonly string[] Keys = [IdKey, TextKey, RiskKey, MethodKey, TypeKey, RefKey];

    /// <summary>
    /// The characters a bare name ends at: the colon a field and a class are written after, the punctuation a relation is drawn
    /// with, the braces fields are written between, the comma between names, the semicolon closing a line, and the quotes a name
    /// holding any of them is written in.
    /// </summary>
    public const string Stops = ":;,-{}<>\" \t\r\n";

    /// <summary>The <c>classDef</c>, <c>class</c> and <c>style</c> lines, which every diagram with them reads the one way.</summary>
    public static readonly MermaidStyling Styling = new(Bare, RequirementRoles.Id, RequirementRoles.Class, "requirement");

    /// <summary>The words a block may open with: the kinds of requirement, and the element.</summary>
    private static readonly string[] Boxes = [.. Requirements, ElementWord];

    /// <summary>Every word a line may open with: what a block is, and the lines that lay the diagram out and style it.</summary>
    private static readonly string[] Openers =
    [
        .. Boxes, DirectionWord,
        MermaidStyling.ClassDefWord, MermaidStyling.ClassWord, MermaidStyling.StyleWord,
    ];

    private const string BlockShape = "A requirement is written requirement Foo {, its fields, then the } closing them.";
    private const string FieldShape = "A field is written id: 1, text: what it asks for, risk: high or verifymethod: test.";
    private const string RelationShape = "A relation joins two of them: A - satisfies -> B, or B <- satisfies - A.";
    private const string DirectionShape = "A direction line lays the diagram out: direction LR.";
    private const string StrayShape = "A } closes the fields a requirement opened with {.";

    /// <inheritdoc/>
    /// <remarks>Nothing follows the keyword: a requirement diagram is laid out by a <c>direction</c> line of its own.</remarks>
    public ContentNode? Header(string arguments)
    {
        var line = MermaidLine.Of(arguments);
        if (line.Done) return null;

        return ContentNode.Shown(arguments, "Nothing follows requirementDiagram — direction LR lays it out.", MermaidRoles.Arguments);
    }

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        if (line.Written.Trim() is Closing or Closing + ";") return line.Shown(StrayShape);

        return MermaidLine.Keyword(line.Written, Bare, Openers) switch
        {
            null => Related(line),
            DirectionWord => Towards(line),
            MermaidStyling.ClassDefWord => Styling.Defined(line, RequirementKinds.ClassDef),
            MermaidStyling.ClassWord => Styling.Applied(line, RequirementKinds.CssClass),
            MermaidStyling.StyleWord => Styling.Styled(line, RequirementKinds.Style),
            var word => Declared(line, word),
        };
    }

    /// <inheritdoc/>
    /// <remarks>A requirement or an element, whose fields are written on the lines between its braces.</remarks>
    public IEnumerable<MermaidStretch> Stretches =>
        [new MermaidStretch(RequirementKinds.Block, Begun, Shutting, Within, Shutter, "These fields are never closed: } closes them.")];

    /// <inheritdoc/>
    /// <remarks>
    /// Under a field, nothing in particular: the fields are the block's own, and a line opening another here would swallow
    /// everything under it. Everywhere else a relation, which is the whole of what a requirement diagram writes to one line.
    /// </remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => above?.Kind switch
    {
        RequirementKinds.Field or RequirementKinds.Opens or RequirementKinds.Shut => null,
        RequirementKinds.Direction or RequirementKinds.ClassDef or RequirementKinds.CssClass or RequirementKinds.Style => null,
        _ => ("\"\" - satisfies -> \"\"", 1),
    };

    /// <inheritdoc/>
    /// <remarks>
    /// A field's value runs to the end of its line and holds anything but a comment. A name and a class are written bare — in
    /// quotes, where one holds what a bare name cannot — and a way is letters.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        if (part.Role is RequirementRoles.Id or RequirementRoles.Class) return MermaidWriting.Only(caret, text, Bare);
        if (part.Role is RequirementRoles.Towards) return MermaidWriting.Only(caret, text, char.IsAsciiLetter);

        return null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A requirement is declared where its block opens and used wherever it is named again — either end of a relation, a
    /// <c>class</c> line, a <c>style</c> line.
    /// </remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var said = new Dictionary<string, List<ContentPart>>(StringComparer.Ordinal);

        foreach (var name in block.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Name))
        {
            if (name.Words() is not { Role: RequirementRoles.Id, Length: > 0 } words) continue;

            if (!said.TryGetValue(words.Text, out var places)) said[words.Text] = places = [];
            places.Add(name);
        }

        return [.. said.Select(name => new MermaidName(name.Key, name.Value[0], [.. name.Value.Skip(1)]))];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A name carried to every use of it has to read in all of them, and a <c>style</c> line names one bare, so what a bare name
    /// cannot hold is dropped rather than quoted.
    /// </remarks>
    public string Naming(string name) => new([.. name.Where(Bare)]);

    /// <inheritdoc/>
    /// <remarks>Whether what is styled is written at all (<see cref="ResolveStyles"/>).</remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block) => [new ResolveStyles()];

    /// <inheritdoc/>
    /// <remarks>
    /// Between a name's quotes, and where a field's value is still to come after its colon. A name with nothing in it stands for
    /// a box of its own while it is written, so the hole in it is where the writing goes.
    /// </remarks>
    public bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind is MermaidKinds.Quoted or MermaidKinds.Name or RequirementKinds.Value;

    /// <summary>Whether a character carries a bare name or a class name on.</summary>
    public static bool Bare(char character) => !Stops.Contains(character, StringComparison.Ordinal);

    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>The line opening a requirement or an element: what kind of thing it is, what it is called, and the brace.</summary>
    private static ContentNode Declared(MermaidLine line, string word)
    {
        line.Word(word, MermaidKinds.Key, RequirementRoles.Kind, Bare);
        line.Room();

        if (!Named(line)) return line.Shown(BlockShape);

        line.Room();
        if (!line.Token(Opening, Roles.Open)) return line.Shown(BlockShape);

        line.Space();
        return line.Done ? line.Read(RequirementKinds.Opens) : line.Shown(BlockShape);
    }

    /// <summary>A relation between two of them, written either way round.</summary>
    private static ContentNode Related(MermaidLine line)
    {
        line.Room();
        if (!Named(line)) return line.Shown(RelationShape);

        line.Space();

        // A name on a line of its own writes the box, which is where ::: gives one a class.
        if (line.Done || line.Sees(";")) return Closed(line, RequirementKinds.Naming, RelationShape);

        // Written the other way round, the end the relation reaches is named first — which is what the <- says.
        var back = line.Sees(Backward);
        if (!line.Token(back ? Backward : Joins, RequirementRoles.Arrow)) return line.Shown(RelationShape);

        line.Room();
        line.Setting(RequirementRoles.Says, Between, until: Stops);
        line.Room();

        if (!line.Token(back ? Joins : Onward, RequirementRoles.Arrow)) return line.Shown(RelationShape);

        line.Room();
        if (!Named(line)) return line.Shown(RelationShape);

        return Closed(line, RequirementKinds.Relation, RelationShape);
    }

    /// <summary>The way the diagram is laid out.</summary>
    private static ContentNode Towards(MermaidLine line)
    {
        line.Word(DirectionWord, letter: Bare);
        line.Room();
        line.Setting(RequirementRoles.Towards, Wayward, until: Stops);

        return Closed(line, RequirementKinds.Direction, DirectionShape);
    }

    // ── The fields between the braces ───────────────────────────────────────

    /// <summary>A line opening a block, which is what says the lines under it are one statement with it.</summary>
    private static ContentNode? Begun(string text) =>
        MermaidLine.Keyword(text, Bare, Boxes) is { } word
        && Declared(MermaidLine.Of(text), word) is { Kind: RequirementKinds.Opens } opened
            ? opened
            : null;

    private static bool Shutting(string text) => MermaidLine.Of(text).Written.Trim() is Closing or Closing + ";";

    /// <summary>A line between the braces: one of the fields a requirement or an element holds, and what it is set to.</summary>
    private static ContentNode Within(string text)
    {
        var line = MermaidLine.Of(text);
        line.Room();

        // A line with nothing written on it yet is one a field is still to be written on.
        if (line.Done) return line.Read(RequirementKinds.Field);

        if (MermaidLine.Keyword(line.Rest, Bare, Keys) is not { } key) return line.Shown(FieldShape);

        line.Word(key, MermaidKinds.Key, RequirementRoles.Key, Bare);
        line.Space();

        if (!line.Token(":", Roles.Open)) return line.Shown(FieldShape);

        line.Open();
        line.Room();
        Valued(line, key);
        line.Close(RequirementKinds.Value, RequirementRoles.Value);

        return line.Done ? line.Read(RequirementKinds.Field) : line.Shown(FieldShape);
    }

    private static ContentNode Shutter(string text)
    {
        var line = MermaidLine.Of(text);

        line.Room();
        line.Token(Closing, Roles.Close);

        return Closed(line, RequirementKinds.Shut, StrayShape);
    }

    /// <summary>
    /// What a field is set to: one of the few words a risk and a verification method may be, and otherwise anything at all — in
    /// quotes, where it is written in them, which is how Mermaid writes one with markdown in it.
    /// </summary>
    private static void Valued(MermaidLine line, string key)
    {
        if (string.Equals(key, RiskKey, StringComparison.OrdinalIgnoreCase))
        {
            line.Setting(RequirementRoles.Value, Risked);
            return;
        }

        if (string.Equals(key, MethodKey, StringComparison.OrdinalIgnoreCase))
        {
            line.Setting(RequirementRoles.Value, Verified);
            return;
        }

        if (line.Next == '"')
        {
            var mark = line.Save();
            if (line.Quoted(RequirementRoles.Value)) return;

            line.Restore(mark);
        }

        line.Words(RequirementRoles.Value);
    }

    // ── The pieces a line is made of ────────────────────────────────────────

    /// <summary>One requirement where it is named: what it is called, and the class <c>:::</c> gives it.</summary>
    private static bool Named(MermaidLine line)
    {
        var mark = line.Save();
        line.Open();

        if (!line.Name(RequirementRoles.Id, Bare)) return Back(line, mark);

        if (line.Sees(Given))
        {
            line.Token(Given);
            if (!line.Name(RequirementRoles.Class, Bare)) return Back(line, mark);
        }

        line.Close(RequirementKinds.Named, RequirementRoles.Id);
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

    private static string? Between(string said) =>
        Holdings.Contains(said, StringComparer.OrdinalIgnoreCase)
            ? null
            : $"What holds between two requirements is {string.Join(", ", Holdings.SkipLast(1))} or {Holdings[^1]}.";

    private static string? Risked(string said) =>
        Risks.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A risk is low, medium or high.";

    private static string? Verified(string said) =>
        Methods.Contains(said, StringComparer.OrdinalIgnoreCase)
            ? null
            : "A requirement is verified by analysis, inspection, test or demonstration.";

    private static string? Wayward(string said) =>
        Ways.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A requirement diagram is laid out TB, TD, BT, RL or LR.";
}
