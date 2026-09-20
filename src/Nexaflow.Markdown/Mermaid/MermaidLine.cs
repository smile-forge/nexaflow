using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// One line of a diagram, being read into the node its grammar hands back. The characters every grammar takes the same
/// way are taken here, once: the space between things, the words a line starts with, names bare or in quotes, labels in
/// brackets, lists, numbers, style properties, a title, and a <c>%%</c> comment closing the line.
///
/// <para>
/// <strong>Every grammar reads through this.</strong> A grammar says what a line is made of and in what order; this says
/// how each of those is written and keeps every character it passes as a piece of the node — so a grammar keeps the rule
/// that it only ever copies without having to think about it. What it reads comes out in the shared shapes
/// (<see cref="MermaidKinds.Name"/>, <see cref="MermaidKinds.Label"/>, <see cref="MermaidKinds.Amount"/> and the rest), which
/// is what lets escaping (<see cref="MermaidWriting.Escape"/>), holes, renames and the builders read every diagram alike.
/// </para>
/// <para>
/// <strong>A read that fails leaves a reason or leaves none.</strong> Where the reason is how something was written — a
/// quote never closed — it is in <see cref="Reason"/>; where the line is only not that shape, there is none, and the
/// grammar's own words for the shape it expected are what <see cref="Shown"/> holds the line with.
/// </para>
/// </summary>
public sealed class MermaidLine
{
    /// <summary>The word a title line starts with, in every diagram that has one — see <see cref="Title"/>.</summary>
    public const string TitleWord = "title";

    private readonly string _text;
    private readonly string _body;
    private readonly int _comment;
    private readonly List<ContentNode> _pieces = [];
    private readonly Stack<int> _groups = new();

    private MermaidLine(string text, bool comments)
    {
        _text = text;
        _comment = comments ? Comment(text) : -1;
        _body = _comment < 0 ? text : text[.._comment];
        Written = _body.TrimEnd();
    }

    /// <summary>Reads a line handed to a grammar — to the end of its row — or what follows a header's keyword.</summary>
    /// <param name="comments">Whether a <c>%%</c> outside quotes starts a comment closing the line, as it may on a diagram's own lines.</param>
    public static MermaidLine Of(string text, bool comments = true) => new(text, comments);

    /// <summary>What is read: the line up to any comment closing it, less the space after it.</summary>
    public string Written { get; }

    /// <summary>How far into <see cref="Written"/> the reading has come.</summary>
    public int At { get; private set; }

    /// <summary>Whether everything written has been read.</summary>
    public bool Done => At >= Written.Length;

    /// <summary>What is still to read.</summary>
    public string Rest => Done ? string.Empty : Written[At..];

    /// <summary>The next character, or nought where everything written has been read.</summary>
    public char Next => Done ? '\0' : Written[At];

    /// <summary>Whether space is written next.</summary>
    public bool Spaced => !Done && char.IsWhiteSpace(Written[At]);

    /// <summary>The first character past any space next, or nought where nothing more is written.</summary>
    public char Past
    {
        get
        {
            var next = At;
            while (next < Written.Length && char.IsWhiteSpace(Written[next])) next++;
            return next < Written.Length ? Written[next] : '\0';
        }
    }

    /// <summary>Why the last read that failed did, where that was how something was written — or null.</summary>
    public string? Reason { get; private set; }

    /// <summary>Whether nothing has been read into a piece yet.</summary>
    public bool Empty => _pieces.Count == 0;

    /// <summary>Whether <paramref name="token"/> is written next.</summary>
    public bool Sees(string token) => !Done && Written.AsSpan(At).StartsWith(token, StringComparison.Ordinal);

    /// <summary>
    /// Which of <paramref name="words"/> <paramref name="text"/> starts with, ignoring case — as a word of its own, so
    /// <c>titled</c> is not <c>title</c> — or null. The word comes back as it was asked for, not as it was written.
    /// </summary>
    public static string? Keyword(string text, params string[] words) => Keyword(text, Letter, words);

    /// <summary>
    /// <inheritdoc cref="Keyword(string, string[])" path="/summary"/>
    ///
    /// <para>
    /// <paramref name="letter"/> says what carries a word on, for words punctuation may close rather than space: a domain's
    /// word ends at the arrow in <c>complex--&gt;clear</c>, where a keyword's hyphen carries it on in <c>x-axis</c>.
    /// </para>
    /// </summary>
    public static string? Keyword(string text, Func<char, bool> letter, params string[] words)
    {
        foreach (var word in words)
        {
            if (!text.StartsWith(word, StringComparison.OrdinalIgnoreCase)) continue;
            if (text.Length == word.Length || !letter(text[word.Length])) return word;
        }

        return null;
    }

