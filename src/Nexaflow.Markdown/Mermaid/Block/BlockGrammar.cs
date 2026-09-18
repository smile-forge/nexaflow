using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Block.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Block;

/// <summary>
/// What a <c>block-beta</c> block says beyond the lines every diagram shares: how many columns its grid is laid out in, the
/// blocks themselves — as many to a line as are written there — the composites they nest in, the links between them, and the
/// <c>classDef</c>, <c>class</c> and <c>style</c> lines that colour them.
///
/// <para>
/// The rules are Mermaid's. A block is an id, with a label in the brackets that say its shape
/// (<see cref="MermaidShapes"/>) and <c>:2</c> where it takes more than one column; <c>space</c> leaves cells empty;
/// <c>block:</c>…<c>end</c> nests a grid of its own inside a cell (<see cref="ResolveBlocks"/>); and a link joins the blocks
/// written either side of it. <strong>Where a block is drawn is what its author wrote</strong>, which is the whole point of
/// the type — nothing here works out a position, and nothing moves.
/// </para>
/// <para>
/// Mermaid reads a block diagram without caring where its lines end, so it would take a link written across two of them.
/// Here a line is a line: what a link joins is written on one, as every example in the documentation writes it.
/// </para>
/// </summary>
public sealed class BlockGrammar : IMermaidGrammar
{
    public const string ColumnsWord = "columns";
    public const string AutoWord = "auto";
    public const string BlockWord = "block";
    public const string EndWord = "end";
    public const string SpaceWord = "space";
    public const string ClassDefWord = "classDef";
    public const string ClassWord = "class";
    public const string StyleWord = "style";

    /// <summary>The class a <c>classDef</c> names to style every block at once.</summary>
    public const string Every = "default";

    /// <summary>
    /// The characters a bare id ends at: everything a label's brackets open or close with, the characters a link is made of, the
    /// comma between the ids a class or a style names, and the rest of what Mermaid's ids exclude.
    /// </summary>
    public const string Stops = "([{<>)}]:=-, \t\"";

    /// <summary>Where a block arrow says what it says.</summary>
    public const string ArrowOpen = "<[";
    public const string ArrowClose = "]>";

    /// <summary>The directions a block arrow points: the four, and <c>x</c> and <c>y</c> for a pair of opposites.</summary>
    public static readonly string[] Directions = ["right", "left", "up", "down", "x", "y"];

