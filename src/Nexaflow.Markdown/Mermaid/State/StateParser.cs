using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.State;

/// <summary>
/// Reads a state diagram — a <c>stateDiagram</c> block, or a <c>stateDiagram-v2</c> one, which Mermaid reads the same way: the states,
/// the transitions between them, the composite states those are gathered into, the notes written beside them, and the lines that lay
/// it out and style it — and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>). The header is optional, since a <c>stateDiagram</c> fence already says
/// what the block is.
/// </para>
/// <para>
/// A statement ends where its line does, except inside quotes: a name or what a note says written in quotes runs to the quote closing
/// it, across line endings, as Mermaid's lexer reads a string. What is written after a colon is the rest of its line, quotes and all,
/// as Mermaid reads a description. A note saying nothing on its own line is written on the lines under it, until an <c>end note</c>
/// closes it.
/// </para>
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
/// <para>
/// A state is named by its id, and a transition, a <c>class</c>, a <c>style</c> and a <c>note</c> line all reach a state by that
/// name. A character that would end a name is not written at all, because writing it would leave every line naming the state
/// pointing at something that is not there. A class is named the same way and reached the same way.
/// </para>
/// </summary>
public sealed class StateParser : ITranspile
{
    private StateParser()
    {
    }

    /// <summary>The language a state diagram is written in: what a fence names it by, and a word its header is written with.</summary>
    public const string Language = "stateDiagram";

    /// <summary>The words a state diagram's header is written with — Mermaid's both.</summary>
    public static readonly string[] HeaderWords = ["stateDiagram-v2", Language];

    /// <summary>What opens a comment running to the end of its row.</summary>
    private const string Comment = "%%";

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
    public const string Stops = ":,-{}<>\" \t\r\n";

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

    /// <inheritdoc/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>A change as a state diagram spells it.</summary>
    private static string? Spelled(ContentPart part, string text) =>
        part.Role is StateRoles.Id or StateRoles.Class
            ? text.All(Bare) && !Keyworded(text) ? text : null
            : MermaidParser.Spelled(part, text);

    /// <summary>
    /// Whether a name would be read as the word a line is read by. The first word of a line says what kind of line it is, ignoring
    /// case, so a state called <c>state</c> turns its own line into a state declaration and whatever followed the name stops being
    /// read as a transition at all.
    /// </summary>
    private static bool Keyworded(string text) => Keywords.Contains(text, StringComparer.Ordinal);

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

        // A note saying nothing on its own line takes every line under it until the end note closing it.
        if (Noting(text, from) is { } opened) return NoteBody(source, row, from, to, opened, lines);

