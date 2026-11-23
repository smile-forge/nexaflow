using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Class;

/// <summary>
/// Reads a class diagram — a <c>classDiagram</c> block, or a <c>classDiagram-v2</c> one, which Mermaid reads the same way: the
/// classes, the members inside them, the relations between them, the namespaces they are boxed into, the notes beside them, and the
/// lines that lay it out and style it — and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in Mermaid's syntax
/// rather than about a class diagram. The header is optional, since a <c>classDiagram</c> fence already says what the block is.
/// </para>
/// <para>
/// The rules are Mermaid's. A class is an id, said again wherever it is used; its members are written one to a line between braces
/// — the brace opening them on the class's own line, and every line under it a member until the <c>}</c> closing them — or one at a
/// time after a colon. A relation is an operator between two classes, its ends saying what each draws and its line whether it is
/// solid or dotted. What is in double quotes or between backticks runs to the mark that closes it, across a line ending where that
/// is where it stands; what follows a colon is the rest of its line, quotes and all.
/// </para>
/// <para>
/// <strong>An id ends at the punctuation a relation is drawn with</strong>, which is what lets <c>A&lt;|--B</c> be read with no
/// space in it: a class called anything else is written in backticks, as Mermaid writes it. Each namespace gathered with what is
/// written in it, what each member draws, and what styles each class are facts about the whole block, worked out by the stages a
/// class diagram's language offers the engine (<see cref="Stages.ResolveNamespaces"/>, <see cref="Stages.ResolveMembers"/>,
/// <see cref="Stages.ResolveStyles"/>).
/// </para>
/// <para>
/// A class is named where it is declared and reached by that name from every relation, note and <c>cssClass</c> line, so a
/// character that would end the name is refused where one is written into it. A name written between backticks holds whatever a
/// bare one cannot, and there the only character refused is the backtick that would close it early. An annotation —
/// <c>&lt;&lt;interface&gt;&gt;</c> — is not a name but is spelled all the same, because the angle brackets that close it are
/// written inside the line rather than around a quoted run.
/// </para>
/// </summary>
public sealed class ClassParser : ITranspile
{
    private ClassParser()
    {
    }

    /// <summary>The language a class diagram is written in: what a fence names it by.</summary>
    public const string Language = "classDiagram";

    /// <summary>The words a class diagram's header is written with — Mermaid's both.</summary>
    public static readonly string[] Keywords = ["classDiagram-v2", Language];
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

    /// <summary>What opens a comment running to the end of its row, outside quotes.</summary>
    private const string Comment = "%%";

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