    private const string ItemShape = "A block is an id, with a label in brackets where it has one: db, A[\"A wide one\"], id:2.";
    private const string OpenShape = "A composite is opened by block, or by block:ID, and ended by end.";
    private const string ColumnShape = "A grid is laid out in columns 3, or in columns auto.";
    private const string ArrowShape = "A block arrow says what it says and where it points: id<[\"Label\"]>(right).";
    private const string LinkShape = "A link joins the blocks either side of it: A --> B, or A -- \"X\" --> B.";
    private const string ClassDefShape = "A classDef names its class, then the style it is: classDef blue fill:#6e6ce6,stroke:#333.";
    private const string ClassShape = "A class line names the blocks taking a class, then the class: class A,B blue.";
    private const string StyleShape = "A style line names the blocks it styles, then the style: style A fill:#969,stroke:#333.";

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        return MermaidLine.Keyword(line.Written, Bare, ColumnsWord, EndWord, BlockWord, ClassDefWord, ClassWord, StyleWord) switch
        {
            ColumnsWord => Columns(line),
            EndWord => Ended(line),
            BlockWord => Opens(line),
            ClassDefWord => Defined(line),
            ClassWord => Applied(line),
            StyleWord => Styled(line),
            _ => Items(line),
        };
    }

    /// <inheritdoc/>
    /// <remarks>Another block, with what is written on it to write — whatever the line above it says.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => ("[\"\"]", 2);

    /// <inheritdoc/>
    /// <remarks>
    /// A label is put in quotes to hold a quote, a bracket closing it or a comment. An id, a class and a direction are written
    /// bare and cannot be quoted at all, so what they cannot hold is dropped; so is anything but a digit in a width.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        if (part.Role is BlockRoles.Id or BlockRoles.Class or BlockRoles.Direction) return MermaidWriting.Only(caret, text, Bare);
        if (part.Parent is { Kind: MermaidKinds.Amount }) return MermaidWriting.Only(caret, text, char.IsAsciiDigit);

        return null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A block is declared where it is first written and used wherever it is written again — a link's ends, a <c>class</c> line,
    /// a <c>style</c> line — because every one of those is the same id.
    /// </remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var said = new Dictionary<string, List<ContentPart>>(StringComparer.Ordinal);

        foreach (var name in block.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Name))
        {
            if (name.Words() is not { Role: BlockRoles.Id, Length: > 0 } words) continue;

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
    /// What each line is inside (<see cref="ResolveBlocks"/>), whether a link has blocks to join (<see cref="ResolveLinks"/>),
    /// and whether what is styled is written at all (<see cref="ResolveStyles"/>).
    /// </remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block) => [new ResolveBlocks(), new ResolveLinks(), new ResolveStyles()];

    /// <inheritdoc/>
    /// <remarks>Between a label's quotes, and where a width or a column count is still to be written.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) => node.Kind is MermaidKinds.Quoted or MermaidKinds.Amount;

    /// <summary>Whether a character carries a bare id, a class or a direction on.</summary>
    public static bool Bare(char character) => !Stops.Contains(character, StringComparison.Ordinal);

    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>How many columns the grid this line is in is laid out in: <c>columns 3</c>, or <c>columns auto</c>.</summary>
    private static ContentNode Columns(MermaidLine line)
    {
        line.Word(ColumnsWord, letter: Bare);
        line.Room();

        if (!line.Word(AutoWord, MermaidKinds.Key, BlockRoles.Count, Bare))
            line.Amount(BlockRoles.Count, MermaidNumber.Where(count => count >= 1 && count == Math.Floor(count),
                                                              "A grid is laid out in a whole number of columns, one or more."),
                        until: Stops);

        line.Space();
        return line.Done ? line.Read(BlockKinds.Columns) : line.Shown(ColumnShape);
    }

    /// <summary>A composite opening: <c>block</c>, or <c>block:ID</c> — which is a block like any other, label, width and all.</summary>
    private static ContentNode Opens(MermaidLine line)
    {
        line.Word(BlockWord, letter: Bare);

        if (line.Token(":") && !Item(line)) return line.Shown(OpenShape);

        line.Space();
        return line.Done ? line.Read(BlockKinds.Opens) : line.Shown(OpenShape);
    }

    private static ContentNode Ended(MermaidLine line)
    {
        line.Word(EndWord, letter: Bare);
        line.Space();
        return line.Done ? line.Read(BlockKinds.Ends) : line.Shown("A composite is ended by end, with nothing after it.");
    }

    /// <summary>The blocks, the empty cells, the arrows and the links written on one line, in the order they are written.</summary>
    private static ContentNode Items(MermaidLine line)
    {
        while (!line.Done)
        {
            if (!Link(line) && !Spaces(line) && !Item(line))
            {
                line.Held(line.Reason ?? ItemShape);
                break;
            }

            line.Space();
        }

        return line.Read(BlockKinds.Items);
    }

    /// <summary>A class and the style it is: <c>classDef blue fill:#6e6ce6,stroke:#333;</c>.</summary>
    private static ContentNode Defined(MermaidLine line)
    {
        line.Word(ClassDefWord, letter: Bare);
        line.Room();

        if (!line.Name(BlockRoles.Class, Bare)) return line.Shown(ClassDefShape);

        line.Room();
        if (!line.Done && !line.Properties(ends: ';')) return line.Shown(ClassDefShape);

        return Closed(line, BlockKinds.ClassDef, ClassDefShape);
    }

    /// <summary>The blocks taking a class: <c>class A,B blue</c>.</summary>
    private static ContentNode Applied(MermaidLine line)
    {
        line.Word(ClassWord, letter: Bare);
        line.Room();

        if (!line.Names(Named, BlockRoles.Id, "block")) return line.Shown(ClassShape);

        line.Room();
        if (!line.Done && !line.Name(BlockRoles.Class, Bare)) return line.Shown(ClassShape);

        return Closed(line, BlockKinds.Class, ClassShape);
    }

    /// <summary>The blocks taking a style of their own: <c>style A fill:#969,stroke:#333</c>.</summary>
    private static ContentNode Styled(MermaidLine line)
    {
        line.Word(StyleWord, letter: Bare);
        line.Room();

        if (!line.Names(Named, BlockRoles.Id, "block")) return line.Shown(StyleShape);

        line.Room();
        if (!line.Done && !line.Properties(ends: ';')) return line.Shown(StyleShape);

        return Closed(line, BlockKinds.Style, StyleShape);
    }

    /// <summary>A styling line as far as it goes, with the semicolon that may close it.</summary>
    private static ContentNode Closed(MermaidLine line, string kind, string shape)
    {
        line.Space();
        line.Token(";");
        line.Space();

        return line.Done ? line.Read(kind) : line.Shown(shape);
    }

    // ── What a line of blocks is made of ────────────────────────────────────

    /// <summary>One block: <c>db</c>, <c>A["A wide one"]</c>, <c>id(("DB")):2</c> — or a block arrow, which is a block too.</summary>
    private static bool Item(MermaidLine line)
    {
        var mark = line.Save();
        line.Open();

        if (!MermaidOutline.Node(line, BlockRoles.Id, BlockRoles.Label, Stops, out var titled, MermaidShapes.Brackets, spaced: false))
            return Back(line, mark);

        var named = titled || line.At > mark.At;

        switch (Arrow(line))
        {
            case false:
                return Back(line, mark);

            case true:
                Width(line);
                line.Close(BlockKinds.Arrow);
                return true;
        }

        if (!named) return Back(line, mark, ItemShape);

        Width(line);
        line.Close(BlockKinds.Item);
        return true;
    }

    /// <summary>
    /// What a block arrow says and where it points: <c>&lt;["Label"]&gt;(x, down)</c>. Null where no arrow is written here, and
    /// false for one written wrong.
    /// </summary>
    private static bool? Arrow(MermaidLine line)
    {
        if (!line.Sees(ArrowOpen)) return null;
        if (!line.Label(ArrowOpen, ArrowClose, BlockRoles.Label)) return false;

        line.Space();
        if (!line.Token("(", Roles.Open)) return line.Fail(ArrowShape);

        line.Room();
        line.Names(Direction, BlockRoles.Direction, "direction", ",", item: null, ends: ')');
        line.Space();

        return line.Token(")", Roles.Close) || line.Fail(ArrowShape);
    }

    /// <summary>Cells left empty: <c>space</c>, or <c>space:3</c>.</summary>
    private static bool Spaces(MermaidLine line)
    {
        if (MermaidLine.Keyword(line.Rest, Bare, SpaceWord) is null) return false;

        line.Open();
        line.Word(SpaceWord, letter: Bare);
        Width(line);
        line.Close(BlockKinds.Space);
        return true;
    }

    /// <summary>How many columns something takes, where it says: the <c>:2</c> after it.</summary>
    private static void Width(MermaidLine line)
    {
        if (!line.Sees(":")) return;

        line.Token(":");
        line.Amount(BlockRoles.Width, MermaidNumber.Where(columns => columns >= 1 && columns == Math.Floor(columns),
                                                          "A block takes a whole number of columns, one or more."),
                    until: Stops);
    }

    /// <summary>A link, with its label between the opening of the link and the link that closes it where it has one.</summary>
    private static bool Link(MermaidLine line)
    {
        if (Joining(line.Written, line.At) is not { } link) return false;

        var mark = line.Save();
        line.Open();

        if (link.Whole)
        {
            line.Token(Drawn(line, link), BlockRoles.Arrow);
            line.Close(BlockKinds.Link);
            return true;
        }

        line.Token(Drawn(line, link), Roles.Open);
        line.Space();

        if (!line.Quoted(BlockRoles.Label, what: "label")) return Back(line, mark, LinkShape);

        line.Space();
        if (Joining(line.Written, line.At) is not { Whole: true } closing) return Back(line, mark, LinkShape);

        line.Token(Drawn(line, closing), BlockRoles.Arrow);
        line.Close(BlockKinds.Link);
        return true;
    }

    private static bool Named(MermaidLine line) => line.Name(BlockRoles.Id, Bare);

    private static bool Direction(MermaidLine line) =>
        line.Name(BlockRoles.Direction, Bare,
                  said => Directions.Contains(said, StringComparer.OrdinalIgnoreCase)
                      ? null
                      : $"A block arrow points {string.Join(", ", Directions.SkipLast(1))} or {Directions[^1]}.");

    /// <summary>Takes nothing, with the reason: a reading that got part of the way and cannot go on.</summary>
    private static bool Back(MermaidLine line, MermaidLine.Mark mark, string? reason = null)
    {
        var why = reason ?? line.Reason;
        line.Restore(mark);
        if (why is not null) line.Fail(why);

        return false;
    }

    // ── What a link is made of ──────────────────────────────────────────────

    /// <summary>
    /// A link written at <paramref name="at"/>: how many characters it takes, and whether it is the whole link or the opening
    /// of one whose label — and closing — are still to come.
    /// </summary>
    private readonly record struct Joined(int Length, bool Whole);

    /// <summary>What is written at <paramref name="at"/> as a link, or null where nothing there is one.</summary>
    private static Joined? Joining(string text, int at)
    {
        var from = at;
        if (at < text.Length && text[at] is 'x' or 'o' or '<') at++;
        if (at >= text.Length) return null;

        return text[at] switch
        {
            '-' or '=' => Dashed(text, at, from, text[at]),
            '.' => Dotted(text, at, from, leading: false),

            // A link drawn as nothing at all, which takes no head and so no character before it either.
            '~' when at == from && Run(text, at, '~') is var tildes and >= 3 => new Joined(tildes, true),
            _ => null,
        };
    }

    /// <summary>
    /// A link of dashes or of equals signs. Two of them are the opening of a labelled link; more than two, or two and a head,
    /// are the whole of one.
    /// </summary>
    private static Joined? Dashed(string text, int at, int from, char dash)
    {
        var run = Run(text, at, dash);

        // A single dash opens the dots of a dotted link, and is nothing else.
        if (run == 1) return dash == '-' ? Dotted(text, at + 1, from, leading: true) : null;

        var after = at + run;
        if (after < text.Length && text[after] is 'x' or 'o' or '>') return new Joined(after + 1 - from, true);

        return run > 2 ? new Joined(after - from, true) : new Joined(at + 2 - from, false);
    }

    /// <summary>A dotted link: the dots with a dash either side of them — <c>-.-</c>, <c>-.-&gt;</c> — or the <c>-.</c> that opens one.</summary>
    private static Joined? Dotted(string text, int at, int from, bool leading)
    {
        var dots = Run(text, at, '.');
        if (dots == 0) return null;

        var after = at + dots;
        if (after >= text.Length || text[after] != '-') return leading ? new Joined(at + 1 - from, false) : null;

        after++;
        if (after < text.Length && text[after] is 'x' or 'o' or '>') after++;

        return new Joined(after - from, true);
    }

    /// <summary>How many of <paramref name="character"/> are written in a row at <paramref name="at"/>.</summary>
    private static int Run(string text, int at, char character)
    {
        var end = at;
        while (end < text.Length && text[end] == character) end++;

        return end - at;
    }

    private static string Drawn(MermaidLine line, Joined link) => line.Written.Substring(line.At, link.Length);
}