    /// <summary>What carries a keyword on: its letters, and the hyphen in <c>x-axis</c> or <c>quadrant-1</c>.</summary>
    public static bool Letter(char character) => char.IsLetterOrDigit(character) || character is '_' or '-';

    // ── What every line has ─────────────────────────────────────────────────

    /// <summary>
    /// Takes <paramref name="word"/> where it is written next, as a word of its own and ignoring case — <paramref name="letter"/>
    /// saying what carries it on, where punctuation may close it (<see cref="Keyword(string, Func{char, bool}, string[])"/>).
    /// </summary>
    public bool Word(string word, string kind = MermaidKinds.Key, string role = Roles.Name, Func<char, bool>? letter = null)
    {
        if (Keyword(Rest, letter ?? Letter, word) is null) return false;

        Add(ContentNode.Leaf(kind, Written.Substring(At, word.Length), role));
        return true;
    }

    /// <summary>Takes <paramref name="token"/> where it is written next — a colon, a comma, an arrow — as machinery.</summary>
    public bool Token(string token, string role = Roles.Separator)
    {
        if (!Sees(token)) return false;

        Add(ContentNode.Leaf(Kinds.Token, token, role));
        return true;
    }

    /// <summary>Takes the space written next, if any.</summary>
    public MermaidLine Space()
    {
        var past = At;
        while (past < Written.Length && char.IsWhiteSpace(Written[past])) past++;

        if (past > At) Add(ContentNode.Leaf(Kinds.Space, Written[At..past], Roles.Trivia));
        return this;
    }

    /// <summary>
    /// Takes the space left for something still to be written: the space next — and, where nothing more is written, the
    /// space after the end of the line as well, because past a colon that space is where the reader will type what follows it.
    /// </summary>
    public MermaidLine Room()
    {
        if (!Done) return Space();

        var past = At;
        while (past < _body.Length && char.IsWhiteSpace(_body[past])) past++;

        if (past > At) Add(ContentNode.Leaf(Kinds.Space, _body[At..past], Roles.Trivia));
        return this;
    }

    // ── Pieces made of pieces ───────────────────────────────────────────────

    /// <summary>Starts a piece holding what is read from here until <see cref="Close"/>.</summary>
    public MermaidLine Open()
    {
        _groups.Push(_pieces.Count);
        return this;
    }

    /// <summary>
    /// Ends the piece <see cref="Open"/> started, as a <paramref name="kind"/> holding everything read since — with
    /// <paramref name="trouble"/>, what is wrong with it as a whole, where anything is: values never closed.
    /// </summary>
    public ContentNode Close(string kind, string role = Roles.Element, string? trouble = null)
    {
        var from = _groups.Pop();
        var node = ContentNode.Branch(kind, _pieces.GetRange(from, _pieces.Count - from), role).Saying(trouble);

        _pieces.RemoveRange(from, _pieces.Count - from);
        _pieces.Add(node);
        return node;
    }

    /// <summary>Where the reading has come to, so a grammar can try one reading and go back to try another.</summary>
    public readonly record struct Mark(int At, int Pieces, int Groups);

    public Mark Save() => new(At, _pieces.Count, _groups.Count);

    /// <summary>Goes back to <paramref name="mark"/>, forgetting everything read since — and why any of it failed.</summary>
    public void Restore(Mark mark)
    {
        At = mark.At;
        _pieces.RemoveRange(mark.Pieces, _pieces.Count - mark.Pieces);
        while (_groups.Count > mark.Groups) _groups.Pop();
        Reason = null;
    }

    /// <summary>
    /// Goes back to <paramref name="mark"/> and says why the reading that got there could not go on — <paramref name="reason"/>,
    /// or what the reading itself gave. Always false, which is what a grammar returns where one reading of a line did not work
    /// out and another is to be tried.
    /// </summary>
    public bool Undo(Mark mark, string? reason = null)
    {
        var why = reason ?? Reason;
        Restore(mark);
        if (why is not null) Fail(why);

        return false;
    }

