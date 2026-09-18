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

    /// <summary>The classDef, class and style lines, which every diagram with them reads the one way.</summary>
    public static readonly MermaidStyling Styling = new(Bare, BlockRoles.Id, BlockRoles.Class, "block");

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
    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        return MermaidLine.Keyword(line.Written, Bare, [ColumnsWord, EndWord, BlockWord, .. MermaidStyling.Words]) switch
        {
            ColumnsWord => Columns(line),
            EndWord => Ended(line),
            BlockWord => Opens(line),
            MermaidStyling.ClassDefWord => Styling.Defined(line, BlockKinds.ClassDef),
            MermaidStyling.ClassWord => Styling.Applied(line, BlockKinds.Class),
            MermaidStyling.StyleWord => Styling.Styled(line, BlockKinds.Style),
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
        if (MermaidLinks.At(line.Written, line.At) is not { } link) return false;

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
        if (MermaidLinks.At(line.Written, line.At) is not { Whole: true } closing) return Back(line, mark, LinkShape);

        line.Token(Drawn(line, closing), BlockRoles.Arrow);
        line.Close(BlockKinds.Link);
        return true;
    }

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

    private static string Drawn(MermaidLine line, MermaidLinks.Joined link) => line.Written.Substring(line.At, link.Length);
}
