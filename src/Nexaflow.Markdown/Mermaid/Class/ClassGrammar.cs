using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Class.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Class;

/// <summary>
/// What a <c>classDiagram</c> block says — or a <c>classDiagram-v2</c> one, which Mermaid reads the same way: the classes, the
/// members inside them, the relations between them, the namespaces they are boxed into, the notes beside them, and the lines that
/// lay it out and style it.
///
/// The rules are Mermaid's. A class is an id, said again wherever it is used; its members are written one to a line between braces
/// or one at a time after a colon, and the braces make that a statement written across several lines (<see cref="MermaidStretch"/>).
/// A relation is an operator between two classes, its ends saying what each draws and its line whether it is solid or dotted.
///
/// <strong>An id ends at the punctuation a relation is drawn with</strong>, which is what lets <c>A&lt;|--B</c> be read with no
/// space in it: a class called anything else is written in backticks, as Mermaid writes it.
/// </summary>
public sealed class ClassGrammar : IMermaidGrammar
{
    public const string ClassWord = "class";
    public const string NamespaceWord = "namespace";
    public const string NoteWord = "note";
    public const string ForWord = "for";
    public const string DirectionWord = "direction";
    public const string ClickWord = "click";
    public const string CallbackWord = "callback";
    public const string LinkWord = "link";
    public const string CssClassWord = "cssClass";
    public const string CallWord = "call";
    public const string HrefWord = "href";

    /// <summary>What opens a class's members or a namespace's classes, and what closes them.</summary>
    public const string Opening = "{";
    public const string Closing = "}";

    /// <summary>What gives a class a class where it is named.</summary>
    public const string Given = ":::";

    /// <summary>What a class's type parameters are written between, which are drawn between angle brackets.</summary>
    public const string Tilde = "~";

    /// <summary>What a class may be named between to hold anything a bare id cannot.</summary>
    public const string Backtick = "`";

    /// <summary>What an annotation is written between.</summary>
    public const string Opens = "<<";
    public const string Shuts = ">>";

    /// <summary>The ways a diagram is laid out.</summary>
    public static readonly string[] Ways = ["TB", "TD", "BT", "RL", "LR"];

    /// <summary>Where the link a <c>click</c> or a <c>link</c> line writes opens.</summary>
    public static readonly string[] Targets = ["_self", "_blank", "_parent", "_top"];

    /// <summary>What may be written at the near end of a relation, saying what that end draws.</summary>
    public static readonly string[] Heads = ["<|", "()", "*", "o", "<"];

    /// <summary>What may be written at the far end of one.</summary>
    public static readonly string[] Tails = ["|>", "()", "*", "o", ">"];

    /// <summary>The line a relation is drawn with: solid, or dotted.</summary>
    public static readonly string[] Lines = ["--", ".."];

    /// <summary>
    /// Every operator a relation may be written as — a line, with either end saying what it draws — longest first, so
    /// <c>--&gt;</c> is read as one operator rather than as a line and a class called <c>&gt;</c>.
    /// </summary>
    public static readonly string[] Operators =
        [.. Lines
            .SelectMany(line => Heads.Append(string.Empty)
                                     .SelectMany(head => Tails.Append(string.Empty).Select(tail => head + line + tail)))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(operation => operation.Length)];

    /// <summary>
    /// The characters a bare id ends at: the colon a member or a class opens with, the punctuation a relation is drawn with, the
    /// braces members and namespaces are written between, the brackets a label is written in, the tildes type parameters are
    /// written between, the comma between ids, the semicolon closing a line, and the marks a name may be written between.
    /// </summary>
    public const string Stops = ":;,-.{}[]<>()~*|\"` \t\r\n";

    /// <summary>The <c>classDef</c> and <c>style</c> lines, which every diagram with them reads the one way.</summary>
    public static readonly MermaidStyling Styling = new(Bare, ClassRoles.Id, ClassRoles.Class, "class");

