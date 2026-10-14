using System;
using System.Collections.Generic;

using System.Linq;
using System.Text;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// A markdown document: the blocks it is written in, and nothing about what any of them says.
///
/// <para>
/// <strong>A document is a list of blocks, and each block is its own content.</strong> A paragraph, a table, a
/// quote and a fenced tune are four languages, read by four parsers into four trees — so this one only has to
/// say where each block starts and which of them it is. What a block holds is settled by whoever reads that
/// kind, when it is read (<see cref="MarkdownBlocks"/>), which is what lets a keystroke re-read one paragraph
/// rather than a thousand-line file, and what lets a kind nothing can read yet be shown exactly as it was typed.
/// </para>
/// <para>
/// Where one block stops and the next begins is CommonMark's to say, with every extension the reading enables, and the
/// scan says it (<see cref="MarkdownScanner"/>): one pass over the lines, in the order CommonMark reads them.
/// </para>
/// <para>
/// <strong>Every character comes from the source.</strong> The scan holds places, not characters: the characters are
/// cut out of the source at them, with whatever falls between two blocks kept as the trivia it is.
/// </para>
/// </summary>
public sealed class MarkdownParser : ITranspile
{
    private MarkdownParser()
    {
    }
    /// <summary>
    /// A parse for one document, read again and again as it is written: what it read of a block last time is what it hands back
    /// for the same block this time, so a keystroke reads the block it was typed in rather than every block there is. One per
    /// document — what it keeps is that document's blocks. What it hands back says where every piece written in another
    /// language is.
    /// </summary>
    public static Func<string, ContentParse> Parsing()
    {
        var blocks = new MarkdownBlocks();
        return source => ContentParse.Of(blocks.Read(Read(source), source));
    }

    /// <summary>
    /// A parse for one large document read a stretch at a time (<see cref="ContentStretch"/>), reading each stretch as the whole
    /// document would read it.
    ///
    /// <para>
    /// A stretch is read on past its length to the first line a block starts on with nothing but the document open, and stops
    /// there: every block before it is whole, because a block the scan has closed stays closed (<see cref="MarkdownScanner"/>).
    /// A stretch holding a single block, however long, is that block whole. A stretch stops between two blocks, where nothing is
    /// open, so it carries nothing to the next (<see cref="ContentParse.Resume"/>).
    /// </para>
    /// </summary>
    public static Func<ContentStretch, ContentParse> Windowed()
    {
        var blocks = new MarkdownBlocks();
        return stretch => Stretched(stretch, blocks);
    }

    private static ContentParse Stretched(ContentStretch stretch, MarkdownBlocks blocks)
    {
        var source = stretch.Source;
        var from = stretch.From;
        var lines = MarkdownLines.Of(source, from: from);
        var scanned = MarkdownScanner.Scan(lines, document: from == 0, from + Math.Max(stretch.SoftMaxLength, 1), out var stopped);

        var end = stopped ?? source.Length;
        var read = new Cut(source, from, end);
        var parts = new List<ContentNode>();

        Split(scanned.Children, parts, read);
        read.Gap(parts, end);

        var tree = new BlockNode(Language, Checked(Kinds.Block, parts, source[from..end], Roles.Element, from).Children, offset: from);

        return new ContentParse(blocks.Read(tree, source)) { Stopped = stopped };
    }

    /// <summary>The word naming what a document is written in — what its root says it is (<see cref="BlockNode"/>).</summary>
    public const string Language = "markdown";

    /// <summary>What a formula is written in — which nobody writes after <c>$$</c>, because the <c>$$</c> says it.</summary>
    internal const string Maths = "latex";

    /// <summary><paramref name="source"/> as the blocks it is written in.</summary>
    public static ContentNode Read(string? source)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return new BlockNode(Language, []);

        var read = new Cut(text);
        var parts = new List<ContentNode>();

        Split(MarkdownScanner.Scan(MarkdownLines.Of(text), document: true).Children, parts, read);
        read.Gap(parts, read.Length);