    /// <summary>The pieces read since <paramref name="mark"/>.</summary>
    public IReadOnlyList<ContentNode> Since(Mark mark) => _pieces.GetRange(mark.Pieces, _pieces.Count - mark.Pieces);

    /// <summary>Takes a piece a grammar made itself, as the next thing written.</summary>
    public void Add(ContentNode piece)
    {
        _pieces.Add(piece);
        At += piece.Width;
    }

    /// <summary>Fails a read with the reason: how what is written next was written wrong.</summary>
    public bool Fail(string reason)
    {
        Reason = reason;
        return false;
    }

    // ── What lines are made of ──────────────────────────────────────────────

    /// <summary>
    /// Text in quotes: the quote next, what follows it, and the quote closing it. False where nothing is quoted here, and a
    /// failure with the reason where nothing closes it.
    /// </summary>
    /// <param name="doubled">
    /// Whether a quote written twice inside the quotes is one quote rather than the end of them, which is how a CSV field
    /// holds one.
    /// </param>
    public bool Quoted(string role, string? kind = MermaidKinds.Quoted, string what = "text", bool doubled = false)
    {
        if (Next != '"') return false;

        var close = Closing(doubled);
        if (close < 0) return Fail($"This {what} is never closed with a quote.");

        if (kind is not null) Open();
        Quotes(close, role);
        if (kind is not null) Close(kind);
        return true;
    }

    /// <summary>Where the quotes opening at the reading close, or -1 where nothing closes them.</summary>
    private int Closing(bool doubled)
    {
        for (var at = At + 1; at < Written.Length; at++)
        {
            if (Written[at] != '"') continue;
            if (doubled && at + 1 < Written.Length && Written[at + 1] == '"') { at++; continue; }

            return at;
        }

        return -1;
    }

    /// <summary>
    /// A name, in quotes or bare, as a <see cref="MermaidKinds.Name"/> — bare, the run of characters <paramref name="letter"/>
    /// allows, with what <paramref name="trouble"/> finds wrong with it, where it finds anything.
    /// </summary>
    public bool Name(string role, Func<char, bool> letter, Func<string, string?>? trouble = null)
    {
        if (Next == '"')
        {
            Open();
            if (Quoted(role, kind: null, what: "name"))
            {
                Close(MermaidKinds.Name);
                return true;
            }

            _groups.Pop();
            return false;
        }

        var end = At;
        while (end < Written.Length && letter(Written[end])) end++;
        if (end == At) return Fail("A name is a word, or words in quotes.");

        var name = Written[At..end];
        Open();
        Add(ContentNode.Leaf(MermaidKinds.Words, name, role, trouble?.Invoke(name)));
        Close(MermaidKinds.Name);
        return true;
    }

    /// <summary>
    /// Names with <paramref name="separator"/> between each, each read by <paramref name="name"/>, as a
    /// <see cref="MermaidKinds.Names"/>. Where nothing is written yet, or a separator has nothing written after it yet, a name
    /// still to write follows, standing after the space left for it — which is where typing it puts it.
    /// </summary>
    /// <param name="named">The role of each name in the list, which a name still to write is given too.</param>
    /// <param name="item">
    /// The kind each of the list's items is where an item is more than its name — an axis and its label, a curve and its
    /// values — so each is a piece of its own, a name still to write too; or null where each is only a name.
    /// </param>
    /// <param name="ends">The character closing the list — <c>]</c> — before which a name is still to write too, where the list is in brackets.</param>
    public bool Names(Func<MermaidLine, bool> name, string role, string named, string separator = ",", string? item = null, char? ends = null)
    {
        var mark = Save();
        Open();

        while (true)
        {
            if (Done || Next == ends)
            {
                Add(Unwritten(named, item));
                break;
            }

            if (item is not null) Open();
            if (!name(this))
            {
                var reason = Reason;
                Restore(mark);
                Reason = reason;
                return false;
            }

            if (item is not null) Close(item);

            var next = At;
            while (next < Written.Length && char.IsWhiteSpace(Written[next])) next++;
            if (next >= Written.Length || string.CompareOrdinal(Written, next, separator, 0, separator.Length) != 0) break;

            Space();
            Token(separator);
            Room();
        }

        Close(MermaidKinds.Names, role);
        return true;
    }

    /// <summary>A name still to write — as an item of <paramref name="item"/>, where a list's items are more than their names.</summary>
    private static ContentNode Unwritten(string named, string? item)
    {
        var name = ContentNode.Branch(MermaidKinds.Name, [ContentNode.Leaf(MermaidKinds.Words, string.Empty, named)]);
        return item is null ? name : ContentNode.Branch(item, [name]);
    }

