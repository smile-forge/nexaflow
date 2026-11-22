using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Settings;
using static Nexaflow.Markdown.EventModeling.EventModelingTokens;

namespace Nexaflow.Markdown.EventModeling;

/// <summary>
/// Reads an <c>eventmodeling</c> block — Mermaid's event model — into a tree, and says how what is written into one is spelled.
///
/// <para>
/// <strong>A stream of tokens, not a list of rows.</strong> Mermaid's grammar for the diagram hides every space, line ending,
/// comment and directive between its tokens, so a statement is as long as its tokens make it: it may run over several rows, and
/// several may share one. Each statement is read from the word it starts with — <c>tf</c>, <c>timeframe</c>, <c>rf</c>,
/// <c>resetframe</c>, <c>entity</c>, <c>data</c>, <c>note</c>, <c>gwt</c>, <c>title</c>, <c>accTitle</c>, <c>accDescr</c> — and
/// ends where its tokens do; anything after it that starts no statement is held as written with the reason, to where the next
/// statement starts (<see cref="EventModelingTokens"/>). Words are matched case and all, as the grammar matches them, and a word
/// the grammar reserves is never a name.
/// </para>
/// <para>
/// The front matter, between <c>---</c> rows at the very start, is read a row at a time — <c>key: value</c> rows, nested by how far
/// they are indented — for the stage to read the settings and the theme's colours from. The <c>eventmodeling</c> header is optional,
/// since an <c>eventmodeling</c> fence already says what the block is.
/// </para>
/// <para>
/// What a part still to be written stands in — a name, the inside of a value — is an empty piece where it goes, which a hole is put
/// in while somebody writes (<see cref="Holds"/>).
/// </para>
/// </summary>
public sealed class EventModelingParser : ITranspile
{
    private EventModelingParser()
    {
    }

    /// <summary>The language an event model is written in, and the word its header is.</summary>
    public const string Language = "eventmodeling";

    public const string TimeFrameWord = "tf";
    public const string TimeFrameLongWord = "timeframe";
    public const string ResetFrameWord = "rf";
    public const string ResetFrameLongWord = "resetframe";
    public const string EntityWord = "entity";
    public const string DataWord = "data";
    public const string NoteWord = "note";
    public const string SpecificationWord = "gwt";
    public const string GivenWord = "given";
    public const string WhenWord = "when";
    public const string ThenWord = "then";
    public const string TitleWord = "title";
    public const string AccessibleTitleWord = "accTitle";
    public const string AccessibleDescriptionWord = "accDescr";

    /// <summary>The entity types a frame or a specification's line is written with, short and long.</summary>
    public static readonly IReadOnlyList<string> Types = ["rmo", "readmodel", "ui", "cmd", "command", "evt", "event", "pcr", "processor"];

    /// <summary>The formats data may say it is written in.</summary>
    public static readonly IReadOnlyList<string> Formats = ["json", "jsobj", "figma", "salt", "uri", "md", "html", "text"];

    /// <summary>The words a statement starts with.</summary>
    private static readonly HashSet<string> Starters =
    [
        TimeFrameWord, TimeFrameLongWord, ResetFrameWord, ResetFrameLongWord, EntityWord, DataWord, NoteWord, SpecificationWord,
        TitleWord, AccessibleTitleWord, AccessibleDescriptionWord,
    ];

    /// <summary>Every word the grammar reserves, none of which is ever a name.</summary>
    private static readonly HashSet<string> Reserved =
    [
        Language, TimeFrameWord, TimeFrameLongWord, ResetFrameWord, ResetFrameLongWord, EntityWord, DataWord, NoteWord,
        SpecificationWord, GivenWord, WhenWord, ThenWord, .. Types, .. Formats,
    ];