    private const string ClassShape = "A class is declared class Foo, class Foo[\"Shown instead\"] or class Foo { its members }.";
    private const string BodyShape = "A class's members are written one to a line between { and }.";
    private const string MemberShape = "A member is written Foo : +int age, or between the braces of class Foo { }.";
    private const string RelationShape = "A relation joins two classes: A <|-- B, or A \"1\" --> \"*\" B : what it says.";
    private const string NamespaceShape = "A namespace boxes the classes written between namespace N { and }.";
    private const string AnnotationShape = "An annotation says what a class is: <<interface>> Shape.";
    private const string NoteShape = "A note is written note \"What it says\", or note for Foo \"What it says\".";
    private const string DirectionShape = "A direction line lays the diagram out: direction LR.";
    private const string ClickShape = "A click line says where a class leads: click Foo href \"https://example.com\" \"Tooltip\".";
    private const string CssClassShape = "A cssClass line names the classes taking a class, then the class: cssClass \"A,B\" blue.";

    /// <inheritdoc/>
    /// <remarks>Nothing follows the keyword: a class diagram is laid out by a <c>direction</c> line of its own.</remarks>
    public ContentNode? Header(string arguments)
    {
        var line = MermaidLine.Of(arguments);
        if (line.Done) return null;

        return ContentNode.Shown(arguments, "Nothing follows classDiagram — direction LR lays it out.", MermaidRoles.Arguments);
    }

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        if (line.Written.Trim() is Closing or Closing + ";") return Shut(line, ClassKinds.Ends);
        if (line.Sees(Opens)) return Annotated(line);