    /// <summary>
    /// A label in its brackets, as a <see cref="MermaidKinds.Label"/>: <c>["Alpha"]</c>, quotes and all, or <c>[Alpha]</c>,
    /// the space round its words its own. The brackets are whatever the diagram writes them as — <c>[</c> and <c>]</c>,
    /// <c>((</c> and <c>))</c>, <c>{{</c> and <c>}}</c>.
    /// </summary>
    public bool Label(string open, string close, string role)
    {
        if (!Sees(open)) return false;

        var mark = Save();
        Open();
        Token(open, Roles.Open);

        if (Next == '"')
        {
            var end = Written.IndexOf("\"" + close, At + 1, StringComparison.Ordinal);
            if (end < 0) return Failed(mark, $"This label is never closed with \"{close}.");

            Open();
            Quotes(end, role);
            Close(MermaidKinds.Quoted);
        }
        else
        {
            var end = Written.IndexOf(close, At, StringComparison.Ordinal);
            if (end < 0) return Failed(mark, $"This label is never closed with {close}.");

            var inner = Written[At..end];
            if (inner.Contains('"')) return Failed(mark, $"A label with a quote in it is written in quotes: {open}\"Alpha\"{close}.");

            var words = inner.Trim();
            var lead = inner.Length - inner.TrimStart().Length;

            if (lead > 0) Add(ContentNode.Leaf(Kinds.Space, inner[..lead], Roles.Trivia));
            Add(ContentNode.Leaf(MermaidKinds.Words, words, role,
                                 words.Length == 0 ? $"A label in brackets has something in it: {open}Alpha{close}, or {open}\"Alpha\"{close}." : null));
            if (inner.Length > lead + words.Length) Add(ContentNode.Leaf(Kinds.Space, inner[(lead + words.Length)..], Roles.Trivia));
        }

        Token(close, Roles.Close);
        Close(MermaidKinds.Label);
        return true;
    }

    /// <summary>
    /// A number, in the place it is written, as a <see cref="MermaidKinds.Amount"/> holding the
    /// <see cref="MermaidKinds.Number"/>: everything up to the first of <paramref name="until"/> or <paramref name="stop"/> — or left
    /// on the line, where neither is given or written — with what <paramref name="trouble"/> finds wrong with it; or nothing, where
    /// nothing is written yet, which is no complaint: it is still to come, and a hole stands there.
    /// </summary>
    /// <param name="until">The characters that end a number written among others — <c>,}</c> in <c>{1, 2}</c>. The space before one is not the number's.</param>
    /// <param name="stop">The token that ends it — <c>--&gt;</c> in <c>0 --&gt; 100</c> — where one is written.</param>
    public void Amount(string role, Func<string, string?> trouble, string? until = null, string? stop = null)
    {
        var number = Upto(until, stop);

        Open();
        Add(ContentNode.Leaf(MermaidKinds.Number, number, role, number.Length == 0 ? null : trouble(number)));
        Close(MermaidKinds.Amount);
    }

    /// <summary>
    /// What a line sets something to — <c>circle</c>, <c>true</c> — as a <see cref="MermaidKinds.Setting"/>: everything up to the
    /// first of <paramref name="until"/>, or left on the line, with what <paramref name="trouble"/> finds wrong with it; or
    /// nothing, where nothing is written yet and it is still to come.
    /// </summary>
    public void Setting(string role, Func<string, string?> trouble, string? until = null)
    {
        var value = Upto(until);
        Add(ContentNode.Leaf(MermaidKinds.Setting, value, role, value.Length == 0 ? null : trouble(value)));
    }

    /// <summary>What is written from here to the first of <paramref name="until"/> or <paramref name="stop"/>, or to the end, less the space before it.</summary>
    private string Upto(string? until, string? stop = null)
    {
        var end = until is null || Done ? -1 : Written.IndexOfAny(until.ToCharArray(), At);
        var token = stop is null || Done ? -1 : Written.IndexOf(stop, At, StringComparison.Ordinal);
        if (token >= 0 && (end < 0 || token < end)) end = token;

        return (end < 0 ? Rest : Written[At..end]).TrimEnd();
    }