    private const string FrameShape = "A frame is its word, its number, its entity type and its name: tf 01 ui CartUI.";
    private const string TypeShape = "A frame's entity type is ui, pcr or processor, cmd or command, rmo or readmodel, evt or event.";
    private const string FeedShape = "->> names the number of a frame feeding this one: ->> 02.";
    private const string ReferenceShape = "A data block is named between double brackets: [[AddItem01]].";
    private const string FormatShape = "A data format is json, jsobj, figma, salt, uri, md, html or text, between backticks: `json`.";
    private const string RowValueShape = "A frame's data is written on its row, in braces or quotes; a data block is written apart and named with [[Name]].";
    private const string DataShape = "A data block is data, its name and its data, opening with { at the end of a row and closing with } at the start of one.";
    private const string NoteShape = "A note is note, the number of its frame and what it says, opening with { at the end of a row and closing with } at the start of one.";
    private const string SpecificationShape = "A specification is gwt, the number of its frame, given and what was there, when and what happened, then and what came of it: gwt 02 given evt ItemAdded then evt ItemAdded.";
    private const string MentionShape = "A clause lists what it names by entity type and name: given evt ItemAdded.";
    private const string NameShape = "A name is words joined by dots: Inventory.ItemAdded.";
    private const string AccessibleShape = "An accessibility line is accTitle: what it says, accDescr: what it says, or accDescr { what it says }.";
    private const string UnreadShape = "An event model is its frames — tf 01 ui CartUI — with data, notes, specifications and entities.";

    /// <summary>The block as a tree, malformed included: every character is in it, and <see cref="ContentNode.Print"/> gives the block back.</summary>
    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;

        var pieces = new List<ContentNode>();
        var at = FrontMatter(source, pieces);
        var headed = false;

        while (true)
        {
            var next = Past(source, at);
            pieces.AddRange(Trivia(source, at, next));
            at = next;

            if (at >= source.Length) break;

            var token = EventModelingTokens.At(source, at);

            // Only the first thing written may be the header — and a block in an eventmodeling fence needs none.
            if (!headed && token.Kind == Kind.Word && Is(source, token, Language))
            {
                pieces.Add(ContentNode.Leaf(EventModelingKinds.Header, Language, Roles.Name, offset: at));
                at = token.End;
                headed = true;
                continue;
            }

            headed = true;

            var said = Statement(source, at);
            pieces.Add(said);
            at += said.Width;
        }