        return MermaidLine.Keyword(line.Written, Bare,
                                   [ClassWord, NamespaceWord, NoteWord, DirectionWord, ClickWord, CallbackWord, LinkWord,
                                    CssClassWord, MermaidStyling.ClassDefWord, MermaidStyling.StyleWord]) switch
        {
            ClassWord => Declared(line),
            NamespaceWord => Boxed(line),
            NoteWord => Noted(line),
            DirectionWord => Towards(line),
            ClickWord or CallbackWord or LinkWord => Clicked(line),
            CssClassWord => Taken(line),
            MermaidStyling.ClassDefWord => Styling.Defined(line, ClassKinds.ClassDef),
            MermaidStyling.StyleWord => Styling.Styled(line, ClassKinds.Style),
            _ => Joined(line),
        };
    }

    /// <inheritdoc/>
    /// <remarks>A class whose members are written on the lines between its braces, until the <c>}</c> closing them.</remarks>
    public IEnumerable<MermaidStretch> Stretches =>
        [new MermaidStretch(ClassKinds.Body, Begun, Shutting, Within, Shutter, "These members are never closed: } closes them.")];

    /// <inheritdoc/>
    /// <remarks>
    /// Under a member, another member of the same class; under a line that styles or says something about one, nothing in
    /// particular; and under everything else another class, which is what a diagram is mostly made of.
    /// </remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => above?.Kind switch
    {
        ClassKinds.Says when Declaring(above) is { Length: > 0 } id => ($"{Naming(id)} : ", Naming(id).Length + 3),
        ClassKinds.Relation => ("\"\" --> \"\"", 1),
        ClassKinds.Note or ClassKinds.Direction or ClassKinds.Annotation or ClassKinds.Click => null,
        ClassKinds.ClassDef or ClassKinds.Style or ClassKinds.CssClass => null,
        _ => ("class \"\"", 7),
    };

    /// <summary>The class a line declares, which is the first one it names.</summary>
    private static string? Declaring(ContentNode line) =>
        line.SelfAndDescendants().FirstOrDefault(node => node.Kind == MermaidKinds.Words && node.Role == ClassRoles.Id)?.Text;

    /// <inheritdoc/>
    /// <remarks>
    /// A member and what is written on a relation run to the end of their line and hold anything but a comment. An id and a class
    /// are written bare — in backticks, where one holds what a bare id cannot — and a way and a target are words.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        if (part.Role is ClassRoles.Id or ClassRoles.Class)
            return Backed(part)
                ? MermaidWriting.Only(caret, text, character => character != '`')
                : MermaidWriting.Only(caret, text, Bare);

        if (part.Role is ClassRoles.Space) return MermaidWriting.Only(caret, text, Dotted);

        // A function named after call is written bare, and the quotes past it open what the class says while pointed at.
        if (part.Role is ClassRoles.Call) return MermaidWriting.Only(caret, text, character => character != '"');
        if (part.Role is ClassRoles.Towards or ClassRoles.Target) return MermaidWriting.Only(caret, text, char.IsAsciiLetter);

        return null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A class is declared where it is first written and used wherever it is written again — either end of a relation, a
    /// <c>cssClass</c> line, a <c>style</c> line, a member's own line, the class a note is written beside.
    /// </remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var said = new Dictionary<string, List<ContentPart>>(StringComparer.Ordinal);

        foreach (var name in block.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Name))
        {
            if (name.Words() is not { Role: ClassRoles.Id, Length: > 0 } words) continue;

            if (!said.TryGetValue(words.Text, out var places)) said[words.Text] = places = [];
            places.Add(name);
        }

        return [.. said.Select(name => new MermaidName(name.Key, name.Value[0], [.. name.Value.Skip(1)]))];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// An id is written bare, so what an id cannot hold is dropped. A class declared in backticks may be called anything at
    /// all, but a <c>cssClass</c> line names it inside quotes of its own and a <c>style</c> line bare, so a name carried to
    /// every use of it has to be one that reads in all three.
    /// </remarks>
    public string Naming(string name) => new([.. name.Where(Bare)]);

    /// <inheritdoc/>
    /// <remarks>
    /// Which namespace each line is in (<see cref="ResolveNamespaces"/>), and whether what is styled is written at all
    /// (<see cref="ResolveStyles"/>).
    /// </remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block) => [new ResolveNamespaces(), new ResolveStyles()];

    /// <inheritdoc/>
    /// <remarks>Between a name's quotes, and where a member or what is written on a relation is still to come after its colon.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind is MermaidKinds.Quoted or ClassKinds.Member or ClassKinds.Said;

    /// <summary>Whether a character carries a bare id or a class name on.</summary>
    public static bool Bare(char character) => !Stops.Contains(character, StringComparison.Ordinal);

    /// <summary>Whether a character carries a namespace's name on, which holds the dots that nest one inside another.</summary>
    public static bool Dotted(char character) => Bare(character) || character == '.';

    /// <summary>Whether a name is written between backticks, which hold anything a bare id cannot.</summary>
    private static bool Backed(ContentPart part) =>
        part.Parent?.Children.Any(child => child.Role == Roles.Open && child.Text == Backtick) ?? false;

    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>A <c>class</c> line: a class on its own, one given a label or an annotation, or one opening its members.</summary>
    private static ContentNode Declared(MermaidLine line)
    {
        line.Word(ClassWord, letter: Bare);
        line.Room();

        if (!Named(line)) return line.Shown(ClassShape);
        if (line.Next == '[' && !line.Label("[", "]", ClassRoles.Label)) return line.Shown(ClassShape);

        var mark = line.Save();
        line.Space();

        if (!line.Sees(Opens) || !line.Label(Opens, Shuts, ClassRoles.Kind)) line.Restore(mark);

        line.Space();
        if (!line.Token(Opening, Roles.Open)) return Closed(line, ClassKinds.Class, ClassShape);

        // class Foo{} is a class written on one line with nothing in it, which Mermaid draws as an empty class.
        line.Space();
        if (line.Token(Closing, Roles.Close)) return Closed(line, ClassKinds.Class, ClassShape);

        return line.Done ? line.Read(ClassKinds.Opens) : line.Shown(BodyShape);
    }

    /// <summary>A <c>namespace</c> line, which boxes the classes written until the <c>}</c> closing it.</summary>
    private static ContentNode Boxed(MermaidLine line)
    {
        line.Word(NamespaceWord, letter: Bare);
        line.Room();

        if (!line.Name(ClassRoles.Space, Dotted)) return line.Shown(NamespaceShape);
        if (line.Next == '[' && !line.Label("[", "]", ClassRoles.Label)) return line.Shown(NamespaceShape);

        line.Room();
        if (!line.Token(Opening, Roles.Open)) return line.Shown(NamespaceShape);

        line.Space();
        return line.Done ? line.Read(ClassKinds.Namespace) : line.Shown(NamespaceShape);
    }

    /// <summary>The brace closing a namespace, or the one closing a class's members.</summary>
    private static ContentNode Shut(MermaidLine line, string kind)
    {
        line.Room();
        line.Token(Closing, Roles.Close);

        return Closed(line, kind, BodyShape);
    }

    /// <summary>A relation between two classes, or a member given to one class after a colon.</summary>
    private static ContentNode Joined(MermaidLine line)
    {
        line.Room();
        if (!Named(line)) return line.Shown(RelationShape);

        var mark = line.Save();
        line.Space();

        if (!Counted(line)) return line.Shown(RelationShape);
        line.Space();

        if (!Operator(line))
        {
            line.Restore(mark);
            return Membered(line);
        }

        // The quotes past the operator are a count only where a class follows them; on their own they are the class.
        line.Space();
        var counted = line.Save();

        if (!Counted(line)) return line.Shown(RelationShape);
        line.Space();

        if (!Named(line))
        {
            line.Restore(counted);
            if (!Named(line)) return line.Shown(RelationShape);
        }

        Said(line);
        return Closed(line, ClassKinds.Relation, RelationShape);
    }

    /// <summary>A member given to a class on a line of its own: <c>Foo : +int age</c>.</summary>
    private static ContentNode Membered(MermaidLine line)
    {
        line.Space();
        if (!line.Token(":", Roles.Open)) return line.Shown(MemberShape);

        line.Open();
        line.Room();
        line.Words(ClassRoles.Member);
        line.Close(ClassKinds.Member, ClassRoles.Member);

        return Closed(line, ClassKinds.Says, MemberShape);
    }

    /// <summary>An annotation written on a line of its own, saying what a class declared elsewhere is.</summary>
    private static ContentNode Annotated(MermaidLine line)
    {
        if (!line.Label(Opens, Shuts, ClassRoles.Kind)) return line.Shown(AnnotationShape);

        line.Room();
        if (!Named(line)) return line.Shown(AnnotationShape);

        return Closed(line, ClassKinds.Annotation, AnnotationShape);
    }

    /// <summary>A note beside a class, or one floating with nothing to be beside.</summary>
    private static ContentNode Noted(MermaidLine line)
    {
        line.Word(NoteWord, letter: Bare);
        line.Room();

        if (line.Word(ForWord, letter: Bare))
        {
            line.Room();
            if (!Named(line)) return line.Shown(NoteShape);
            line.Room();
        }

        if (!line.Quoted(ClassRoles.Said)) return line.Shown(NoteShape);

        return Closed(line, ClassKinds.Note, NoteShape);
    }

    /// <summary>The way the diagram is laid out.</summary>
    private static ContentNode Towards(MermaidLine line)
    {
        line.Word(DirectionWord, letter: Bare);
        line.Room();
        line.Setting(ClassRoles.Towards, Wayward, until: Stops);

        return Closed(line, ClassKinds.Direction, DirectionShape);
    }

    /// <summary>What takes a class, and the class it takes: <c>cssClass "A,B" blue</c>.</summary>
    private static ContentNode Taken(MermaidLine line)
    {
        line.Word(CssClassWord, letter: Bare);
        line.Room();

        if (!line.Token("\"", Roles.Open)) return line.Shown(CssClassShape);
        if (!line.Names(Id, ClassRoles.Id, "class", ends: '"')) return line.Shown(CssClassShape);
        if (!line.Token("\"", Roles.Close)) return line.Shown(CssClassShape);

        line.Room();
        if (!line.Done && !line.Name(ClassRoles.Class, Bare)) return line.Shown(CssClassShape);

        return Closed(line, ClassKinds.CssClass, CssClassShape);
    }

    /// <summary>Where pressing a class leads, what it says while pointed at, and where it opens.</summary>
    private static ContentNode Clicked(MermaidLine line)
    {
        var word = MermaidLine.Keyword(line.Written, Bare, ClickWord, CallbackWord, LinkWord) ?? ClickWord;

        line.Word(word, letter: Bare);
        line.Room();

        if (!Named(line)) return line.Shown(ClickShape);
        line.Room();

        if (line.Word(CallWord, letter: Bare))
        {
            line.Room();
            line.Words(ClassRoles.Call, until: "\"");
        }
        else
        {
            if (line.Word(HrefWord, letter: Bare)) line.Room();
            if (!line.Quoted(word == CallbackWord ? ClassRoles.Call : ClassRoles.Href)) return line.Shown(ClickShape);
        }

        if (!Quotable(line, ClassRoles.Tip)) return line.Shown(ClickShape);

        var mark = line.Save();
        line.Space();

        if (line.Done) line.Restore(mark);
        else line.Setting(ClassRoles.Target, Targeted, until: Stops);

        return Closed(line, ClassKinds.Click, ClickShape);
    }

    // ── The members between the braces ──────────────────────────────────────

    /// <summary>A <c>class</c> line whose members are written on the lines under it.</summary>
    private static ContentNode? Begun(string text) =>
        MermaidLine.Keyword(text, Bare, ClassWord) is not null && Declared(MermaidLine.Of(text)) is { Kind: ClassKinds.Opens } opened
            ? opened
            : null;

    private static bool Shutting(string text) => MermaidLine.Of(text).Written.Trim() is Closing or Closing + ";";

    /// <summary>A line between the braces: an annotation, and otherwise a member, whatever it would read as on its own.</summary>
    private static ContentNode Within(string text)
    {
        var line = MermaidLine.Of(text);
        line.Room();

        if (line.Sees(Opens))
            return line.Label(Opens, Shuts, ClassRoles.Kind)
                ? Closed(line, ClassKinds.Annotation, AnnotationShape)
                : line.Shown(AnnotationShape);

        line.Words(ClassRoles.Member);
        return line.Read(ClassKinds.Member, ClassRoles.Member);
    }

    private static ContentNode Shutter(string text) => Shut(MermaidLine.Of(text), ClassKinds.Shut);

    // ── The pieces a line is made of ────────────────────────────────────────

    /// <summary>One class where it is named: its id, its type parameters, and the class <c>:::</c> gives it.</summary>
    private static bool Named(MermaidLine line)
    {
        var mark = line.Save();
        line.Open();

        if (!Id(line)) return Back(line, mark);

        if (line.Sees(Tilde))
        {
            line.Token(Tilde, Roles.Open);
            line.Words(ClassRoles.Generic, until: Tilde);
            line.Space();
            line.Token(Tilde, Roles.Close);
        }

        if (line.Sees(Given))
        {
            line.Token(Given);
            if (!line.Name(ClassRoles.Class, Bare)) return Back(line, mark);
        }

        line.Close(ClassKinds.Named, ClassRoles.Id);
        return true;
    }

    /// <summary>A class's id: a word, words in quotes, or anything at all between backticks.</summary>
    private static bool Id(MermaidLine line)
    {
        if (line.Next != '`') return line.Name(ClassRoles.Id, Bare);

        var closing = line.Written.IndexOf('`', line.At + 1);
        if (closing < 0) return line.Fail("This name is never closed with a backtick.");

        line.Open();
        line.Token(Backtick, Roles.Open);
        // The name is read less the space before the backtick closing it, which is the line's own rather than the name's.
        line.Words(ClassRoles.Id, closing);
        line.Space();
        line.Token(Backtick, Roles.Close);
        line.Close(MermaidKinds.Name, ClassRoles.Id);

        return true;
    }

    /// <summary>How many of the class at this end of a relation the other has, where a number is written there.</summary>
    private static bool Counted(MermaidLine line) => line.Next != '"' || line.Quoted(ClassRoles.Count);

    /// <summary>Something in quotes where it is written next, and nothing where it is not.</summary>
    private static bool Quotable(MermaidLine line, string role)
    {
        var mark = line.Save();
        line.Room();

        if (line.Next == '"') return line.Quoted(role);

        line.Restore(mark);
        return true;
    }

    /// <summary>The operator a relation is drawn with, where one is written next.</summary>
    private static bool Operator(MermaidLine line)
    {
        foreach (var operation in Operators)
            if (line.Token(operation, ClassRoles.Arrow))
                return true;

        return false;
    }

    /// <summary>What is written on a relation, after the colon opening it — whether anything is.</summary>
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
        line.Words(ClassRoles.Said, until: ";");
        line.Close(ClassKinds.Said, ClassRoles.Said);

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
        Ways.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A class diagram is laid out TB, TD, BT, RL or LR.";

    private static string? Targeted(string said) =>
        Targets.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A link opens _self, _blank, _parent or _top.";
}