    /// <summary>
    /// Properties — a style's <c>fill:#ff6b6b, stroke-width:4px</c>, a card's <c>assigned: knsv, priority: 'High'</c> — as
    /// <see cref="MermaidKinds.Properties"/>, one <see cref="MermaidKinds.Property"/> each, a comma between each. A value runs to the
    /// next comma outside quotes and brackets, so <c>rgb(1, 2, 3)</c> and <c>'Smith, J'</c> are one value. What is wrong with a
    /// value is <see cref="MermaidStyle.Trouble"/>.
    /// </summary>
    /// <param name="known">The properties the diagram sets, or null for any: a property it does not set carries the reason.</param>
    /// <param name="ends">The character closing the properties — <c>}</c> in <c>@{ … }</c> — where they are written in braces.</param>
    /// <param name="what">What the properties are, for the reason a property is not known: <c>A style</c>, <c>Metadata</c>.</param>
    public bool Properties(IReadOnlyCollection<string>? known = null, char? ends = null, string what = "A style", bool spaced = false)
    {
        var mark = Save();
        Open();

        while (true)
        {
            var colon = At;
            while (colon < Written.Length && Written[colon] is not (':' or ',') && Written[colon] != ends) colon++;
            if (colon >= Written.Length || Written[colon] != ':') return Failed(mark, null);

            var name = Written[At..colon].TrimEnd();
            if (name.Length == 0) return Failed(mark, null);

            Open();
            Add(ContentNode.Leaf(MermaidKinds.Key, name, Roles.Name,
                                 known is null || known.Contains(name, StringComparer.OrdinalIgnoreCase)
                                     ? null
                                     : $"{what} sets {string.Join(", ", known.SkipLast(1))} or {known.Last()}, not '{name}'."));
            Space();
            Token(":");
            Space();

            var end = spaced ? ValueWord(Written, At) : ValueEnd(Written, At, ends);
            var value = Written[At..end].TrimEnd();
            Add(ContentNode.Leaf(MermaidKinds.Setting, value, MermaidRoles.Value, MermaidStyle.Trouble(name, value)));
            Space();
            Close(MermaidKinds.Property);

            if (Done || Next == ends) break;

            // Written one after another, the space after a value is all that goes between them.
            if (spaced) continue;

            Token(",");
            Space();
            if (Done || Next == ends) return Failed(mark, null);
        }

        Close(MermaidKinds.Properties);
        return true;
    }

    /// <summary>
    /// What is written, as what it says: everything left on the line — or up to the first of <paramref name="until"/> or
    /// <paramref name="stop"/>, less the space before it, where either is given and written.
    /// </summary>
    public void Words(string role, string? trouble = null, string? until = null, string? stop = null) =>
        Add(ContentNode.Leaf(MermaidKinds.Words, until is null && stop is null ? Rest : Upto(until, stop), role, trouble));

    /// <summary>
    /// What is written from here to <paramref name="end"/>, as what it says, less the space before it — where a rule of the
    /// diagram's own says a word ends rather than any one character doing: a flowchart's id, which a dash carries on or ends
    /// depending on what is written after it.
    /// </summary>
    public void Words(string role, int end, string? trouble = null) =>
        Add(ContentNode.Leaf(MermaidKinds.Words, Written[At..Math.Clamp(end, At, Written.Length)].TrimEnd(), role, trouble));

    /// <summary>Everything left on the line, held as written with the reason — the rest of a line whose start could be read.</summary>
    public void Held(string reason) => Add(ContentNode.Shown(Rest, reason));

    /// <summary>Everything left on the line, read by <paramref name="read"/> — or false, taking nothing, where it reads nothing.</summary>
    public bool Then(Func<string, ContentNode?> read)
    {
        if (read(Rest) is not { } node) return false;

        Add(node);
        return true;
    }

    /// <summary>
    /// A title: its word, and what it says to the end of the line — in quotes or not, where <paramref name="quotes"/> allows
    /// them — as a <see cref="MermaidKinds.Title"/> with what it says as <see cref="MermaidRoles.Title"/>, which is the title
    /// every builder sets over its diagram. Null where the line does not start with the word, or says nothing after it.
    /// </summary>
    public ContentNode? Title(bool quotes = false)
    {
        if (!Word(TitleWord)) return null;

        Space();
        if (Done) return null;

        if (quotes && Rest.Length >= 2 && Next == '"' && Written[^1] == '"') Quotes(Written.Length - 1, MermaidRoles.Title);
        else Words(MermaidRoles.Title);

        return Read(MermaidKinds.Title);
    }

