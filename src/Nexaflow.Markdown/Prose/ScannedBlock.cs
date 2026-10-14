namespace Nexaflow.Markdown.Prose;

/// <summary>What a block is, as the scan says it.</summary>
internal enum ScannedKind
{
    Document,
    Paragraph,
    Heading,
    Rule,
    Quote,
    Alert,
    List,
    Item,
    Fence,
    Maths,
    IndentedCode,
    Html,
    FrontMatter,
    Table,
    Row,
    Cell,
    DefinitionList,
    DefinitionItem,
    Term,
    Figure,
    Caption,
    Footer,

    /// <summary>A link's definition: written where it stands, and read for what it defines by the stages, not as a block.</summary>
    Reference,

    /// <summary>An abbreviation's definition — the same.</summary>
    Abbreviation,
}

/// <summary>
/// One block the scan found: what it is, where its characters are, and what it holds.
///
/// <para>
/// Only places — no characters. <see cref="Start"/> is where its first character stands and <see cref="End"/> one past
/// its last, line ending left out; a leaf's <see cref="Lines"/> say where on each of its lines what it holds starts. The
/// characters are the source's, cut out at those places by whoever reads the block.
/// </para>
/// </summary>
internal class ScannedBlock(MarkdownBlockRule rule, ScannedKind kind, int start)
{
    /// <summary>The rule that opened it, which says how it goes on and what closing it does.</summary>
    public MarkdownBlockRule Rule { get; } = rule;

    public ScannedKind Kind { get; set; } = kind;

    private List<ScannedBlock>? _children;
    private List<(int From, int To)>? _lines;

    public ScannedBlock? Parent { get; set; }

    /// <summary>What it holds, in order — no list at all until it holds something, so a leaf costs none.</summary>
    public IReadOnlyList<ScannedBlock> Children => _children ?? (IReadOnlyList<ScannedBlock>)Array.Empty<ScannedBlock>();

    /// <summary>Whether lines may still be added to it.</summary>
    public bool IsOpen { get; set; } = true;

    /// <summary>Where its first character stands.</summary>
    public int Start { get; set; } = start;

    /// <summary>One past the last character it holds that is not space, line ending left out.</summary>
    public int End { get; set; } = start;

    /// <summary>
    /// A leaf's lines, where its rule keeps them (<see cref="MarkdownBlockRule.KeepsLines"/>): from where what it holds starts
    /// on each to where the line ends.
    /// </summary>
    public List<(int From, int To)> Lines => _lines ??= [];

    /// <summary>The last line it took, from where what it holds starts on it to where the line ends.</summary>
    public (int From, int To)? Taken { get; set; }

    /// <summary>The line the scan opened it on, counting from nought.</summary>
    public int Line { get; init; }

    /// <summary>The line it last took one from — so whoever comes after it knows how far behind it is.</summary>
    public int LastLine { get; set; }

    /// <summary>The last line it took that was not blank to it — a quote's <c>&gt;</c> alone is written; an item's empty line is not.</summary>
    public int LastWritten { get; set; }

    /// <summary>
    /// The last line anything was written on in it, its own marks or what it holds — so a line between it and what follows it,
    /// which nothing holds, is a blank one.
    /// </summary>
    public int WrittenThrough => Math.Max(this.LastWritten, this.LastChild?.WrittenThrough ?? this.LastWritten);

    public ScannedBlock? LastChild => _children is { Count: > 0 } held ? held[^1] : null;

    /// <summary>Whether it is a definition — read for what it defines, and held as the trivia between blocks.</summary>
    public bool Defines => this.Kind is ScannedKind.Reference or ScannedKind.Abbreviation;

    /// <summary>Takes in a child, as the last of what it holds.</summary>
    public void Add(ScannedBlock child) => this.Insert(this.Children.Count, child);

    /// <summary>Takes in a child at <paramref name="index"/> among what it holds.</summary>
    public void Insert(int index, ScannedBlock child)
    {
        child.Parent = this;
        (_children ??= []).Insert(index, child);
    }

    /// <summary>Holds <paramref name="child"/> where it held what stood at <paramref name="index"/>.</summary>
    public void Put(int index, ScannedBlock child)
    {
        child.Parent = this;
        _children![index] = child;
    }

    public int IndexOf(ScannedBlock child) => _children?.IndexOf(child) ?? -1;

    public void Remove(ScannedBlock child) => _children?.Remove(child);

    public void RemoveAt(int index) => _children!.RemoveAt(index);

    /// <summary>Lets go of everything it holds from <paramref name="index"/> on.</summary>
    public void RemoveFrom(int index)
    {
        if (_children is { } held && index < held.Count) held.RemoveRange(index, held.Count - index);
    }

    /// <summary>Reaches at least as far as <paramref name="end"/>.</summary>
    public void Reach(int end)
    {
        if (end > this.End) this.End = end;
    }
}

/// <summary>A heading, with how deep it is.</summary>
internal sealed class ScannedHeading(MarkdownBlockRule rule, int start, int rank) : ScannedBlock(rule, ScannedKind.Heading, start)
{
    /// <summary>One to six: as many hashes as open it, or which character underlined it.</summary>
    public int Rank { get; set; } = rank;
}