        return new BlockNode(Language, pieces, EventModelingKinds.Block);
    }

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes: a name, or a value, with nothing written in it.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind is EventModelingKinds.Name or EventModelingKinds.Value;

    /// <inheritdoc cref="ITranspile.Rewrite"/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>
    /// What words written into <paramref name="part"/> at <paramref name="start"/> are spelled as: only what the place can hold. A name
    /// holds word characters and the dots between its words, a number digits; data on its row and a title hold no line ending, and a
    /// title no <c>%%</c>, which would end it; a brace written at the start of a block's row would close the block, so it goes in after a
    /// space.
    /// </summary>
    private static string? Spelled(ContentPart part, string text, int start)
    {
        var hole = part.Kind == Kinds.Hole;
        var place = hole ? part.Parent?.Kind : null;
        var role = hole ? null : part.Role;

        if (place == EventModelingKinds.Name || role == EventModelingRoles.Name)
        {
            var name = Kept(text, character => Carries(character) || character == '.');
            return name.Length > 0 && Joined(part, start) ? " " + name : name;
        }
        if (role is EventModelingRoles.Frame or EventModelingRoles.Source) return Kept(text, char.IsAsciiDigit);
        if (role is EventModelingRoles.DataName or EventModelingRoles.Entity or EventModelingRoles.Format) return Kept(text, Carries);

        if (place == EventModelingKinds.Value || role == EventModelingRoles.Said)
        {
            var value = part.Parent;
            if (value?.Text.Contains('\n') != true) return Flat(text);

            var kept = text.Replace("\n}", "\n }", StringComparison.Ordinal);
            return start == part.Start && kept.StartsWith('}') ? " " + kept : kept;
        }

        if (role is EventModelingRoles.Text or EventModelingRoles.Setting)
        {
            var flat = Flat(text);
            while (flat.Contains("%%", StringComparison.Ordinal)) flat = flat.Replace("%%", "%", StringComparison.Ordinal);

            var before = start - part.Start - 1;
            return before >= 0 && before < part.Text.Length && part.Text[before] == '%' && flat.StartsWith('%') ? flat[1..] : flat;
        }

        return text;
    }

    /// <summary>
    /// Whether a name written at <paramref name="start"/> into <paramref name="part"/>, a name with nothing written in it yet, would run
    /// into the word before it — a frame's type, with no space after it — and so be read as part of that word.
    /// </summary>
    private static bool Joined(ContentPart part, int start)
    {
        var name = part.Kind == Kinds.Hole ? part.Parent : part.Parent?.Kind == EventModelingKinds.Name ? part.Parent : part;
        if (name is null || name.Text.Length > 0) return false;

        var root = part.Ancestors().LastOrDefault() ?? part;
        var before = start - root.Start - 1;
        return before >= 0 && before < root.Text.Length && Carries(root.Text[before]);
    }

    /// <summary><paramref name="text"/> with only the characters <paramref name="keeps"/> says a place holds.</summary>
    private static string Kept(string text, Func<char, bool> keeps) => new([.. text.Where(keeps)]);

    /// <summary><paramref name="text"/> on one row: each line ending a space.</summary>
    private static string Flat(string text) => text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>Whether <paramref name="word"/> is one the grammar reserves, and so never a name.</summary>
    public static bool IsReserved(string word) => Reserved.Contains(word);

    // ── The front matter ────────────────────────────────────────────────────

    /// <summary>
    /// The front matter, where the block opens with it — a <c>---</c> row, the rows under it, and a row starting <c>---</c> closing it —
    /// added to <paramref name="pieces"/>, and where the rest of the block starts. Nothing is added, and the block starts at nought, where
    /// it opens with none or none is closed.
    /// </summary>
    private static int FrontMatter(string source, List<ContentNode> pieces)
    {
        var first = RowEnd(source, 0);
        if (!source[..first].TrimEnd(' ', '\t').Equals("---", StringComparison.Ordinal)) return 0;

        var rows = new List<(int Start, int End, int Stop)>();
        var at = Stop(source, first);

        while (at < source.Length)
        {
            var end = RowEnd(source, at);

            if (Sees(source, at, "---") && (at + 3 >= source.Length || char.IsWhiteSpace(source[at + 3])))
            {
                var fence = new List<ContentNode> { ContentNode.Leaf(Kinds.Token, "---", Roles.Open, offset: 0) };
                if (first > 3) fence.Add(SettingLines.Space(source[3..first], 3));

                var gap = Stop(source, first);
                if (gap > first) fence.Add(SettingLines.Space(source[first..gap], first));

                foreach (var row in rows) fence.AddRange(Setting(source, row.Start, row.End, row.Stop));

                fence.Add(ContentNode.Leaf(Kinds.Token, "---", Roles.Close, offset: at));
                pieces.Add(ContentNode.Branch(EventModelingKinds.FrontMatter, fence, offset: 0));
                return at + 3;
            }

            rows.Add((at, end, Stop(source, end)));
            at = Stop(source, end);
        }

        return 0;
    }

    /// <summary>
    /// A row of the front matter: its indentation, then a <c>key: value</c> setting where it is one, or the row held as written where
    /// it is something else — and the line ending after it.
    /// </summary>
    private static IEnumerable<ContentNode> Setting(string source, int start, int end, int stop)
    {
        var text = source[start..end];
        var lead = SettingLines.Leading(text);
        var trail = SettingLines.Trailing(text, lead);
        var body = text[lead..(text.Length - trail)];

        if (lead > 0) yield return SettingLines.Space(text[..lead], start);

        if (body.Length > 0)
        {
            var colon = SettingLines.Colon(body);
            yield return colon > 0 && body[0] != '-' && body[0] != '#'
                ? Pair(body, colon, start + lead)
                : ContentNode.Leaf(EventModelingKinds.Yaml, body, offset: start + lead);
        }

        if (trail > 0) yield return SettingLines.Space(text[(text.Length - trail)..], end - trail);
        if (stop > end) yield return SettingLines.Space(source[end..stop], end);
    }

    /// <summary>A <c>key: value</c> row: its key, its colon and what it is set to — nothing, for a key holding the rows under it.</summary>
    private static ContentNode Pair(string body, int colon, int at)
    {
        var key = body[..colon];
        var keyEnd = key.Length - SettingLines.Trailing(key, 0);

        var pieces = new List<ContentNode> { ContentNode.Leaf(Kinds.Words, key[..keyEnd], Roles.Name, offset: at) };
        if (keyEnd < key.Length) pieces.Add(SettingLines.Space(key[keyEnd..], at + keyEnd));

        pieces.Add(ContentNode.Leaf(Kinds.Token, ":", Roles.Separator, offset: at + colon));

        var rest = body[(colon + 1)..];
        var gap = SettingLines.Leading(rest);
        if (gap > 0) pieces.Add(SettingLines.Space(rest[..gap], at + colon + 1));
        if (gap < rest.Length) pieces.Add(ContentNode.Leaf(Kinds.Words, rest[gap..], EventModelingRoles.Setting, offset: at + colon + 1 + gap));

        return ContentNode.Branch(EventModelingKinds.Setting, pieces, offset: at);
    }

    /// <summary>Where the row ending at <paramref name="end"/> stops — past its line ending.</summary>
    private static int Stop(string source, int end)
    {
        if (end < source.Length && source[end] == '\r') end++;
        return end < source.Length && source[end] == '\n' ? end + 1 : end;
    }

    // ── Statements ──────────────────────────────────────────────────────────

    /// <summary>The statement starting at <paramref name="at"/> — or what is written there, to where the next statement starts, held with the reason.</summary>
    private static ContentNode Statement(string source, int at)
    {
        var token = EventModelingTokens.At(source, at);
        if (token.Kind != Kind.Word) return Unread(source, at);

        return source[token.Start..token.End] switch
        {
            TimeFrameWord or TimeFrameLongWord or ResetFrameWord or ResetFrameLongWord => Frame(source, at),
            EntityWord => Entity(source, at),
            DataWord => Data(source, at),
            NoteWord => Note(source, at),
            SpecificationWord => Specification(source, at),
            TitleWord when Titled(source, token.End) => Title(source, at, token),
            AccessibleTitleWord => Accessible(source, at, token, EventModelingKinds.AccessibleTitle, braces: false),
            AccessibleDescriptionWord => Accessible(source, at, token, EventModelingKinds.AccessibleDescription, braces: true),
            _ => Unread(source, at),
        };
    }

    /// <summary>
    /// A frame: <c>tf</c> or <c>rf</c> or their long forms, its number, its entity type, its name, what feeds it, the data block it
    /// names, its format and its data on its row.
    /// </summary>
    private static ContentNode Frame(string source, int at)
    {
        var said = new Said(source, at);
        said.Take(said.Next, EventModelingKinds.Keyword, Roles.Name);

        if (said.Next is not { Kind: Kind.Number } number) return said.Done(EventModelingKinds.Frame, FrameShape);
        said.Take(number, Kinds.Number, EventModelingRoles.Frame);

        if (!said.Sees(Types)) return said.Done(EventModelingKinds.Frame, said.Next.Kind == Kind.Word ? TypeShape : FrameShape);
        said.Take(said.Next, EventModelingKinds.Keyword, EventModelingRoles.Type);

        Name(said);

        while (said.Next is { Kind: Kind.Feed } feed)
        {
            said.Open();
            said.Take(feed, Kinds.Token, Roles.Separator);

            var named = said.Next is { Kind: Kind.Number };
            if (named) said.Take(said.Next, Kinds.Number, EventModelingRoles.Source);

            said.Close(EventModelingKinds.Feed, named ? null : FeedShape);
        }

        if (said.Next is { Kind: Kind.OpenReference } open) Reference(said, open);
        if (said.Next is { Kind: Kind.Tick } tick) Format(said, tick);

        return said.Next switch
        {
            { Kind: Kind.Inline } value => Value(said, value).Done(EventModelingKinds.Frame),
            { Kind: Kind.Block } block => Value(said, block).Done(EventModelingKinds.Frame, RowValueShape),
            _ => said.Done(EventModelingKinds.Frame),
        };
    }

    /// <summary>An <c>entity</c> statement: the word and the name it declares.</summary>
    private static ContentNode Entity(string source, int at)
    {
        var said = new Said(source, at);
        said.Take(said.Next, EventModelingKinds.Keyword, Roles.Name);

        Name(said);
        return said.Done(EventModelingKinds.Entity);
    }

    /// <summary>A <c>data</c> statement: the word, the block's name, its format, and the block.</summary>
    private static ContentNode Data(string source, int at)
    {
        var said = new Said(source, at);
        said.Take(said.Next, EventModelingKinds.Keyword, Roles.Name);

        if (!said.SeesName()) return said.Done(EventModelingKinds.Data, DataShape);
        said.Take(said.Next, Kinds.Words, EventModelingRoles.DataName);

        return Blocked(said, EventModelingKinds.Data, DataShape);
    }

    /// <summary>A <c>note</c> statement: the word, the number of the frame it is about, its format, and what it says.</summary>
    private static ContentNode Note(string source, int at)
    {
        var said = new Said(source, at);
        said.Take(said.Next, EventModelingKinds.Keyword, Roles.Name);

        if (said.Next is not { Kind: Kind.Number } number) return said.Done(EventModelingKinds.Note, NoteShape);
        said.Take(number, Kinds.Number, EventModelingRoles.Source);

        return Blocked(said, EventModelingKinds.Note, NoteShape);
    }

    /// <summary>The rest of a statement written with a data block: its format, then the block — or what is wrong where it has none.</summary>
    private static ContentNode Blocked(Said said, string kind, string shape)
    {
        if (said.Next is { Kind: Kind.Tick } tick) Format(said, tick);

        return said.Next switch
        {
            { Kind: Kind.Block } block => Value(said, block).Done(kind),
            { Kind: Kind.Inline } value => Value(said, value).Done(kind, shape),
            _ => said.Done(kind, shape),
        };
    }

    /// <summary>
    /// A <c>gwt</c> statement: the word, the number of the frame it specifies, and its clauses — <c>given</c> and <c>then</c>, with
    /// <c>when</c> between them where it is written.
    /// </summary>
    private static ContentNode Specification(string source, int at)
    {
        var said = new Said(source, at);
        said.Take(said.Next, EventModelingKinds.Keyword, Roles.Name);

        if (said.Next is not { Kind: Kind.Number } number) return said.Done(EventModelingKinds.Specification, SpecificationShape);
        said.Take(number, Kinds.Number, EventModelingRoles.Source);

        if (!Clause(said, GivenWord)) return said.Done(EventModelingKinds.Specification, SpecificationShape);
        Clause(said, WhenWord);

        return Clause(said, ThenWord)
            ? said.Done(EventModelingKinds.Specification)
            : said.Done(EventModelingKinds.Specification, SpecificationShape);
    }

    /// <summary>A clause of a specification, where its word is next: the word, and each entity type and name it lists. False where its word is not next.</summary>
    private static bool Clause(Said said, string word)
    {
        if (said.Next is not { Kind: Kind.Word } opening || !Is(said.Source, opening, word)) return false;

        said.Open();
        said.Take(opening, EventModelingKinds.Keyword, Roles.Name);

        var listed = 0;
        while (said.Sees(Types))
        {
            said.Open();
            said.Take(said.Next, EventModelingKinds.Keyword, EventModelingRoles.Type);

            var named = said.SeesName();
            if (named) said.Take(said.Next, Kinds.Words, EventModelingRoles.Entity);

            said.Close(EventModelingKinds.Mention, named ? null : MentionShape);
            listed++;
        }

        said.Close(EventModelingKinds.Clause, listed > 0 ? null : MentionShape);
        return true;
    }

    /// <summary>
    /// A name: its words with dots between them — or, where none is written yet, an empty name where it goes, for a hole to stand in
    /// while somebody writes.
    /// </summary>
    private static void Name(Said said)
    {
        said.Open();

        if (said.SeesName())
        {
            said.Take(said.Next, Kinds.Words, EventModelingRoles.Name);

            while (said.Next is { Kind: Kind.Dot } dot)
            {
                said.Take(dot, Kinds.Token, Roles.Separator);

                if (!said.SeesName())
                {
                    said.Close(EventModelingKinds.Name, NameShape);
                    return;
                }

                said.Take(said.Next, Kinds.Words, EventModelingRoles.Name);
            }
        }
        else
        {
            // A name still to be written: its words, none of them yet, where they go — past the space on its row, so what is written
            // there stands apart from the word before it — which a hole stands in while somebody writes.
            var to = said.At;
            while (to < said.Source.Length && said.Source[to] is ' ' or '\t') to++;

            said.Spaced(to);
            said.Add(ContentNode.Leaf(Kinds.Words, string.Empty, EventModelingRoles.Name, offset: said.At));
        }

        said.Close(EventModelingKinds.Name);
    }

    /// <summary>A <c>[[name]]</c> naming a data block.</summary>
    private static void Reference(Said said, Token open)
    {
        said.Open();
        said.Take(open, Kinds.Token, Roles.Open);

        if (said.SeesName()) said.Take(said.Next, Kinds.Words, EventModelingRoles.DataName);

        var closed = said.Next is { Kind: Kind.CloseReference } close;
        if (closed) said.Take(said.Next, Kinds.Token, Roles.Close);

        said.Close(EventModelingKinds.Reference, closed && said.Has(EventModelingRoles.DataName) ? null : ReferenceShape);
    }

    /// <summary>A data format between backticks.</summary>
    private static void Format(Said said, Token tick)
    {
        said.Open();
        said.Take(tick, Kinds.Token, Roles.Open);

        var known = said.Sees(Formats);
        if (known) said.Take(said.Next, Kinds.Words, EventModelingRoles.Format);

        var closed = said.Next is { Kind: Kind.Tick } close;
        if (closed) said.Take(said.Next, Kinds.Token, Roles.Close);

        said.Close(EventModelingKinds.Format, known && closed ? null : FormatShape);
    }

    /// <summary>
    /// Data: on its row, its brace or quote, what it says and what closes it; as a block, its brace, each row it says with the space and
    /// line endings between them its own, and the brace closing it.
    /// </summary>
    private static Said Value(Said said, Token value)
    {
        var source = said.Source;
        var opening = source[value.Start];
        var closing = value.Closed ? 1 : 0;
        var inside = (From: value.Start + 1, To: value.End - closing);

        said.Spaced(value.Start);
        said.Open();
        said.Add(ContentNode.Leaf(Kinds.Token, opening.ToString(), Roles.Open, offset: value.Start));

        if (value.Kind == Kind.Block) said.AddRange(Rows(source, inside.From, inside.To));
        else if (inside.To > inside.From) said.Add(ContentNode.Leaf(Kinds.Words, source[inside.From..inside.To], EventModelingRoles.Said, offset: inside.From));

        if (value.Closed) said.Add(ContentNode.Leaf(Kinds.Token, source[(value.End - 1)..value.End], Roles.Close, offset: value.End - 1));

        said.Moved(value.End);

        var trouble = value.Closed ? null
            : value.Kind == Kind.Block ? "This block is never closed: a } at the start of a row closes it."
            : $"This data is never closed with {(opening == '{' ? '}' : opening)} on its row.";

        said.Close(EventModelingKinds.Value, trouble);
        return said;
    }

    /// <summary>A block's rows: each row's words — its indentation with them, since it is the data's — and the space and line endings between.</summary>
    private static IEnumerable<ContentNode> Rows(string source, int from, int to)
    {
        for (var at = from; at < to;)
        {
            var end = Math.Min(RowEnd(source, at), to);
            var words = end;
            while (words > at && char.IsWhiteSpace(source[words - 1])) words--;

            var lead = at;
            while (lead < words && char.IsWhiteSpace(source[lead])) lead++;

            if (lead < words) yield return ContentNode.Leaf(Kinds.Words, source[at..words], EventModelingRoles.Said, offset: at);

            var stop = Math.Min(Stop(source, end), to);
            var space = lead < words ? words : at;
            if (stop > space) yield return SettingLines.Space(source[space..stop], space);

            at = Math.Max(stop, at + 1);
        }
    }

    /// <summary>A <c>title</c> statement: the word, and what it says to the end of its row or to a <c>%%</c> closing it.</summary>
    private static ContentNode Title(string source, int at, Token word)
    {
        var said = new Said(source, at);
        said.Take(word, EventModelingKinds.Keyword, Roles.Name);
        said.Words(said.Rest(), EventModelingRoles.Text);

        return said.Done(EventModelingKinds.Title);
    }

    /// <summary>
    /// An accessibility statement: the word, then a colon and what it says to the end of its row or to a <c>%%</c> — or, for a
    /// description, what it says between braces, over as many rows as it takes.
    /// </summary>
    private static ContentNode Accessible(string source, int at, Token word, string kind, bool braces)
    {
        var said = new Said(source, at);
        said.Take(word, EventModelingKinds.Keyword, Roles.Name);

        var gap = word.End;
        while (gap < source.Length && source[gap] is ' ' or '\t') gap++;

        if (gap < source.Length && source[gap] == ':')
        {
            said.Spaced(gap);
            said.Take(new Token(Kind.Colon, gap, gap + 1), Kinds.Token, Roles.Separator);
            said.Words(said.Rest(), EventModelingRoles.Text);
            return said.Done(kind);
        }

        var opening = Past(source, word.End);
        if (braces && opening < source.Length && source[opening] == '{')
        {
            said.Spaced(opening);
            said.Take(new Token(Kind.Other, opening, opening + 1), Kinds.Token, Roles.Open);

            var close = source.IndexOf('}', opening + 1);
            var end = close < 0 ? source.Length : close;
            said.Words((opening + 1, end), EventModelingRoles.Text);

            if (close < 0) return said.Done(kind, "This description is never closed with }.");

            said.Take(new Token(Kind.Other, close, close + 1), Kinds.Token, Roles.Close);
            return said.Done(kind);
        }

        return said.Done(kind, AccessibleShape);
    }

    /// <summary>Whether a <c>title</c> word at a statement's start is one: nothing after it on its row, or a space or a <c>%%</c>.</summary>
    private static bool Titled(string source, int end) =>
        end >= source.Length || source[end] is ' ' or '\t' or '\r' or '\n' || Sees(source, end, "%%");

    /// <summary>
    /// What is written from <paramref name="at"/> that starts no statement, to where the next statement starts — held as written, with
    /// the reason, the trivia after it left to the block.
    /// </summary>
    private static ContentNode Unread(string source, int at)
    {
        var end = at;

        for (var next = at; next < source.Length;)
        {
            var token = EventModelingTokens.At(source, next);
            if (token.Kind == Kind.End) break;
            if (next > at && token.Kind == Kind.Word && Starters.Contains(source[token.Start..token.End])) break;

            end = Math.Max(token.End, next + 1);
            next = Past(source, end);
        }

        return ContentNode.Shown(source[at..end], UnreadShape, offset: at);
    }

    /// <summary>Whether <paramref name="token"/> is <paramref name="word"/>, case and all.</summary>
    private static bool Is(string source, Token token, string word) =>
        token.End - token.Start == word.Length && string.CompareOrdinal(source, token.Start, word, 0, word.Length) == 0;

    // ── Reading a statement ─────────────────────────────────────────────────

    /// <summary>
    /// A statement being read: what has been taken into it so far, each token with the trivia before it, and the groups opened in it.
    /// Trivia is taken only with the token after it, so what follows a statement's last token is the block's, not the statement's.
    /// </summary>
    private sealed class Said(string source, int at)
    {
        private readonly int _start = at;
        private readonly Stack<(int Start, List<ContentNode> Pieces)> _groups = new([(at, new List<ContentNode>())]);

        /// <summary>The block being read.</summary>
        public string Source { get; } = source;

        /// <summary>Where what has been taken ends.</summary>
        public int At { get; private set; } = at;

        /// <summary>The token after what has been taken, past the trivia before it.</summary>
        public Token Next => EventModelingTokens.At(Source, Past(Source, At));

        /// <summary>Whether the next token is a word among <paramref name="words"/>.</summary>
        public bool Sees(IReadOnlyCollection<string> words) =>
            Next is { Kind: Kind.Word } token && words.Contains(Source[token.Start..token.End]);

        /// <summary>Whether the next token is a word that may be a name — one the grammar does not reserve.</summary>
        public bool SeesName() => Next is { Kind: Kind.Word } token && !Reserved.Contains(Source[token.Start..token.End]);

        /// <summary>Whether what the open group holds so far has a piece in <paramref name="role"/>.</summary>
        public bool Has(string role) => _groups.Peek().Pieces.Any(piece => piece.Role == role);

        /// <summary>Takes <paramref name="token"/> as a piece of <paramref name="kind"/> in <paramref name="role"/>, with the trivia before it.</summary>
        public void Take(Token token, string kind, string role)
        {
            Spaced(token.Start);
            Add(ContentNode.Leaf(kind, Source[token.Start..token.End], role, offset: token.Start));
            At = token.End;
        }

        /// <summary>Takes the trivia up to <paramref name="to"/>.</summary>
        public void Spaced(int to)
        {
            AddRange(Trivia(Source, At, to));
            At = Math.Max(At, to);
        }

        /// <summary>
        /// Takes the words between <paramref name="span"/>'s ends as one piece in <paramref name="role"/> — the space before them the
        /// statement's — or nothing where they are only space.
        /// </summary>
        public void Words((int From, int To) span, string role)
        {
            var start = span.From;
            while (start < span.To && char.IsWhiteSpace(Source[start])) start++;

            var end = span.To;
            while (end > start && char.IsWhiteSpace(Source[end - 1])) end--;

            if (start > At) AddRange(Trivia(Source, At, start));
            if (end > start) Add(ContentWords.Of(Source[start..end], Kinds.Words, role, at: start));

            At = Math.Max(At, end > start ? end : start);
        }

        /// <summary>From where what has been taken ends to the end of its row, or to a <c>%%</c> before it.</summary>
        public (int From, int To) Rest()
        {
            var end = RowEnd(Source, At);
            var comment = Source.IndexOf("%%", At, end - At, StringComparison.Ordinal);
            return (At, comment < 0 ? end : comment);
        }

        /// <summary>Adds a piece made elsewhere to the open group.</summary>
        public void Add(ContentNode piece) => _groups.Peek().Pieces.Add(piece);

        /// <summary>Adds pieces made elsewhere to the open group.</summary>
        public void AddRange(IEnumerable<ContentNode> pieces) => _groups.Peek().Pieces.AddRange(pieces);

        /// <summary>Says what has been taken now ends at <paramref name="end"/>, where pieces added elsewhere ran to.</summary>
        public void Moved(int end) => At = Math.Max(At, end);

        /// <summary>Opens a group, which starts past the trivia before the next token — or where what has been taken ends, where it holds none yet.</summary>
        public void Open() => _groups.Push((At, []));

        /// <summary>Closes the group opened last as a piece of <paramref name="kind"/>, saying what is wrong with it where anything is.</summary>
        public void Close(string kind, string? trouble = null)
        {
            var (start, pieces) = _groups.Pop();
            var offset = pieces.FirstOrDefault(piece => piece.Role != Roles.Trivia)?.Offset ?? start;

            // The trivia a group opened with is the statement's, so the group stands for its own characters.
            var lead = pieces.TakeWhile(piece => piece.Role == Roles.Trivia).ToList();
            var own = pieces.Skip(lead.Count).ToList();

            AddRange(lead);
            Add(ContentNode.Branch(kind, own, offset: own.Count > 0 ? offset : At).Saying(trouble));
        }

        /// <summary>The statement, as a piece of <paramref name="kind"/>, saying what is wrong with it where anything is.</summary>
        public ContentNode Done(string kind, string? trouble = null)
        {
            while (_groups.Count > 1) Close(EventModelingKinds.Name);
            return ContentNode.Branch(kind, _groups.Pop().Pieces, offset: _start).Saying(trouble);
        }
    }
}