    // ── What comes of it ────────────────────────────────────────────────────

    /// <summary>What was read, as a <paramref name="kind"/> — with the comment closing the line, where one does, kept inside it.</summary>
    public ContentNode Read(string kind, string role = Roles.Element) => Commented(ContentNode.Branch(kind, [.. _pieces], role));

    /// <summary>
    /// What was read as a <paramref name="kind"/>, once the semicolon and the space a line may end with are taken — and the line
    /// as written, with <paramref name="shape"/> saying what one of this kind looks like, where anything else is written there.
    /// </summary>
    public ContentNode Closed(string kind, string shape)
    {
        Space();
        Token(";");
        Space();

        return Done ? Read(kind) : Shown(shape);
    }

    /// <summary>
    /// The line as written, held with why it could not be read: <see cref="Reason"/> where a read gave one, and otherwise
    /// <paramref name="shape"/> — the grammar's words for what a line of this kind looks like.
    /// </summary>
    public ContentNode Shown(string shape) => Commented(ContentNode.Shown(Written, Reason ?? shape));

    // ── Characters ──────────────────────────────────────────────────────────

    /// <summary>
    /// What is written between quotes, and the quotes round it.
    ///
    /// <para>
    /// Words that open with a fence are a whole other content — <c>["```abc CDEF"]</c> is a tune — so they are held
    /// under a node saying so. The words themselves are still there, said and rolled exactly as any others, because
    /// what a label <em>is</em> has not changed: only that something else can read it.
    /// </para>
    /// </summary>
    private void Quotes(int close, string role)
    {
        Add(ContentNode.Leaf(Kinds.Token, "\"", Roles.Open));

        var said = ContentNode.Leaf(MermaidKinds.Words, Written[At..close], role);
        Add(ContentLink.Opens(said.Text) ? ContentNode.Branch(Kinds.Nested, [said], role) : said);

        Add(ContentNode.Leaf(Kinds.Token, "\"", Roles.Close));
    }

    private bool Failed(Mark mark, string? reason)
    {
        Restore(mark);
        Reason = reason;
        return false;
    }

    /// <summary>A line read, with the comment closing it — the space before the comment, and the comment itself.</summary>
    private ContentNode Commented(ContentNode read)
    {
        if (_comment < 0) return read;

        var pieces = new List<ContentNode>();
        if (_comment > read.Width) pieces.Add(ContentNode.Leaf(Kinds.Space, _text[read.Width.._comment], Roles.Trivia));
        pieces.Add(ContentNode.Leaf(Kinds.Comment, _text[_comment..].TrimEnd(), Roles.Trivia));

        return read.IsLeaf
            ? ContentNode.Branch(Kinds.Sequence, [read, .. pieces])
            : read.With([.. read.Children, .. pieces]);
    }

    /// <summary>Where a <c>%%</c> comment starts on a line, outside anything in quotes — or -1.</summary>
    private static int Comment(string text)
    {
        var quoted = false;
        for (var at = 0; at + 1 < text.Length; at++)
        {
            if (text[at] == '"') quoted = !quoted;
            else if (!quoted && text[at] == '%' && text[at + 1] == '%') return at;
        }

        return -1;
    }

    /// <summary>Where a property's value ends: at the next comma — or <paramref name="ends"/> — outside quotes and brackets, or the end of what is written.</summary>
    private static int ValueEnd(string written, int at, char? ends = null)
    {
        var depth = 0;
        char? quote = null;

        for (; at < written.Length; at++)
        {
            var character = written[at];
            if (quote is not null)
            {
                if (character == quote) quote = null;
                continue;
            }

            switch (character)
            {
                case '"' or '\'': quote = character; break;
                case '(': depth++; break;
                case ')': depth = Math.Max(0, depth - 1); break;
                case ',' when depth == 0: return at;
                default:
                    if (character == ends && depth == 0) return at;
                    break;
            }
        }

        return at;
    }

    /// <summary>Where a value written as a word of its own ends: at the space after it, outside anything in quotes.</summary>
    private static int ValueWord(string written, int at)
    {
        char? quote = null;

        for (; at < written.Length; at++)
        {
            var character = written[at];
            if (quote is not null)
            {
                if (character == quote) quote = null;
                continue;
            }

            if (character is '"' or '\'') quote = character;
            else if (char.IsWhiteSpace(character)) break;
        }

        return at;
    }
}