/// <summary>
/// A fence — code, a formula's <c>$$</c>, a figure's <c>^^^</c>: the characters that open it, how far in it was written,
/// and the word naming its language, where one does.
/// </summary>
internal sealed class ScannedFence(MarkdownBlockRule rule, ScannedKind kind, int start, char mark, int length, int indent)
    : ScannedBlock(rule, kind, start)
{
    public char Mark { get; } = mark;

    /// <summary>How many of <see cref="Mark"/> open it — what a closing fence has to reach.</summary>
    public int Length { get; } = length;

    /// <summary>How many columns in it was written, which every line inside gives up as much of as it has.</summary>
    public int Indent { get; } = indent;

    /// <summary>The language the word after the fence names, as it means rather than as it was spelled — or nothing.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Where the marks closing it start, where a line closed it.</summary>
    public int? Closed { get; set; }

    /// <summary>
    /// Where the line closing it starts past what its containers put in front of it — a quote's <c>&gt;</c>, an item's
    /// indentation — which are theirs, where the fence's own indentation in front of its marks is the fence's.
    /// </summary>
    public int? Shut { get; set; }
}

/// <summary>A list: how its items are marked, and the number the first stands for.</summary>
internal sealed class ScannedList(MarkdownBlockRule rule, int start, ScannedMarker marker) : ScannedBlock(rule, ScannedKind.List, start)
{
    public ScannedMarker Marker { get; } = marker;

    /// <summary>Whether its items are set apart by blank lines, or one of them holds two blocks a blank line apart — loose, as CommonMark says.</summary>
    public bool Loose { get; set; }
}

/// <summary>
/// How an item is marked: a bullet's character, or an ordered item's alphabet (<c>1</c>, <c>a</c>, <c>A</c>, <c>i</c>,
/// <c>I</c>), what it was numbered as written, and the character after the number.
/// </summary>
internal readonly record struct ScannedMarker(bool Ordered, char Bullet, string Number, char Delimiter)
{
    /// <summary>Whether an item marked this way goes on the same list as one marked <paramref name="other"/>.</summary>
    public bool Continues(ScannedMarker other) =>
        this.Ordered == other.Ordered && this.Bullet == other.Bullet && this.Delimiter == other.Delimiter;
}

/// <summary>A list item: where its content starts, and the box it can be ticked by, where one was written.</summary>
internal sealed class ScannedItem(MarkdownBlockRule rule, int start, ScannedMarker marker, int padding)
    : ScannedBlock(rule, ScannedKind.Item, start)
{
    public ScannedMarker Marker { get; } = marker;

    /// <summary>How many columns in from what holds it the marker stands.</summary>
    public int Offset { get; init; }

    /// <summary>How many columns past where the item starts its content does — what a line has to be indented by to go on in it.</summary>
    public int Padding { get; } = padding;

    /// <summary>Where the item's box — <c>[ ]</c>, <c>[x]</c> — starts, where one leads its first paragraph.</summary>
    public int? Box { get; set; }

    /// <summary>Whether the box is ticked.</summary>
    public bool Done { get; set; }
}

/// <summary>A quote — or an alert, a quote whose first line names what kind of note it is.</summary>
internal sealed class ScannedQuote(MarkdownBlockRule rule, ScannedKind kind, int start) : ScannedBlock(rule, kind, start)
{
    /// <summary>Where an alert's <c>[!NAME]</c> starts, and how long the name is.</summary>
    public (int At, int Length)? Named { get; set; }
}

/// <summary>Raw HTML: which of the seven ways of starting one it was, which says how it ends.</summary>
internal sealed class ScannedHtml(MarkdownBlockRule rule, int start, int type) : ScannedBlock(rule, ScannedKind.Html, start)
{
    public int Type { get; } = type;
}

/// <summary>A table: how each of its columns is set.</summary>
internal sealed class ScannedTable(MarkdownBlockRule rule, int start) : ScannedBlock(rule, ScannedKind.Table, start)
{
    /// <summary>Each column's alignment — <see cref="MarkdownAligns"/> — or null where the rule under the head says none.</summary>
    public List<string?> Aligns { get; } = [];
}

/// <summary>A row of a table, and whether it is the head.</summary>
internal sealed class ScannedRow(MarkdownBlockRule rule, int start, bool head) : ScannedBlock(rule, ScannedKind.Row, start)
{
    public bool Head { get; } = head;
}

/// <summary>
/// A cell: which column it stands in, and how many columns and rows it covers. A grid table's cell holds its slice of each line
/// it covers as its <see cref="ScannedBlock.Lines"/>, what it says written over them.
/// </summary>
internal sealed class ScannedCell(MarkdownBlockRule rule, int start, int column) : ScannedBlock(rule, ScannedKind.Cell, start)
{
    public int Column { get; } = column;

    public int Across { get; set; } = 1;

    public int Down { get; set; } = 1;
}

/// <summary>A definition: the column its content starts at.</summary>
internal sealed class ScannedDefinition(MarkdownBlockRule rule, int start, int column) : ScannedBlock(rule, ScannedKind.DefinitionItem, start)
{
    /// <summary>The column the <c>:</c> stood at — a line is in the definition while it is indented four columns past it.</summary>
    public int Column { get; } = column;

    /// <summary>Whether a blank line stands after what it holds so far — after which only an indented line goes on in it.</summary>
    public bool AfterBlank { get; set; }
}