        // A # starts a comment running to the end of its row, as Mermaid's lexer reads one — but for an entity code.
        if (text[0] == '#' && MermaidLine.EntityLength(text, 0) == 0)
        {
            lines.Add(MermaidParser.Line(source, row.Start, from, [ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia, offset: from)], to, row));
            return row.Stop;
        }

        // Any other statement, which is as much of the source as it takes rather than as much as the row holds — a composite's braces
        // and a comment end one part-way along its row. As much of it as is read is what was written, and the rest is the line's.
        var end = StatementEnd(source, from);
        return MermaidParser.StatementLine(source, row.Start, from, Statement(source[from..end], from), end, lines);
    }

    /// <summary>
    /// A note written on the lines under the one opening it: that line, every line under it, and the <c>end note</c> closing it, read as
    /// one statement and made one line of the tree. Where nothing closes it, the line opening it is a statement on its own, said to be
    /// never closed, and the lines under it are read as they would be without it.
    /// </summary>
    private static int NoteBody(string source, MermaidParser.Row row, int from, int to, ContentNode opened, List<ContentNode> lines)
    {
        var pieces = new List<ContentNode> { opened };
        var end = to;

        for (var next = MermaidParser.Row.At(source, row.Stop); next.Start < source.Length; next = MermaidParser.Row.At(source, next.Stop))
        {
            var (start, stop) = next.Text(source);
            var text = source[start..stop];

            pieces.Add(MermaidParser.Space(source[end..start], end));
            end = stop;

            if (text.Length == 0) continue;

            if (!Ending(text))
            {
                pieces.Add(Within(text, start));
                continue;
            }

            pieces.Add(Ended(text, start));
            lines.Add(MermaidParser.Line(source, row.Start, from, [ContentNode.Branch(StateKinds.Note, pieces, offset: from)], stop, next));
            return next.Stop;
        }

        lines.Add(MermaidParser.Line(source, row.Start, from,
            [ContentNode.Branch(StateKinds.Note, [opened], offset: from).Saying("This note is never closed: end note closes it.")], to, row));
        return row.Stop;
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: the end of the row a line ending, a comment or the colon opening
    /// what is written on a state is found on, outside a quoted run, and the end of the block where none follows. A composite's braces
    /// end one part-way along its row, as Mermaid reads them — <c>state X { A --&gt; B }</c> is three statements — and a <c>state</c>
    /// line with nothing more takes the brace standing alone on the row under it. A <c>#</c> before the colon starts a comment, but for
    /// an entity code. A quote nothing closes is a quote like any other character, so one typed by mistake costs the line it is on
    /// rather than everything under it.
    /// </summary>
    private static int StatementEnd(string source, int from)
    {
        var row = MermaidParser.Row.Containing(source, from).End;
        var stated = MermaidLine.ExactKeyword(source[from..row], Bare, StateWord) is not null;

        // Mermaid reads statements one after another with only space between them: a --> b c --> d is two transitions.
        if (Following(source, from, row) is { } next) return Before(source, from, next);

        for (var at = from; at < source.Length; at++)
        {
            var character = source[at];

            if (character == '\n' || source.AsSpan(at).StartsWith(Comment, StringComparison.Ordinal))
            {
                if (character == '\n' && stated && BraceBelow(source, at) is { } brace) return brace;

                return MermaidParser.Row.Containing(source, at).End;
            }

            if (character == '#')
            {
                var entity = MermaidLine.EntityLength(source, at);
                if (entity == 0) return Before(source, from, at);

                at += entity - 1;
                continue;
            }

            if (character == '{') return at + 1;
            if (character == '}') return at > from ? Before(source, from, at) : at + 1;

            if (character == ':')
            {
                if (!source.AsSpan(at).StartsWith(Given, StringComparison.Ordinal)) return MermaidParser.Row.Containing(source, at).End;

                at += Given.Length - 1;
                continue;
            }

            if (character != '"') continue;

            var close = source.IndexOf('"', at + 1);
            if (close >= 0) at = close;
        }

        return MermaidParser.Row.Containing(source, source.Length).End;
    }

    /// <summary>
    /// Where another statement starts on the row after a transition written from <paramref name="from"/> — past its target, the class
    /// given it and the space after them, where anything but what the transition says follows — or null where none does.
    /// </summary>
    private static int? Following(string source, int from, int end)
    {
        var arrow = source.IndexOf(Arrow, from, end - from, StringComparison.Ordinal);
        if (arrow < 0 || source.IndexOf('"', from, arrow - from) >= 0) return null;

        var at = arrow + Arrow.Length;
        while (at < end && source[at] is ' ' or '\t') at++;

        if (source.AsSpan(at, end - at).StartsWith("[*]", StringComparison.Ordinal)) at += 3;
        else while (at < end && Identifying(source[at])) at++;

        if (source.AsSpan(at, end - at).StartsWith(Given, StringComparison.Ordinal))
            for (at += Given.Length; at < end && Identifying(source[at]); at++) { }

        var target = at;
        while (at < end && source[at] is ' ' or '\t') at++;

        return at < end && at > target && source[at] is not (':' or '{' or '}' or '#' or '%') ? at : null;
    }

    /// <summary>What carries an id on, as Mermaid's lexer reads one: anything but a colon, a dash, a brace and space.</summary>
    private static bool Identifying(char character) => character is not (':' or '-' or '{') && !char.IsWhiteSpace(character);

    /// <summary>Where <paramref name="at"/> is, less the space before it back to <paramref name="from"/>.</summary>
    private static int Before(string source, int from, int at)
    {
        while (at > from && char.IsWhiteSpace(source[at - 1])) at--;
        return at;
    }

    /// <summary>Where the row under the line ending at <paramref name="ending"/> ends, where that row is a composite's brace and nothing else — or null.</summary>
    private static int? BraceBelow(string source, int ending)
    {
        var below = MermaidParser.Row.At(source, ending + 1);
        var (from, to) = below.Text(source);

        return to - from == 1 && source[from] == '{' ? below.End : null;
    }

    /// <summary>The header — <c>stateDiagram</c> or <c>stateDiagram-v2</c>, with nothing after it — where the first line saying anything is one.</summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, HeaderWords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(Arguments(rest[gap..], after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    /// <summary>What follows the keyword, which is nothing: a state diagram is laid out by a <c>direction</c> line of its own.</summary>
    private static ContentNode Arguments(string arguments, int at) =>
        MermaidLine.Of(arguments, at).Done
            ? ContentNode.Leaf(Kinds.Verbatim, arguments, MermaidRoles.Arguments, offset: at)
            : ContentNode.Shown(arguments, "Nothing follows stateDiagram — direction LR lays it out.", MermaidRoles.Arguments, at);

    /// <summary>
    /// The words a line is read by, matched against the first word of one, ignoring case. A name that is one of these
    /// cannot be a state's: the line would be read as that kind of line instead of as what was meant.
    /// </summary>
    public static readonly string[] Keywords =
        [StateWord, NoteWord, EndWord, DirectionWord, ClickWord, HideWord, ScaleWord, .. MermaidStyling.Words];

    /// <summary>A statement written on its own: a state, a transition, a composite opening or closing, a note, or a line laying out or styling the diagram.</summary>
    private static ContentNode Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at, written => MermaidLine.HashComment(written, before: ':'));

        if (line.Past == '}') return Shut(line);
        if (line.Written.Trim() == Divider) return Divided(line);

        // Mermaid's state diagram reads its words case and all, and end only as the end of a note: a state called End is a state.
        return MermaidLine.ExactKeyword(line.Written, Bare, Keywords) switch
        {
            StateWord => Stated(line),
            NoteWord => Noted(line),
            EndWord when MermaidLine.ExactKeyword(line.Written[EndWord.Length..].TrimStart(), Bare, NoteWord) is not null => Ended(line),
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


    /// <summary>A <c>note</c> line saying nothing on its own, which opens a note written on the lines under it.</summary>
    private static ContentNode? Noting(string text, int at) =>
        MermaidLine.Keyword(text, Bare, NoteWord) is not null && Noted(MermaidLine.Of(text, at)) is { Kind: StateKinds.NoteOpens } note
            ? note
            : null;

    // Only what the line reads as is asked for, and nothing read from it is kept, so it stands nowhere.
    private static bool Ending(string text) => Ended(text, 0).Kind == StateKinds.NoteEnds;

    /// <summary>A line inside a note: what the note says, whatever it would read as on its own.</summary>
    private static ContentNode Within(string text, int at) =>
        ContentNode.Branch(StateKinds.NoteText, [ContentNode.Leaf(Kinds.Words, text, StateRoles.Label, offset: at)], StateRoles.Label, at);

    /// <inheritdoc cref="Ended(MermaidLine)"/>
    private static ContentNode Ended(string text, int at) => Ended(MermaidLine.Of(text, at));

    /// <summary>
    /// Whether a hole stands in <paramref name="node"/> while somebody writes in the block: between a name's quotes, and where what is
    /// written on a state is still to come after its colon.
    /// </summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind is MermaidKinds.Quoted or StateKinds.Said;

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
            return line.Closed(StateKinds.Transition, TransitionShape);
        }

        line.Restore(mark);
        Says(line);

        return line.Closed(StateKinds.State, StateShape);
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

        if (Marked(line)) return line.Closed(StateKinds.State, DrawnShape);

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
            return line.Closed(StateKinds.State, StateShape);
        }

        line.Space();
        return line.Done ? line.Read(StateKinds.Opens) : line.Shown(CompositeShape);
    }

    /// <summary>The brace closing a composite state.</summary>
    private static ContentNode Shut(MermaidLine line)
    {
        line.Room();
        line.Token(Closing, Roles.Close);

        return line.Closed(StateKinds.Ends, CompositeShape);
    }

    /// <summary>The <c>--</c> dividing a composite state into regions running at the same time.</summary>
    private static ContentNode Divided(MermaidLine line)
    {
        line.Room();
        line.Token(Divider, StateRoles.Arrow);

        return line.Closed(StateKinds.Concurrent, DividerShape);
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

            return line.Closed(StateKinds.Note, NoteShape);
        }

        line.Setting(StateRoles.Side, Sided, until: " \t");
        line.Room();

        if (!line.Word(OfWord, letter: Bare)) return line.Shown(NoteShape);

        line.Room();
        if (!Named(line)) return line.Shown(NoteShape);

        // A note saying nothing on its own is written on the lines under it, until an end note closes it.
        return line.Closed(Says(line) ? StateKinds.Note : StateKinds.NoteOpens, NoteShape);
    }

    /// <summary>The <c>end note</c> closing a note written across several lines.</summary>
    private static ContentNode Ended(MermaidLine line)
    {
        line.Word(EndWord, letter: Bare);
        line.Room();

        if (!line.Word(NoteWord, letter: Bare)) return line.Shown(NoteEndShape);

        return line.Closed(StateKinds.NoteEnds, NoteEndShape);
    }

    /// <summary>The way the diagram, or the composite state this line is in, is laid out.</summary>
    private static ContentNode Towards(MermaidLine line)
    {
        line.Word(DirectionWord, letter: Bare);
        line.Room();
        line.Setting(StateRoles.Towards, Wayward, until: Stops);

        return line.Closed(StateKinds.Direction, DirectionShape);
    }

    /// <summary>Where pressing a state leads, and what it says while pointed at.</summary>
    private static ContentNode Clicked(MermaidLine line)
    {
        line.Word(ClickWord, letter: Bare);
        line.Room();

        if (!Named(line)) return line.Shown(ClickShape);

        line.Room();
        if (line.Word(HrefWord, letter: Bare)) line.Room();

        if (!line.Quoted(Roles.Destination)) return line.Shown(ClickShape);

        var mark = line.Save();
        line.Room();

        if (line.Next == '"' && !line.Quoted(StateRoles.Tip)) return line.Shown(ClickShape);
        if (line.Next != '"' && !line.Done) line.Restore(mark);

        return line.Closed(StateKinds.Click, ClickShape);
    }

    /// <summary>A state with nothing written on it drawn as its id alone.</summary>
    private static ContentNode Hidden(MermaidLine line)
    {
        line.Word(HideWord, letter: Bare);
        line.Room();

        if (!line.Word(EmptyWord, letter: Bare)) return line.Shown(HideShape);

        line.Room();
        if (!line.Word(DescriptionWord, letter: Bare)) return line.Shown(HideShape);

        return line.Closed(StateKinds.Hide, HideShape);
    }

    /// <summary>How wide to draw it, which Mermaid keeps from its first renderer.</summary>
    private static ContentNode Scaled(MermaidLine line)
    {
        line.Word(ScaleWord, letter: Bare);
        line.Room();
        line.Amount(StateRoles.Width, MermaidNumber.Positive(ScaleShape), until: Stops);
        line.Room();

        if (!line.Word(WidthWord, letter: Bare)) return line.Shown(ScaleShape);

        return line.Closed(StateKinds.Scale, ScaleShape);
    }

    // ── The pieces a line is made of ────────────────────────────────────────

    /// <summary>One state where it is named: its id, and the class <c>:::</c> gives it.</summary>
    private static bool Named(MermaidLine line)
    {
        var mark = line.Save();
        line.Open();

        if (!line.Name(StateRoles.Id, Bare)) return line.Undo(mark);

        if (line.Sees(Given))
        {
            line.Token(Given);
            if (!line.Name(StateRoles.Class, Bare)) return line.Undo(mark);
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

    private static string? Wayward(string said) =>
        Ways.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A state diagram is laid out TB, TD, BT, RL or LR.";

    private static string? Sorted(string said) =>
        Drawn.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : DrawnShape;

    private static string? Sided(string said) =>
        Sides.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A note is written left of or right of the state it is beside.";
}