    /// <summary>One statement, read to where it ends — or the line held as written, with the reason.</summary>
    private static ContentNode Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at);

        if (line.Written.Trim() is Closing or Closing + ";") return Shut(line, ClassKinds.Ends);
        if (line.Sees(Opens)) return Annotated(line);

        // Mermaid's class diagram reads its words case and all: a class called Note or Link is a class.
        return MermaidLine.ExactKeyword(line.Written, Bare,
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

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: between a name's quotes, and where a member or what is written on a relation is still to come after its colon.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind is MermaidKinds.Quoted or ClassKinds.Member or ClassKinds.Said;

    /// <summary>Whether a character carries a bare id or a class name on.</summary>
    public static bool Bare(char character) => !Stops.Contains(character, StringComparison.Ordinal);

    /// <summary>Whether a character carries a namespace's name on, which holds the dots that nest one inside another.</summary>
    public static bool Dotted(char character) => Bare(character) || character == '.';

    /// <summary>Reads a block into its lines, every character kept.</summary>
    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;

        var lines = new List<ContentNode>();
        var at = 0;
        var headed = false;

        if (MermaidParser.Fences(source) is (var open, var close))
        {
            // Only blank lines come before front matter; that is what makes it front matter.
            for (; at < open.Start; at = Next(source, at, ref headed, lines)) { }

            lines.Add(MermaidParser.FrontMatter(source, open, close));
            at = close.Stop;
        }

        while (at < source.Length) at = Next(source, at, ref headed, lines);

        return new BlockNode(Language, lines, MermaidKinds.Block);
    }

    // ── Reading the block ───────────────────────────────────────────────────

    /// <summary>The next line of the block, read and added to <paramref name="lines"/> — or the rows it takes — and where the one after it starts.</summary>
    /// <param name="headed">Whether the first line saying anything has been read: only it may be the header.</param>
    private static int Next(string source, int at, ref bool headed, List<ContentNode> lines)
    {
        var row = MermaidParser.Row.At(source, at);
        var (from, to) = row.Text(source);
        var text = source[from..to];

        if (text.Length == 0)
        {
            lines.Add(MermaidParser.Line(source, row.Start, from, [], to, row));
            return row.Stop;
        }

        if (text.StartsWith("%%{", StringComparison.Ordinal)) return MermaidParser.Directive(source, row, from, to, lines);

        if (text.StartsWith(Comment, StringComparison.Ordinal))
        {
            lines.Add(MermaidParser.Line(source, row.Start, from,
                                         [ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia, offset: from)], to, row));
            return row.Stop;
        }

        // A binding standing on a line of its own after the header is lines supplied by whatever the diagram is shown against. Held
        // as written; read into its place before the diagram is worked over.
        if (BoundText.Path(text) is not null)
        {
            lines.Add(MermaidParser.Line(source, row.Start, from,
                [headed
                    ? ContentNode.Leaf(Kinds.BoundContent, text, offset: from)
                    : ContentNode.Shown(text, "A diagram is not bound whole: its first line names its type, and a binding after it supplies lines of it.", offset: from)],
                to, row));
            return row.Stop;
        }

        if (!headed)
        {
            headed = true;
            if (Heading(text, from) is { } header)
            {
                lines.Add(MermaidParser.Line(source, row.Start, from, [header], to, row));
                return row.Stop;
            }
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;

        // A class opening its members takes everything after its brace until the brace closing them, on its line or under it.
        if (Begun(source, from, to) is var (opened, after)) return Body(source, row, from, after, opened, lines);

        // Any other statement, which is as much of the source as it takes rather than as much as the row holds. As much of it as is
        // read is what was written, and the rest is the line's.
        var end = Ended(source, from);
        return MermaidParser.StatementLine(source, row.Start, from, Statement(source[from..end], from), end, lines);
    }

    /// <summary>
    /// A class's members: the line opening them, everything after its brace — one member to a line, the first and the last on the
    /// brace's lines or on lines of their own — and the brace closing them, read as one statement and made one line of the tree, as
    /// Mermaid reads a body: <c>class A { +int x }</c> as well as one written down the page. Where nothing closes them, the line opening
    /// them is a statement on its own, said to be never closed, and the lines under it are read as they would be without it.
    /// </summary>
    /// <param name="after">Where what follows the opening brace starts.</param>
    private static int Body(string source, MermaidParser.Row row, int from, int after, ContentNode opened, List<ContentNode> lines)
    {
        var pieces = new List<ContentNode> { opened };
        var end = after;

        for (var at = after; at < source.Length;)
        {
            var stop = at;
            while (stop < source.Length && source[stop] is not ('\n' or '}')) stop++;
            if (stop >= source.Length) break;

            var (start, finish) = (at, stop);
            while (start < finish && char.IsWhiteSpace(source[start])) start++;
            while (finish > start && char.IsWhiteSpace(source[finish - 1])) finish--;

            if (finish > start)
            {
                if (start > end) pieces.Add(MermaidParser.Space(source[end..start], end));
                pieces.Add(Within(source[start..finish], start));
                end = finish;
            }

            if (source[stop] == '}')
            {
                var shut = stop + 1 < source.Length && source[stop + 1] == ';' ? stop + 2 : stop + 1;
                if (stop > end) pieces.Add(MermaidParser.Space(source[end..stop], end));
                pieces.Add(Shutter(source[stop..shut], stop));

                return MermaidParser.StatementLine(source, row.Start, from, ContentNode.Branch(ClassKinds.Body, pieces, offset: from), shut, lines);
            }

            at = stop + 1;
        }

        lines.Add(MermaidParser.Line(source, row.Start, from,
            [ContentNode.Branch(ClassKinds.Body, [opened], offset: from).Saying("These members are never closed: } closes them.")], after, row));
        return row.Stop;
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: the end of the row a line ending outside quotes and backticks
    /// first ends, or the end of the row a comment, or a colon opening what a member or a relation says, is written on. A quote or a
    /// backtick nothing closes is a character like any other.
    /// </summary>
    private static int Ended(string source, int from)
    {
        // A namespace opens with a brace and what follows it on the row is its own statement, as Mermaid reads namespace N { class A }.
    var boxed = MermaidLine.ExactKeyword(source[from..MermaidParser.Row.Containing(source, from).End], Bare, NamespaceWord) is not null;
        var depth = 0;

        for (var at = from; at < source.Length; at++)
        {
            var character = source[at];

            if (character == '\n' || source.AsSpan(at).StartsWith(Comment, StringComparison.Ordinal)) return MermaidParser.Row.Containing(source, at).End;

            if (character == '{')
            {
                if (boxed) return at + 1;

                depth++;
                continue;
            }

            // A brace closing one this statement opened — class A{} — is its own; one closing a namespace is a statement of its own,
            // wherever it stands on its row.
            if (character == '}' && depth > 0)
            {
                depth--;
                continue;
            }

            if (character == '}')
            {
                if (at > from)
                {
                    while (at > from && char.IsWhiteSpace(source[at - 1])) at--;
                    return at;
                }

                return at + 1 < source.Length && source[at + 1] == ';' ? at + 2 : at + 1;
            }

            if (character == ':')
            {
                if (!source.AsSpan(at).StartsWith(Given, StringComparison.Ordinal)) return MermaidParser.Row.Containing(source, at).End;

                at += Given.Length - 1;
                continue;
            }

            if (character is not ('"' or '`')) continue;

            var close = source.IndexOf(character, at + 1);
            if (close >= 0) at = close;
        }

        return MermaidParser.Row.Containing(source, source.Length).End;
    }

    /// <summary>
    /// The header — <c>classDiagram</c> or <c>classDiagram-v2</c>, and anything after it held as written — or null where the line
    /// says something else, which a <c>classDiagram</c> fence needs no header before.
    /// </summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(Arguments(rest[gap..], after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    /// <summary>What follows the keyword: nothing but a comment, since a class diagram is laid out by a <c>direction</c> line of its own.</summary>
    private static ContentNode Arguments(string arguments, int at) =>
        MermaidLine.Of(arguments, at).Done
            ? ContentNode.Leaf(Kinds.Verbatim, arguments, MermaidRoles.Arguments, offset: at)
            : ContentNode.Shown(arguments, "Nothing follows classDiagram — direction LR lays it out.", MermaidRoles.Arguments, at);
    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>A <c>class</c> line: a class on its own, one given a label or an annotation, or one opening its members.</summary>
    private static ContentNode Declared(MermaidLine line)
    {
        line.Word(ClassWord, letter: Bare);
        line.Room();

        if (!Named(line, labelled: true)) return line.Shown(ClassShape);

        var mark = line.Save();
        line.Space();

        if (!line.Sees(Opens) || !line.Label(Opens, Shuts, ClassRoles.Kind)) line.Restore(mark);

        line.Space();
        if (!line.Token(Opening, Roles.Open)) return line.Closed(ClassKinds.Class, ClassShape);

        // class Foo{} is a class written on one line with nothing in it, which Mermaid draws as an empty class.
        line.Space();
        if (line.Token(Closing, Roles.Close)) return line.Closed(ClassKinds.Class, ClassShape);

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

        return line.Closed(kind, BodyShape);
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
        return line.Closed(ClassKinds.Relation, RelationShape);
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

        return line.Closed(ClassKinds.Says, MemberShape);
    }

    /// <summary>An annotation written on a line of its own, saying what a class declared elsewhere is.</summary>
    private static ContentNode Annotated(MermaidLine line)
    {
        if (!line.Label(Opens, Shuts, ClassRoles.Kind)) return line.Shown(AnnotationShape);

        line.Room();
        if (!Named(line)) return line.Shown(AnnotationShape);

        return line.Closed(ClassKinds.Annotation, AnnotationShape);
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

        return line.Closed(ClassKinds.Note, NoteShape);
    }

    /// <summary>The way the diagram is laid out.</summary>
    private static ContentNode Towards(MermaidLine line)
    {
        line.Word(DirectionWord, letter: Bare);
        line.Room();
        line.Setting(ClassRoles.Towards, Wayward, until: Stops);

        return line.Closed(ClassKinds.Direction, DirectionShape);
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
        if (!line.Done && !line.Names(Classed, ClassRoles.Class, "class")) return line.Shown(CssClassShape);

        return line.Closed(ClassKinds.CssClass, CssClassShape);
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
            if (!line.Quoted(word == CallbackWord ? ClassRoles.Call : Roles.Destination)) return line.Shown(ClickShape);
        }

        if (!Quotable(line, ClassRoles.Tip)) return line.Shown(ClickShape);

        var mark = line.Save();
        line.Space();

        if (line.Done) line.Restore(mark);
        else line.Setting(ClassRoles.Target, Targeted, until: Stops);

        return line.Closed(ClassKinds.Click, ClickShape);
    }

    // ── The members between the braces ──────────────────────────────────────

    /// <summary>
    /// The line opening a class's members — <c>class A {</c>, whatever follows the brace on its row — and where what follows the brace
    /// starts; or null where the line opens none. A class written <c>class A {}</c> with nothing after it is a class with nothing in it,
    /// read as the statement it is.
    /// </summary>
    private static (ContentNode Opened, int After)? Begun(string source, int from, int to)
    {
        var text = source[from..to];
        if (MermaidLine.ExactKeyword(text, Bare, ClassWord) is null) return null;

        var brace = -1;
        for (var at = 0; at < text.Length && brace < 0; at++)
        {
            if (text[at] == '{') brace = at;
            else if (text[at] is '"' or '`' && text.IndexOf(text[at], at + 1) is var close and >= 0) at = close;
        }

        if (brace < 0) return null;

        var past = brace + 1;
        while (past < text.Length && text[past] is ' ' or '\t') past++;
        if (past < text.Length && text[past] == '}' && text[(past + 1)..].Trim() is "" or ";") return null;

        return Declared(MermaidLine.Of(text[..(brace + 1)], from)) is { Kind: ClassKinds.Opens } opened ? (opened, from + brace + 1) : null;
    }

    /// <summary>A line between the braces: an annotation, and otherwise a member, whatever it would read as on its own.</summary>
    private static ContentNode Within(string text, int at)
    {
        var line = MermaidLine.Of(text, at);
        line.Room();

        if (line.Sees(Opens))
            return line.Label(Opens, Shuts, ClassRoles.Kind)
                ? line.Closed(ClassKinds.Annotation, AnnotationShape)
                : line.Shown(AnnotationShape);

        line.Words(ClassRoles.Member);
        return line.Read(ClassKinds.Member, ClassRoles.Member);
    }

    private static ContentNode Shutter(string text, int at) => Shut(MermaidLine.Of(text, at), ClassKinds.Shut);

    // ── The pieces a line is made of ────────────────────────────────────────

    /// <summary>One class where it is named: its id, its type parameters, and the class <c>:::</c> gives it.</summary>
    /// <param name="labelled">Whether a label may follow the name, as one does where a class is declared: class A["Shown"]:::hot.</param>
    private static bool Named(MermaidLine line, bool labelled = false)
    {
        var mark = line.Save();
        line.Open();

        if (!Id(line)) return line.Undo(mark);

        if (line.Sees(Tilde))
        {
            line.Token(Tilde, Roles.Open);
            line.Words(ClassRoles.Generic, until: Tilde);
            line.Space();
            line.Token(Tilde, Roles.Close);
        }

        // A label stands between the name and the class given it, as Mermaid's grammar writes a class's identifier.
        if (labelled && line.Next == '[' && !line.Label("[", "]", ClassRoles.Label)) return line.Undo(mark);

        if (line.Sees(Given))
        {
            line.Token(Given);
            if (!line.Names(Classed, ClassRoles.Class, "class")) return line.Undo(mark);
        }

        line.Close(ClassKinds.Named, ClassRoles.Id);
        return true;
    }

    /// <summary>A class's id: a word, words in quotes, or anything at all between backticks.</summary>
    private static bool Id(MermaidLine line)
    {
        if (line.Next != '`') return line.Name(ClassRoles.Id, Ends);

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

    /// <summary>
    /// Where a class's name written at <paramref name="at"/> ends: past every character a bare name holds, and past a dash or a dot too —
    /// <c>user-service</c>, <c>com.acme.Db</c>, as Mermaid's names hold them — unless a relation starts there.
    /// </summary>
    private static int Ends(string text, int at)
    {
        while (at < text.Length && (Bare(text[at]) || text[at] is '-' or '.'))
        {
            if (text[at] is '-' or '.' && (text.AsSpan(at).StartsWith("--", StringComparison.Ordinal) || text.AsSpan(at).StartsWith("..", StringComparison.Ordinal))) break;
            at++;
        }

        return at;
    }

    /// <summary>One of the classes a line gives something, which may be given several at once.</summary>
    private static bool Classed(MermaidLine line) => line.Name(ClassRoles.Class, Bare);

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

    /// <summary>
    /// A relation's arrow, read as the three things it is built from — what it draws at the class on its left, its line, and what
    /// it draws at the class on its right — each a piece of its own.
    /// </summary>
    private static bool Operator(MermaidLine line)
    {
        foreach (var operation in Operators)
        {
            if (!line.Sees(operation)) continue;

            var (head, drawn, tail) = Parts(operation);

            line.Open();
            if (head.Length > 0) line.Token(head, ClassRoles.Head);
            line.Token(drawn, ClassRoles.Line);
            if (tail.Length > 0) line.Token(tail, ClassRoles.Tail);
            line.Close(ClassKinds.Arrow, ClassRoles.Arrow);

            return true;
        }

        return false;
    }

    /// <summary>The end, the line and the other end an operator is built from.</summary>
    private static (string Head, string Drawn, string Tail) Parts(string operation)
    {
        foreach (var drawn in Lines)
            foreach (var head in Heads.Append(string.Empty))
            {
                if (!operation.StartsWith(head + drawn, StringComparison.Ordinal)) continue;

                var tail = operation[(head.Length + drawn.Length)..];
                if (tail.Length == 0 || Tails.Contains(tail, StringComparer.Ordinal)) return (head, drawn, tail);
            }

        return (string.Empty, operation, string.Empty);
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

    private static string? Wayward(string said) =>
        Ways.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A class diagram is laid out TB, TD, BT, RL or LR.";

    private static string? Targeted(string said) =>
        Targets.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A link opens _self, _blank, _parent or _top.";

    // ── Writing back ────────────────────────────────────────────────────────

    /// <inheritdoc cref="ITranspile.Rewrite"/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>A change as a class diagram spells it.</summary>
    private static string? Spelled(ContentPart part, string text) => part.Role switch
    {
        ClassRoles.Id or ClassRoles.Class =>
            Backed(part)
                ? text.Contains(Backtick, StringComparison.Ordinal) ? null : text
                : text.All(Bare) ? text : null,

        ClassRoles.Kind => text.All(Annotating) ? text : null,

        _ => MermaidParser.Spelled(part, text),
    };

    /// <summary>Whether a character may stand in an annotation, which the angle brackets around it end.</summary>
    private static bool Annotating(char character) => character is not ('<' or '>' or '"');

    /// <summary>Whether a name is written between backticks, which hold anything a bare name cannot.</summary>
    private static bool Backed(ContentPart part) =>
        part.Parent?.Children.Any(child => child.Role == Roles.Open && child.Text == Backtick) ?? false;
}