        return new BlockNode(Language, Checked(Kinds.Block, parts, text, Roles.Element, read.From).Children, offset: read.From);
    }

    /// <summary>
    /// <paramref name="source"/> read, and worked over by the stages that need nothing but the document: what its definitions say
    /// its links and abbreviations mean, and the names its headings answer to — the reading anything that reads a document without
    /// showing it asks of, such as a search through its words or a list of its sections.
    /// </summary>
    public static ContentNode Worked(string? source)
    {
        var targets = new LinkTargets();

        return new AstPipeline(new WithTargets(targets), new WithReferences(targets)).Run(Parsing()(source ?? string.Empty).Tree);
    }

    /// <summary>
    /// What is written inside a block that holds blocks — a quote's lines, an alert's body.
    ///
    /// <para>
    /// Nothing is stripped. The <c>&gt;</c> at the head of each line falls between the blocks the quote holds,
    /// and is kept as the trivia it is, so the quote prints back as exactly what somebody typed while a builder
    /// draws its blocks and skips its marks, neither of them knowing about the other.
    /// </para>
    /// <para>
    /// A block covering the whole of what was handed over is the block being read rather than something inside
    /// it, so the walk goes through it — twice over where it has to, because a definition list is one item and
    /// the item is the terms and what they mean. Stopping at the first would hand back the same stretch of source
    /// under a new name, which is a reading that learned nothing.
    /// </para>
    /// </summary>
    /// <param name="at">Where <paramref name="source"/>'s first character stands in the document holding it.</param>
    /// <param name="lead">What stands in front of <paramref name="source"/> on the line it starts on — see <see cref="MarkdownLines"/>.</param>
    public static ContentNode Inside(string? source, int at = 0, string? lead = null)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return ContentNode.Branch(Kinds.Sequence, [], Roles.Body, at);

        var read = new Cut(text, at);
        var parts = new List<ContentNode>();
        var holder = MarkdownScanner.Scan(MarkdownLines.Of(text, lead), document: false);

        while (holder.Children is [var only] && Holds(only) && Whole(only, text)) holder = only;

        // An alert says what it is with the first characters of its first line, which are its own and no paragraph's.
        if (holder is ScannedQuote { Named: { } named }) Called(named, parts, read);

        Split(holder.Children, parts, read);
        read.Gap(parts, read.Length);

        return Checked(Kinds.Sequence, parts, text, Roles.Body, at);
    }

    /// <summary>Whether a block holds blocks, and so can be the whole of what was handed over without being what is read.</summary>
    private static bool Holds(ScannedBlock block) => block.Kind is ScannedKind.Quote or ScannedKind.Alert or ScannedKind.Footer
        or ScannedKind.Figure or ScannedKind.DefinitionList or ScannedKind.DefinitionItem or ScannedKind.List or ScannedKind.Item;

    /// <summary>Whether a block is the whole of what was handed over rather than a part of it.</summary>
    private static bool Whole(ScannedBlock block, string text) =>
        block.Start <= 0 && block.End >= text.TrimEnd('\n', '\r', ' ', '\t').Length;

    /// <summary>
    /// The <c>[!NOTE]</c> an alert opens with, cut into its marks and the name between them — which are one marker is the
    /// parse's to say (<see cref="MarkdownGroups"/>).
    /// </summary>
    private static void Called((int At, int Length) named, List<ContentNode> parts, Cut read)
    {
        var from = named.At - 2;
        if (from < read.At) return;

        read.Gap(parts, from);
        parts.Add(read.Take(from + 2, Roles.Open, Kinds.Token));
        parts.Add(read.Take(from + 2 + named.Length, Roles.Name, Kinds.Token));
        parts.Add(read.Take(from + 3 + named.Length, Roles.Close, Kinds.Token));
    }

    /// <summary>
    /// The blocks a container holds, each as its own piece.
    ///
    /// <para>
    /// Only the blocks: a link's or an abbreviation's definition is read for what it defines by the stages, not drawn, so
    /// what it was written as falls between the blocks around it and is kept as it was typed, as anything between two
    /// blocks is.
    /// </para>
    /// </summary>
    internal static void Split(IReadOnlyList<ScannedBlock> blocks, List<ContentNode> parts, Cut read)
    {
        var written = blocks.Where(block => !block.Defines).ToList();

        for (var at = 0; at < written.Count; at++)
            One(written[at], parts, read, at + 1 < written.Count ? written[at + 1].Start : null);
    }

    /// <summary>One block, with whatever was written in front of it — to the end of the line it stops on, and no further than the next begins.</summary>
    private static void One(ScannedBlock block, List<ContentNode> parts, Cut read, int? next)
    {
        var from = Math.Clamp(block.Start, read.At, read.Length);
        var to = read.Closing(block.End, from);
        if (next is { } ceiling && ceiling > from && ceiling < to) to = ceiling;
        if (to == from) return;

        read.Gap(parts, from);

        // A block of a reading that owns only slices of its lines — what a grid table's cell holds — is read where it stands:
        // its characters are slices too, which a body held as one stretch of source for its reader later cannot be. And so is
        // a definition: under the definition before it, a ':' line with no term of its own is one, which on its own it is not.
        if (read.Sliced || block.Kind == ScannedKind.DefinitionItem)
        {
            parts.Add(Built(block, read, to));
            return;
        }

        // Where it stands is asked for before the characters are taken, which is what moves the cut along.
        parts.Add(Block(block, read.Here, read.Text(to), from));
    }

    /// <summary>
    /// One block read where it stands, up to <paramref name="to"/>: each kind read by the reader that reads its body, from what the
    /// scan found of it — so it comes out the shape that reader gives it held and read later.
    /// </summary>
    private static ContentNode Built(ScannedBlock block, Cut read, int to)
    {
        var stands = read.Here;
        int? at = read.Stretches(read.At, to).Skip(1).Any() ? null : stands;

        if (block is ScannedFence { Kind: ScannedKind.Fence or ScannedKind.Maths } fence) return Fenced(fence, read, to, at);

        var body = block.Kind switch
        {
            ScannedKind.Paragraph or ScannedKind.Heading or ScannedKind.Term or ScannedKind.Caption => MarkdownInline.Words(read, block.Lines, to),
            ScannedKind.List => MarkdownList.Read(block, read, to),
            ScannedKind.Table => MarkdownTable.Read((ScannedTable)block, read, to),
            ScannedKind.Quote or ScannedKind.Alert or ScannedKind.Footer or ScannedKind.Figure or ScannedKind.DefinitionList
                or ScannedKind.DefinitionItem => Contents(block, read, to),
            _ => read.Held(to, Kinds.Verbatim, Roles.Body),
        };

        var built = ContentNode.Branch(Kind(block), [body], offset: at);

        return block is ScannedHeading ranked
            ? built.Saying(MarkdownKinds.Rank, MarkdownRoles.Rank, ranked.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : built;
    }

    /// <summary>
    /// What a block holding blocks holds, read where it stands up to <paramref name="to"/>: an alert's name, the blocks inside, and
    /// the marks between them as trivia — as <see cref="Inside"/> reads the block's own source. A definition list of one
    /// definition is that definition's terms and what they mean, as it is there.
    /// </summary>
    internal static ContentNode Contents(ScannedBlock holder, Cut read, int to)
    {
        var stands = read.Here;
        int? at = read.Stretches(read.At, to).Skip(1).Any() ? null : stands;
        var parts = new List<ContentNode>();

        if (holder is { Kind: ScannedKind.DefinitionList, Children: [ScannedDefinition only] } && only.Start <= holder.Start && only.End >= holder.End) holder = only;

        if (holder is ScannedQuote { Named: { } named }) Called(named, parts, read);

        Split(holder.Children, parts, read);
        read.Gap(parts, to);

        return ContentNode.Branch(Kinds.Sequence, parts, Roles.Body, at);
    }

    /// <summary>A fence read where it stands: the fence line, what is written in its language, and the closing fence.</summary>
    private static ContentNode Fenced(ScannedFence fence, Cut read, int to, int? at)
    {
        var language = fence.Kind == ScannedKind.Maths ? Maths : fence.Language;
        var opened = read.Closing(read.At, read.At);
        var shut = fence.Closed is { } closed ? Math.Clamp(closed, opened, to) : to;

        List<ContentNode> parts = [read.Take(opened, Roles.Open, Kinds.Token)];

        if (shut > read.At) parts.Add(read.Held(shut, language.Length > 0 ? Kinds.Nested : Kinds.Verbatim, Roles.Body));
        if (to > read.At) parts.Add(read.Held(to, Kinds.Token, Roles.Close));

        return language.Length > 0 ? new BlockNode(language, parts, offset: at) : ContentNode.Branch(MarkdownKinds.Code, parts, offset: at);
    }

    /// <summary>
    /// The reading, where it is one.
    ///
    /// <para>
    /// Checked rather than trusted: a tree that does not print back as what it was read from is not a reading
    /// of it, whatever else it is. What is handed back instead is the source shown as it was typed — which is
    /// what the body held before anybody read it, and what every builder already knows how to draw. Either way
    /// it says where it was read from, because either way that is where it was read from.
    /// </para>
    /// </summary>
    /// <param name="at">Where <paramref name="text"/>'s first character stands in the document holding it.</param>
    internal static ContentNode Checked(string kind, IReadOnlyList<ContentNode> parts, string text, string role, int at)
    {
        var node = ContentNode.Branch(kind, parts, role, at);

        return node.Prints(text) ? node : ContentNode.Branch(kind, [ContentNode.Shown(text, offset: at)], role, at);
    }

    /// <summary>
    /// One block: what kind it is, where it was read from, and its own source held as written. Nothing is read
    /// out of the body here, because what is inside is a different language with a different grammar, and
    /// reading it is its own parser's business — which happens a pass later, in <see cref="MarkdownBlocks"/>.
    /// </summary>
    /// <param name="origin">Where <paramref name="source"/>'s first character stands in what the scan read, which the lines it found are counted from.</param>
    private static ContentNode Block(ScannedBlock block, int at, string source, int origin)
    {
        // A maths block is a fence, and reads the same way: a delimiter, a body in another language, a delimiter. What
        // differs is only that nobody writes the language after the fence, because the $$ is what says it.
        if (block is ScannedFence { Kind: ScannedKind.Fence or ScannedKind.Maths } fence) return Fenced(fence, at, source, origin);

        // Code and markup are held as their lines, past the marks a container put in front of each: those are the container's.
        var body = block.Kind is ScannedKind.IndentedCode or ScannedKind.Html
            ? Lined(Kinds.Verbatim, source, at, 0, source.Length, [.. block.Lines.Select(line => Math.Max(line.From - origin, 0))])
            : ContentNode.Leaf(Kinds.Verbatim, source, Roles.Body, offset: at);

        var read = ContentNode.Branch(Kind(block), [body], offset: at);

        // How deep a heading is, which the scan counted as it read the hashes or saw which character underlined it.
        if (block is ScannedHeading ranked)
            read = read.Saying(MarkdownKinds.Rank, MarkdownRoles.Rank, ranked.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return read;
    }

    /// <summary>
    /// A fence: a block in the language the word after it names — or maths, which a <c>$$</c> says by being one — holding the
    /// fence line, the lines written in that language (<see cref="Kinds.Nested"/>, unread) with whatever a container put in front
    /// of each between them, and the closing fence.
    /// </summary>
    private static ContentNode Fenced(ScannedFence fence, int at, string source, int origin)
    {
        if (source.Length == 0 || source[0] != fence.Mark)
            return ContentNode.Branch(MarkdownKinds.Code, [ContentNode.Leaf(Kinds.Verbatim, source, Roles.Body, offset: at)], offset: at);

        var line = source.IndexOf('\n');
        var body = line < 0 ? source.Length : line + 1;

        // The closing fence is the line the scan closed it on, from past what its containers put in front of it: the fence's own
        // indentation in front of its marks is the fence's. The line ending before it belongs to the body — it ends the last line
        // somebody wrote in there — and so do the containers' marks on the closing line, which stand between the two.
        var shut = fence.Shut is { } closing ? Math.Clamp(closing - origin, body, source.Length) : source.Length;

        var language = fence.Kind == ScannedKind.Maths ? Maths : fence.Language;

        List<ContentNode> parts = [ContentNode.Leaf(Kinds.Token, source[..body], Roles.Open, offset: at)];

        // Held as written and nothing read out of it: what is in there is a different language — or, where the fence names none, the
        // document's own code, as indented code is. The fence's own line is no line of it.
        if (shut > body)
            parts.Add(Lined(language.Length > 0 ? Kinds.Nested : Kinds.Verbatim, source, at, body, shut,
                            [.. fence.Lines.Skip(1).Select(written => written.From - origin)]));

        if (source.Length > shut) parts.Add(ContentNode.Leaf(Kinds.Token, source[shut..], Roles.Close, offset: at + shut));

        return language.Length > 0
            ? new BlockNode(language, parts, offset: at)
            : ContentNode.Branch(MarkdownKinds.Code, parts, offset: at);
    }

    /// <summary>
    /// What a block holding its lines as written holds, from <paramref name="start"/> to <paramref name="end"/> of
    /// <paramref name="source"/>: each line from where the block's characters start on it (<paramref name="starts"/>) — past the
    /// marks its containers put in front of it and the indentation the block itself takes — to the line ending closing it, and
    /// whatever stands in front of a line held as trivia between them. One run of characters where nothing stands in front of any
    /// line, which is every block standing at the head of its lines.
    /// </summary>
    private static ContentNode Lined(string kind, string source, int at, int start, int end, IReadOnlyList<int> starts)
    {
        var pieces = new List<(int From, int To, bool Marks)>(starts.Count * 2);
        var cursor = start;

        foreach (var begins in starts)
        {
            if (begins < cursor) continue;
            if (begins >= end) break;

            if (begins > cursor) pieces.Add((cursor, begins, true));

            var ending = source.IndexOf('\n', begins, end - begins);
            var stop = ending < 0 ? end : ending + 1;

            pieces.Add((begins, stop, false));
            cursor = stop;
        }

        if (cursor < end) pieces.Add((cursor, end, true));

        if (!pieces.Exists(piece => piece.Marks)) return ContentNode.Leaf(kind, source[start..end], Roles.Body, offset: at + start);

        return ContentNode.Branch(kind, [.. pieces.Select(piece => piece.Marks
            ? ContentNode.Leaf(source.AsSpan(piece.From, piece.To - piece.From).IsWhiteSpace() ? Kinds.Space : Kinds.Token,
                               source[piece.From..piece.To], Roles.Trivia, offset: at + piece.From)
            : ContentNode.Leaf(kind, source[piece.From..piece.To], offset: at + piece.From))], Roles.Body, at + start);
    }

    /// <summary>Which language reads a block's body.</summary>
    private static string Kind(ScannedBlock block) => block.Kind switch
    {
        ScannedKind.Paragraph => MarkdownKinds.Paragraph,
        ScannedKind.Heading => MarkdownKinds.Heading,
        ScannedKind.Rule => MarkdownKinds.Rule,
        ScannedKind.Alert => MarkdownKinds.Alert,
        ScannedKind.Quote => MarkdownKinds.Quote,
        ScannedKind.List => MarkdownKinds.List,
        ScannedKind.Table => MarkdownKinds.Table,
        ScannedKind.FrontMatter => MarkdownKinds.FrontMatter,
        ScannedKind.DefinitionList => MarkdownKinds.Definition,
        ScannedKind.Term => MarkdownKinds.Term,
        ScannedKind.DefinitionItem => MarkdownKinds.Described,
        ScannedKind.Caption => MarkdownKinds.Caption,
        ScannedKind.Figure => MarkdownKinds.Figure,
        ScannedKind.Footer => MarkdownKinds.Footer,
        ScannedKind.Html => MarkdownKinds.Html,
        ScannedKind.IndentedCode => MarkdownKinds.Code,
        _ => Kinds.Verbatim,
    };

    /// <summary>
    /// How prose spells words written into it: every character markdown would read as markup goes in as itself.
    ///
    /// <para>
    /// Prose is the one language here that keeps the lines it was given. Every other collapses them — a formula is one
    /// expression, a diagram's title is one line — and that disposes of every construct which only means something at the
    /// start of a line. A paragraph dragged into a document is meant to stay a paragraph, so those constructs are live
    /// here, and whether a character starts one depends on where it lands: a <c>#</c> is a heading at the start of a line
    /// and a hash everywhere else. Which is why this is asked where the words go, and nothing else is.
    /// </para>
    /// </summary>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>A change as prose spells it, or null where what holds the words is not prose at all.</summary>
    private static string? Spelled(ContentPart part, string text, int at)
    {
        if (Unread(part)) return null;

        var table = Within(part, MarkdownKinds.Table) || Within(part, MarkdownKinds.Row) || Within(part, MarkdownKinds.Cell);
        var begins = Begins(part, at);

        // Words meant as text belong in a run of text, and in front of nothing. Put at the front of a piece that is a
        // construct's own punctuation they separate what holds it together — a link's brackets from its target — and put
        // at the front of a line they push whatever opened it off the start, so a heading becomes a paragraph about a
        // hash. Both are where the words land rather than what they say, and no spelling of them prevents either.
        if (at == part.Start && !Worded(part)) return null;
        if (at == part.Start && begins && part.Print() is { Length: > 0 } ahead && Marked(ahead, 0, true, table)) return null;

        var said = new StringBuilder(text.Length);
        var from = 0;

        while (true)
        {
            var stop = text.IndexOf('\n', from);

            said.Append(Written(stop < 0 ? text[from..] : text[from..stop], begins, table));

            if (stop < 0) return said.ToString();

            // Whatever follows a break stands where a line starts, whether or not the words began at one.
            said.Append('\n');
            from = stop + 1;
            begins = true;
        }
    }

    /// <summary>One line of them, every character read as markup given the backslash that makes it a character.</summary>
    private static string Written(string line, bool begins, bool table)
    {
        var said = new StringBuilder(line.Length);

        for (var at = 0; at < line.Length; at++)
        {
            if (Marked(line, at, begins, table)) said.Append('\\');
            said.Append(line[at]);
        }

        return said.ToString();
    }

    /// <summary>
    /// Whether the character at <paramref name="at"/> would be read as markup where it stands — asked of the tables the reading
    /// itself is read by: what the words' reading stops at (<see cref="MarkdownTokenizer.Starts"/>), and what may start a block
    /// (<see cref="MarkdownBlockRules.MayStart"/>).
    /// </summary>
    private static bool Marked(string line, int at, bool begins, bool table)
    {
        var character = line[at];

        var marked = MarkdownTokenizer.Starts(character) switch
        {
            // Emphasis, code, a link and a strikethrough are read wherever they are written. So are an entity and a tag,
            // which are only markup when something closes them — but what closes one can be the document's own next
            // character rather than anything written here, and a run of words cannot see past its own end. A less-than that
            // opens a link's target is the case that proves it: nothing follows it in the words at all.
            MarkdownStarts.Alone => true,

            // These only where what makes them markup is written beside them.
            MarkdownStarts.Doubled => (at > 0 && line[at - 1] == character) || (at + 1 < line.Length && line[at + 1] == character),
            MarkdownStarts.Paired => line.AsSpan(0, at).Contains(character) || line.AsSpan(at + 1).Contains(character),
            MarkdownStarts.Bracketed => at + 1 < line.Length && line[at + 1] == '[',

            _ => false,
        };

        if (marked) return true;

        // A pipe divides cells in a table and is a pipe anywhere else.
        if (character == '|' && table) return true;

        // And what starts a block, only where a line starts, which is the one thing a part cannot say about itself — or, after
        // the number an item is counted by, what makes it an item's.
        if (!begins) return false;
        if (Opening(line, at)) return MarkdownLinkSyntax.Escapable(character) && MarkdownBlockRules.Reading.MayStart(character);

        return character is '.' or ')' && Numbered(line, at);
    }

    /// <summary>Whether nothing but space stands in front of <paramref name="at"/> on its line.</summary>
    private static bool Opening(string line, int at) => line.AsSpan(0, at).IsWhiteSpace();

    /// <summary>Whether <paramref name="at"/> is what would make an item counted by the number or letters in front of it.</summary>
    private static bool Numbered(string line, int at)
    {
        var from = at;
        while (from > 0 && char.IsAsciiLetterOrDigit(line[from - 1])) from--;

        return from < at && Opening(line, from) && ItemRule.Counts(line.AsSpan(from, at - from));
    }

    /// <summary>
    /// Whether <paramref name="at"/> stands where a line starts, asked of the smallest part holding the character in
    /// front of it.
    ///
    /// <para>
    /// Nothing here holds the source, and nothing needs to: a part prints the characters it was read from, so the one
    /// just before a position is found by printing whichever part is small enough to hold it — a word, usually. Where no
    /// part holds anything in front of it, the words are at the start of everything, which is the start of a line.
    /// </para>
    /// </summary>
    private static bool Begins(ContentPart part, int at)
    {
        for (var up = part; up is not null; up = up.Parent)
        {
            if (up.Derived) continue;

            var into = at - up.Start;
            if (into <= 0) continue;

            var text = up.Print();
            if (into > text.Length) continue;

            // The line started further back than this piece, so what holds it is the one that can see the start.
            var line = text.LastIndexOf('\n', into - 1);
            if (line < 0 && up.Parent is not null) continue;

            return Opens(text[(line + 1)..into]);
        }

        return true;
    }

    /// <summary>
    /// Whether nothing stands between the start of a line and a position but what opens the blocks already holding it.
    ///
    /// <para>
    /// Which is not the same as standing at the start of a line. Inside a quote or a list item the line starts after that
    /// block's own mark, so a <c>&gt;</c> written straight after a <c>&gt;</c> opens a quote inside a quote, and a mark
    /// written straight after a bullet opens a list inside a list. Everything a container writes in front of its content
    /// counts as the start for whatever would open next.
    /// </para>
    /// </summary>
    private static bool Opens(string before)
    {
        foreach (var character in before)
            if (!char.IsWhiteSpace(character) && !char.IsAsciiDigit(character)
                && character is not ('>' or '-' or '+' or '*' or '.' or ')')) return false;

        return true;
    }

    /// <summary>
    /// Whether what holds the words is something prose does not spell: code, raw HTML, front matter, a formula, or a
    /// block nothing has read.
    ///
    /// <para>
    /// A backslash is a literal backslash in every one of them, so there is no spelling that would make a character safe
    /// there — and widening a fence until it could hold one is more than the run of words this is asked about.
    /// </para>
    /// <para>
    /// Trivia is refused for a different reason: it is the gap between two blocks rather than anywhere inside one, so
    /// words put there do not join a block, they make or break one. That is where the words go rather than how they are
    /// spelled, and it is settled before this is asked.
    /// </para>
    /// </summary>
    private static bool Unread(ContentPart part) =>
        Within(part, MarkdownKinds.Code) || Within(part, MarkdownKinds.Html) || Within(part, MarkdownKinds.FrontMatter)
        || Within(part, MarkdownKinds.Formula) || Within(part, Kinds.Verbatim)
        || part.Role == Roles.Trivia || part.Ancestors().Any(up => up.Role == Roles.Trivia);

    /// <summary>Whether a part is, or is written inside, a <paramref name="kind"/>.</summary>
    private static bool Within(ContentPart part, string kind) =>
        part.Kind == kind || part.Ancestors().Any(up => up.Kind == kind);

    /// <summary>
    /// Whether a piece is a run of text, rather than a construct's own punctuation.
    ///
    /// <para>
    /// Said by the role and not the kind: a link's closing bracket is a token exactly as a word is, and what separates
    /// them is what each is for. Only what a block holds as its own words takes words in front of it.
    /// </para>
    /// </summary>
    private static bool Worded(ContentPart part) =>
        part.Role == Roles.Element && part.Kind is MarkdownKinds.Word or Kinds.Token or Kinds.Space;
}
