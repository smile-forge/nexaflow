using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The list a run of words is matched over — its tokens, the runs of emphasis marks with what each can open and close, its
/// brackets — and the matching: CommonMark's, which makes its links and its emphasis.
///
/// <para>
/// <strong>Links are made in two places, from this one list.</strong> Some brackets are settled by what follows them:
/// <c>[words](where)</c> is a link whatever the document defines, and so is an address in angle brackets or written bare, so
/// the reading makes those as it reads. Others wait on a definition that can be written anywhere in the document —
/// <c>[words][name]</c>, <c>[name][]</c>, <c>[name]</c> — and a block is read from its own characters alone, so the reading
/// cannot settle them. It neither guesses nor drops them: the words are read as CommonMark reads them where nothing is
/// defined, and the list is kept on them (<see cref="MarkdownKinds.Delimiters"/>) with the names its brackets wait on.
/// <see cref="WithReferences"/> takes the same list and matches it again with the document's names known — which is what
/// makes a link by name read exactly as CommonMark reads it, <c>*[a*][ref]</c> and all, with no second matcher written
/// beside this one.
/// </para>
/// <para>
/// <strong>The list holds places in the words, never offsets in the document</strong>, so a block kept from the last reading
/// and moved along holds a list that is still true. Matching it again goes over the tokens the first matching read, and reads
/// one afresh only where the second goes another way — past a link the first did not make.
/// </para>
/// <para>
/// Emphasis is matched as Markdig matches it, which is CommonMark's way with its extensions: a run of <c>*</c> or <c>_</c>,
/// and of the extra marks — <c>~</c> struck through or under, <c>^</c> over, <c>++</c> inserted, <c>==</c> marked, <c>""</c>
/// cited — each matched with the nearest run of the same mark before it that can open, taking as many as both have up to what
/// the mark can use.
/// </para>
/// </summary>
internal sealed class MarkdownDelimiters
{
    private readonly string _text;
    private readonly IReadOnlyList<(int From, int To)> _lines;

    /// <summary>The tokens the first matching read, in the order it read them.</summary>
    private readonly MarkdownToken[] _read;

    /// <summary>Every name a bracket here waits on, folded as labels are compared.</summary>
    private readonly string[] _named;

    /// <summary>The stretches the words own, where their lines are slices of lines holding other things too.</summary>
    private readonly IReadOnlyList<(int From, int To)>? _owned;

    private MarkdownDelimiters(string text, IReadOnlyList<(int From, int To)> lines, IReadOnlyList<(int From, int To)>? owned,
                               MarkdownToken[] read, string[] named)
    {
        _text = text;
        _lines = lines;
        _owned = owned;
        _read = read;
        _named = named;
    }

    /// <summary>Where the pieces it was read into stand among the parts of the words holding them: the first.</summary>
    public int First { get; init; }

    /// <summary>How many there are.</summary>
    public int Count { get; init; }

    /// <summary>
    /// The words written on <paramref name="lines"/> of <paramref name="text"/>, as the pieces they are read into where nothing
    /// is defined — covering the lines from where the first starts to where the last ends, what stands between two lines left
    /// as the trivia it is — and the list, where a link in them waits on a name.
    /// </summary>
    /// <param name="at">Where <paramref name="text"/>'s first character stands in the document holding it.</param>
    /// <param name="first">Where the pieces will stand among the parts of the words holding them.</param>
    /// <param name="owned">
    /// The stretches of <paramref name="text"/> the words own, where that is not everything from their first line to their last:
    /// what stands between two of them is no trivia of theirs (<see cref="MarkdownTokenizer.Owned"/>).
    /// </param>
    public static ContentNode[] Read(string text, IReadOnlyList<(int From, int To)> lines, int at, int first, out MarkdownDelimiters? waiting,
                                     IReadOnlyList<(int From, int To)>? owned = null)
    {
        var matching = Matching.Taken();

        try
        {
            var pieces = matching.Matched(new MarkdownTokenizer(text, lines, owned), at, null, null);

            waiting = matching.Named.Count > 0
                ? new MarkdownDelimiters(text, lines, owned, [.. matching.Recorded], [.. matching.Named]) { First = first, Count = pieces.Length }
                : null;

            return pieces;
        }
        finally
        {
            Matching.Given(matching);
        }
    }

    /// <summary>Whether a name a bracket here waits on is one <paramref name="defined"/> says where it goes.</summary>
    public bool Answered(Func<string, LinkTarget?> defined)
    {
        foreach (var name in _named)
            if (defined(name) is not null) return true;

        return false;
    }

    /// <summary>The same words matched again, every link by a name <paramref name="defined"/> knows made one.</summary>
    /// <param name="at">Where the text the words were read from now stands in the document.</param>
    public ContentNode[] Again(int at, Func<string, LinkTarget?> defined)
    {
        var matching = Matching.Taken();

        try
        {
            return matching.Matched(new MarkdownTokenizer(_text, _lines, _owned), at, defined, _read);
        }
        finally
        {
            Matching.Given(matching);
        }
    }

    /// <summary>What a run of a mark makes, by how many of it are used.</summary>
    private static string Emphasised(char mark, int count) => (mark, count) switch
    {
        ('"', _) => MarkdownKinds.Citation,
        ('~', 2) => MarkdownKinds.Strike,
        ('~', _) => MarkdownKinds.Sub,
        ('^', _) => MarkdownKinds.Sup,
        ('=', _) => MarkdownKinds.Mark,
        ('+', _) => MarkdownKinds.Insert,
        (_, 2) => MarkdownKinds.Strong,
        _ => MarkdownKinds.Emphasis,
    };

    // ── One matching ─────────────────────────────────────────────────────────

    /// <summary>What one piece of the run is while it is matched.</summary>
    private enum Shape : byte
    {
        /// <summary>A token, standing for itself.</summary>
        Token,

        /// <summary>A run of an emphasis mark, what of it is not used yet.</summary>
        Run,

        /// <summary>A bracket opening, that may yet open a link.</summary>
        Opener,

        /// <summary>A link or emphasis already made.</summary>
        Made,
    }

    private struct Piece
    {
        public Shape Shape;
        public MarkdownToken Token;

        /// <summary>The characters it stands for — for a run, those of it not used yet.</summary>
        public int Start;

        public int End;

        public ContentNode? Made;

        public int Previous;
        public int Next;

        /// <summary>The runs below and above it on the stack of runs not matched yet.</summary>
        public int Below;

        public int Above;

        /// <summary>Whether a bracket can still open a link — none can, inside a link.</summary>
        public bool Active;

        /// <summary>Whether another bracket was opened after this one, so its words cannot be a name.</summary>
        public bool Bracketed;

        /// <summary>The top of the stack of runs when the bracket was opened — the runs inside its words are those above it.</summary>
        public int Bottom;
    }

    /// <summary>
    /// A matching of a run: CommonMark's walk over the tokens, a stack of the runs not matched yet and one of the brackets
    /// not closed yet, emphasis made inside a link when it is made and over the whole run at its end.
    ///
    /// <para>
    /// What it works with — the pieces, the stacks, the tokens it read — is the same few lists for every run a thread matches,
    /// grown to the longest it has matched: a document is thousands of runs of words, and what is made of each is only the
    /// pieces it is read into.
    /// </para>
    /// </summary>
    private sealed class Matching
    {
        /// <summary>The matching each thread keeps for the next run it reads.</summary>
        [ThreadStatic]
        private static Matching? t_kept;

        private MarkdownTokenizer _tokens = null!;
        private int _at;
        private Func<string, LinkTarget?>? _defined;

        /// <summary>The tokens a matching before this one read, where this one matches the same words again.</summary>
        private MarkdownToken[]? _replay;

        private readonly List<MarkdownToken> _recorded = [];
        private readonly HashSet<string> _named = new(StringComparer.Ordinal);
        private readonly List<ContentNode> _parts = [];

        private Piece[] _pieces = new Piece[32];
        private int _count;
        private int _head = -1;
        private int _tail = -1;

        /// <summary>The top of the stack of runs.</summary>
        private int _top = -1;

        private readonly List<int> _brackets = [];

        /// <summary>Where in the tokens read before the walk has got to.</summary>
        private int _replayed;

        /// <summary>Whether a link written as raw HTML is open — no address written bare is a link inside one.</summary>
        private bool _anchored;

        /// <summary>The thread's matching, or a new one where it is in use already.</summary>
        public static Matching Taken()
        {
            var matching = t_kept ?? new Matching();
            t_kept = null;

            return matching;
        }

        /// <summary>A matching done with, kept for the thread's next run — holding nothing of this one.</summary>
        public static void Given(Matching matching)
        {
            Array.Clear(matching._pieces, 0, matching._count);
            matching._count = 0;
            matching._tokens = null!;
            matching._defined = null;
            matching._replay = null;
            matching._parts.Clear();

            t_kept = matching;
        }

        /// <summary>The tokens this matching read, where it read them afresh rather than going over another's.</summary>
        public List<MarkdownToken> Recorded => _recorded;

        /// <summary>Every name a bracket waited on.</summary>
        public HashSet<string> Named => _named;

        private bool Recording => _replay is null;

        private string Text => _tokens.Text;

        /// <summary>
        /// The words <paramref name="tokens"/> reads, matched — links by name made where <paramref name="defined"/> knows the name,
        /// over the tokens <paramref name="replay"/> holds where it holds any.
        /// </summary>
        public ContentNode[] Matched(MarkdownTokenizer tokens, int at, Func<string, LinkTarget?>? defined, MarkdownToken[]? replay)
        {
            _tokens = tokens;
            _at = at;
            _defined = defined;
            _replay = replay;

            _count = 0;
            _head = _tail = _top = -1;
            _brackets.Clear();
            _recorded.Clear();
            _named.Clear();
            _replayed = 0;
            _anchored = false;

            var place = tokens.Start;
            var end = tokens.End;

            while ((place = _tokens.Resumed(place)) < end)
            {
                var token = this.Token(place, plain: false);

                switch (token.Kind)
                {
                    case MarkdownTokenKind.Run:
                        this.Stacked(this.Added(Shape.Run, token));
                        break;

                    case MarkdownTokenKind.Open or MarkdownTokenKind.Picture:
                        this.Bracketed(token);
                        break;

                    case MarkdownTokenKind.Close:
                        place = this.Closed(token);
                        continue;

                    case MarkdownTokenKind.Bare:
                        if (this.Bared(token) is var stop and >= 0)
                        {
                            this.Added(Shape.Token, token with { End = stop });
                            place = stop;
                        }
                        else
                        {
                            var plain = this.Token(place, plain: true);
                            this.Added(Shape.Token, plain);
                            place = plain.End;
                        }

                        continue;

                    case MarkdownTokenKind.Html:
                        this.Added(Shape.Token, token);
                        this.Anchors(token);
                        break;

                    // The next line's words start where its own characters do, past whatever its containers put in front of them.
                    case MarkdownTokenKind.Break or MarkdownTokenKind.HardBreak:
                        this.Added(Shape.Token, token);
                        place = _tokens.Following(token.Start);
                        continue;

                    default:
                        this.Added(Shape.Token, token);
                        break;
                }

                place = token.End;
            }

            this.Emphasise(-1);

            return this.Pieces(_head, -1, _tokens.Start, end);
        }

        /// <summary>
        /// The token at <paramref name="place"/>: the one the first matching read there, where it read one — and where this
        /// matching has gone another way, the tokenizer's.
        /// </summary>
        private MarkdownToken Token(int place, bool plain)
        {
            if (_replay is { } read)
            {
                while (_replayed < read.Length && read[_replayed].Start < place) _replayed++;

                for (var look = _replayed; look < read.Length && read[look].Start == place; look++)
                    if (read[look].Plain == plain) return read[look];

                return _tokens.Token(place, plain);
            }

            var token = _tokens.Token(place, plain);
            _recorded.Add(token);

            return token;
        }

        // ── The list ─────────────────────────────────────────────────────────

        private int Added(Shape shape, MarkdownToken token)
        {
            if (_count == _pieces.Length) Array.Resize(ref _pieces, _count * 2);

            var index = _count++;
            _pieces[index] = new Piece
            {
                Shape = shape, Token = token, Start = token.Start, End = token.End, Previous = _tail, Next = -1, Below = -1, Above = -1,
            };

            if (_tail >= 0) _pieces[_tail].Next = index;
            else _head = index;

            _tail = index;
            return index;
        }

        /// <summary>Takes a piece out of the list.</summary>
        private void Unlinked(int index)
        {
            ref var piece = ref _pieces[index];

            if (piece.Previous >= 0) _pieces[piece.Previous].Next = piece.Next;
            else _head = piece.Next;

            if (piece.Next >= 0) _pieces[piece.Next].Previous = piece.Previous;
            else _tail = piece.Previous;
        }

        /// <summary>Puts what was made of the pieces after <paramref name="after"/> and before <paramref name="before"/> in their place.</summary>
        private void Replaced(int after, int before, ContentNode made, int start, int end)
        {
            var index = this.Added(Shape.Made, default);

            // Added at the tail; moved to where the pieces it was made of stood.
            this.Unlinked(index);

            ref var piece = ref _pieces[index];
            piece.Made = made;
            piece.Start = start;
            piece.End = end;
            piece.Previous = after;
            piece.Next = before;

            if (after >= 0) _pieces[after].Next = index;
            else _head = index;

            if (before >= 0) _pieces[before].Previous = index;
            else _tail = index;
        }

        // ── Runs ─────────────────────────────────────────────────────────────

        private void Stacked(int index)
        {
            _pieces[index].Below = _top;
            if (_top >= 0) _pieces[_top].Above = index;

            _top = index;
        }

        private void Unstacked(int index)
        {
            ref var piece = ref _pieces[index];

            if (piece.Below >= 0) _pieces[piece.Below].Above = piece.Above;
            if (piece.Above >= 0) _pieces[piece.Above].Below = piece.Below;
            if (_top == index) _top = piece.Below;

            piece.Below = piece.Above = -1;
        }

        private int Left(int index) => _pieces[index].End - _pieces[index].Start;

        /// <summary>
        /// Every run above <paramref name="bottom"/> on the stack matched with the nearest before it that it can close —
        /// Markdig's way, the runs between two that match taken off as the characters they are — and then all of them off the
        /// stack, whatever is not used of them the characters it is.
        /// </summary>
        private void Emphasise(int bottom)
        {
            var closer = _top;
            if (closer < 0 || closer == bottom) return;

            while (_pieces[closer].Below >= 0 && _pieces[closer].Below != bottom) closer = _pieces[closer].Below;

            while (closer >= 0)
            {
                var next = _pieces[closer].Above;
                var mark = _pieces[closer].Token.Mark;
                var least = MarkdownTokenizer.Least(mark);
                var most = MarkdownTokenizer.Most(mark);

                if (_pieces[closer].Token.Closes)
                {
                    while (this.Left(closer) >= least)
                    {
                        var opener = this.Opener(closer, bottom, least);

                        if (opener < 0)
                        {
                            if (!_pieces[closer].Token.Opens) this.Unstacked(closer);
                            break;
                        }

                        var done = false;

                        while (true)
                        {
                            var use = Math.Min(Math.Min(this.Left(opener), this.Left(closer)), most);
                            this.Embraced(opener, closer, use);

                            if (this.Left(closer) == 0)
                            {
                                this.Unstacked(closer);
                                this.Unlinked(closer);

                                if (this.Left(opener) == 0)
                                {
                                    this.Unstacked(opener);
                                    this.Unlinked(opener);
                                }

                                done = true;
                                break;
                            }

                            if (this.Left(opener) >= least && this.Left(closer) >= least) continue;

                            this.Unstacked(opener);
                            if (this.Left(opener) == 0) this.Unlinked(opener);

                            break;
                        }

                        if (done) break;
                    }
                }

                closer = next;
            }

            while (_top >= 0 && _top != bottom) this.Unstacked(_top);
        }

        /// <summary>The nearest run below <paramref name="closer"/> and above <paramref name="bottom"/> it can close, or -1.</summary>
        private int Opener(int closer, int bottom, int least)
        {
            ref var closing = ref _pieces[closer];

            for (var opener = closing.Below; opener >= 0 && opener != bottom; opener = _pieces[opener].Below)
            {
                ref var opening = ref _pieces[opener];
                if (opening.Token.Mark != closing.Token.Mark || !opening.Token.Opens || this.Left(opener) < least) continue;

                // A run that could both open and close is not matched with one whose length makes a multiple of three with it,
                // unless both are — counted as they stand now, as Markdig counts them.
                var openerLeft = this.Left(opener);
                var closerLeft = this.Left(closer);
                var odd = (closing.Token.Opens || opening.Token.Closes) && openerLeft != closerLeft && (openerLeft + closerLeft) % 3 == 0
                          && (openerLeft % 3 != 0 || closerLeft % 3 != 0);

                if (!odd) return opener;
            }

            return -1;
        }

        /// <summary>
        /// Emphasis made of the pieces between two runs, each giving up <paramref name="use"/> of its marks — the opener the
        /// last of them, the closer the first — and every run between taken off as the characters it is.
        /// </summary>
        private void Embraced(int opener, int closer, int use)
        {
            for (var between = _pieces[closer].Below; between >= 0 && between != opener;)
            {
                var below = _pieces[between].Below;
                this.Unstacked(between);
                between = below;
            }

            var opens = _pieces[opener].End - use;
            var shuts = _pieces[closer].Start + use;

            var open = this.Leaf(Kinds.Token, opens, _pieces[opener].End, Roles.Open);
            var body = this.Pieces(_pieces[opener].Next, closer, _pieces[opener].End, _pieces[closer].Start);
            var close = this.Leaf(Kinds.Token, _pieces[closer].Start, shuts, Roles.Close);

            ContentNode[] parts = body.Length > 0 ? [open, ContentNode.Branch(Kinds.Words, body, Roles.Body), close] : [open, close];

            this.Replaced(opener, closer, ContentNode.Branch(Emphasised(_pieces[closer].Token.Mark, use), parts), opens, shuts);

            _pieces[opener].End = opens;
            _pieces[closer].Start = shuts;
        }

        // ── Brackets ─────────────────────────────────────────────────────────

        private void Bracketed(MarkdownToken token)
        {
            var index = this.Added(Shape.Opener, token);

            _pieces[index].Active = true;
            _pieces[index].Bottom = _top;

            if (_brackets.Count > 0) _pieces[_brackets[^1]].Bracketed = true;
            _brackets.Add(index);
        }

        /// <summary>
        /// A <c>]</c>: the link it closes, where the nearest bracket open before it can open one and what follows makes it one —
        /// an address in parentheses, or a name the document defines — and the bracket itself otherwise. Where the walk goes on.
        /// </summary>
        private int Closed(MarkdownToken token)
        {
            var after = token.End;

            if (_brackets.Count == 0)
            {
                this.Added(Shape.Token, token);
                return after;
            }

            var opener = _brackets[^1];

            if (!_pieces[opener].Active)
            {
                _brackets.RemoveAt(_brackets.Count - 1);
                this.Added(Shape.Token, token);
                return after;
            }

            var tail = _tokens.Inline(after);
            LinkTarget? meaning = null;
            var end = after;

            if (tail is { } inline)
            {
                end = inline.End;
            }
            else
            {
                var label = _tokens.Label(after);
                string? name = null;

                if (label >= 0 && label - after > 2) name = _tokens.Between(after + 1, label - 1);
                else if (!_pieces[opener].Bracketed) name = _tokens.Between(_pieces[opener].Token.End, token.Start);

                if (name is not null && MarkdownLabels.Folded(name) is { Length: > 0 } folded)
                {
                    if (this.Recording) _named.Add(folded);

                    if (_defined?.Invoke(folded) is { } defines)
                    {
                        meaning = defines;
                        end = label >= 0 ? label : after;
                    }
                }
            }

            if (tail is null && meaning is null)
            {
                _brackets.RemoveAt(_brackets.Count - 1);
                this.Added(Shape.Token, token);
                return after;
            }

            this.Linked(opener, token, tail, meaning, end);
            return end;
        }

        /// <summary>The link a bracket opened and <paramref name="close"/> closed, made of everything between and what follows it.</summary>
        private void Linked(int opener, MarkdownToken close, MarkdownTail? tail, LinkTarget? meaning, int end)
        {
            var open = _pieces[opener].Token;
            var picture = open.Kind == MarkdownTokenKind.Picture;
            var kind = picture ? MarkdownKinds.Image : Kinds.Link;

            this.Emphasise(_pieces[opener].Bottom);

            IReadOnlyList<ContentNode> words = this.Pieces(_pieces[opener].Next, -1, open.End, close.Start);
            (ContentNode Written, MarkdownPictureSize Size)? sized = null;

            // A picture's words can end in the size it asks to be drawn at, which is no part of what they say.
            if (picture)
            {
                var said = new List<ContentNode>(words);
                sized = MarkdownPictureSize.Taken(said);
                words = said;
            }

            List<ContentNode> parts = new(8) { this.Leaf(Kinds.Token, open.Start, open.End, Roles.Open) };
            if (words.Count > 0) parts.Add(ContentNode.Branch(Kinds.Words, words, Roles.Body));
            if (sized is { } size) parts.Add(size.Written);

            parts.Add(this.Leaf(Kinds.Token, close.Start, close.End, Roles.Close));

            var goes = meaning?.Meaning;

            if (tail is { } inline)
            {
                var from = close.End;

                if (inline.Destination.To > inline.Destination.From)
                {
                    this.Marks(parts, from, inline.Destination.From, Roles.Trivia);
                    parts.Add(this.Leaf(MarkdownKinds.Word, inline.Destination.From, inline.Destination.To, Roles.Destination));

                    var written = this.Text[inline.Destination.From..inline.Destination.To];
                    if (MarkdownLinkSyntax.Unescaped(written) is var unescaped && unescaped != written) goes = unescaped;

                    from = inline.Destination.To;
                }

                if (inline.Title is { } title)
                {
                    this.Marks(parts, from, title.From, Roles.Trivia);
                    parts.Add(this.Spanning(MarkdownKinds.Word, title.From, title.To, MarkdownRoles.Title));
                    from = title.To;
                }

                this.Marks(parts, from, end, Roles.Close);
            }
            else
            {
                this.Marks(parts, close.End, end, Roles.Close);
            }

            var link = ContentNode.Branch(kind, parts);
            if (sized is { } asked) link = link.Holding(MarkdownKinds.Size, MarkdownRoles.Size, asked.Size);
            if (goes is not null) link = link.Holding(kind, Roles.Destination, goes);

            // A link by name says about itself what its definition's title does.
            if (meaning?.Title is { } titled) link = link.Holding(kind, MarkdownRoles.Title, titled);

            // Everything from the bracket on is the link's now, so the link is what stands there instead.
            this.Replaced(_pieces[opener].Previous, -1, link, open.Start, end);

            _brackets.RemoveAt(_brackets.Count - 1);

            // A link holds no link, so no bracket before this one can open one round it — a picture still can.
            if (!picture)
                foreach (var bracket in _brackets)
                    if (_pieces[bracket].Token.Kind == MarkdownTokenKind.Open) _pieces[bracket].Active = false;
        }

        // ── Addresses written bare ───────────────────────────────────────────

        /// <summary>
        /// Where an address written bare ends as a link, or -1 where it is none here: inside a link's words — a bracket still
        /// open before it that could open one — or a link written in HTML, none is. The marks of emphasis opened round it are
        /// not its own, at its end.
        /// </summary>
        private int Bared(MarkdownToken token)
        {
            if (_anchored) return -1;

            foreach (var bracket in _brackets)
                if (_pieces[bracket].Active) return -1;

            Span<char> pending = stackalloc char[7];
            var count = 0;

            for (var run = _top; run >= 0; run = _pieces[run].Below)
                if (pending[..count].IndexOf(_pieces[run].Token.Mark) < 0 && count < pending.Length) pending[count++] = _pieces[run].Token.Mark;

            return _tokens.Bared(token.Start, token.End, pending[..count]);
        }

        /// <summary>Whether a tag opens a link written in HTML, or closes one.</summary>
        private void Anchors(MarkdownToken html)
        {
            var tag = this.Text.AsSpan(html.Start, html.End - html.Start);

            if (tag.StartsWith("</a", StringComparison.OrdinalIgnoreCase)) _anchored = false;
            else if (tag.StartsWith("<a", StringComparison.OrdinalIgnoreCase)) _anchored = true;
        }

        // ── Pieces ───────────────────────────────────────────────────────────

        /// <summary>
        /// The pieces from <paramref name="first"/> up to <paramref name="stop"/> — the end of the list where that is -1 — as the
        /// nodes they are, covering <paramref name="from"/> to <paramref name="to"/>: words standing side by side one word, and
        /// whatever falls between two pieces — the marks of the containers holding the lines — the trivia it is. Gathered in the
        /// one list the matching keeps for it, and handed out as exactly as many as there are.
        /// </summary>
        private ContentNode[] Pieces(int first, int stop, int from, int to)
        {
            var parts = _parts;
            parts.Clear();

            var accounted = from;
            var words = -1;

            for (var index = first; index >= 0 && index != stop; index = _pieces[index].Next)
            {
                ref var piece = ref _pieces[index];
                // A run used up is nothing. A break between two slices of a cell's lines is a break all the same, though its
                // line ending is somebody else's.
                if (piece.End <= piece.Start && piece.Shape != Shape.Made && piece.Token.Kind is not (MarkdownTokenKind.Break or MarkdownTokenKind.HardBreak))
                    continue;

                if (piece.Start > accounted)
                {
                    this.Worded(parts, ref words, accounted);
                    this.Marks(parts, accounted, piece.Start, Roles.Trivia);
                }

                if (this.Plain(piece))
                {
                    if (words < 0) words = piece.Start;
                }
                else
                {
                    this.Worded(parts, ref words, piece.Start);
                    parts.Add(piece.Made ?? this.Node(piece.Token));
                }

                accounted = piece.End;
            }

            this.Worded(parts, ref words, accounted);
            if (to > accounted) this.Marks(parts, accounted, to, Roles.Trivia);

            var made = parts.ToArray();
            parts.Clear();

            return made;
        }

        /// <summary>Whether a piece is nothing but the characters it is written with: words, and every mark nothing matched.</summary>
        private bool Plain(in Piece piece) =>
            piece.Shape is Shape.Run or Shape.Opener
            || (piece.Shape == Shape.Token && piece.Token.Kind is MarkdownTokenKind.Text or MarkdownTokenKind.Close);

        /// <summary>The words gathered since <paramref name="words"/>, up to <paramref name="end"/>, as the one word they are.</summary>
        private void Worded(List<ContentNode> parts, ref int words, int end)
        {
            if (words < 0) return;

            if (end > words) parts.Add(this.Leaf(MarkdownKinds.Word, words, end, Roles.Element));
            words = -1;
        }

        /// <summary>A token as the node it is.</summary>
        private ContentNode Node(MarkdownToken token)
        {
            switch (token.Kind)
            {
                case MarkdownTokenKind.Space:
                    return this.Leaf(Kinds.Space, token.Start, token.End, Roles.Trivia);

                // Whether the writer asked for the line to break is settled by what stands before the ending, which the reader
                // has already looked at; it is said here so nothing after has to look again.
                case MarkdownTokenKind.Break:
                    return this.Leaf(MarkdownKinds.Break, token.Start, token.End, Roles.Separator);

                case MarkdownTokenKind.HardBreak:
                    return this.Leaf(MarkdownKinds.Break, token.Start, token.End, MarkdownRoles.Hard);

                // Two characters that mean one, which is exactly what an entity is — so it is its own piece, drawn as the
                // character it stands for while still being the two that were typed.
                case MarkdownTokenKind.Escape:
                    return this.Leaf(MarkdownKinds.Escape, token.Start, token.End, Roles.Element);

                case MarkdownTokenKind.Entity:
                    return this.Leaf(MarkdownKinds.Entity, token.Start, token.End, Roles.Element);

                case MarkdownTokenKind.Html:
                    return this.Spanning(MarkdownKinds.Html, token.Start, token.End, Roles.Element);

                // Nothing inside a code span is read: an asterisk in there is an asterisk.
                case MarkdownTokenKind.Code:
                    return ContentNode.Branch(MarkdownKinds.Code,
                    [
                        this.Leaf(Kinds.Token, token.Start, token.Inner, Roles.Open),
                        this.Spanning(Kinds.Verbatim, token.Inner, token.InnerEnd, Roles.Body),
                        this.Leaf(Kinds.Token, token.InnerEnd, token.End, Roles.Close),
                    ]);

                // An address written bare is a link to itself just as one in angle brackets is.
                case MarkdownTokenKind.Autolink:
                    return ContentNode.Branch(Kinds.Link,
                    [
                        this.Leaf(Kinds.Token, token.Start, token.Inner, Roles.Open),
                        this.Leaf(MarkdownKinds.Word, token.Inner, token.InnerEnd, Roles.Destination),
                        this.Leaf(Kinds.Token, token.InnerEnd, token.End, Roles.Close),
                    ]);

                case MarkdownTokenKind.Bare:
                    return ContentNode.Branch(Kinds.Link, [this.Leaf(MarkdownKinds.Word, token.Start, token.End, Roles.Destination)]);

                // A formula in a sentence: a block in LaTeX holding its opening dollars, the formula as written (unread), and the
                // closing ones.
                case MarkdownTokenKind.Formula when token.InnerEnd > token.Inner:
                    return new BlockNode(MarkdownParser.Maths,
                    [
                        this.Leaf(Kinds.Token, token.Start, token.Inner, Roles.Open),
                        this.Leaf(Kinds.Nested, token.Inner, token.InnerEnd, Roles.Body),
                        this.Leaf(Kinds.Token, token.InnerEnd, token.End, Roles.Close),
                    ], offset: _at + token.Start);

                case MarkdownTokenKind.Formula:
                    return this.Leaf(MarkdownKinds.Formula, token.Start, token.End, Roles.Element);

                default:
                    return this.Leaf(MarkdownKinds.Word, token.Start, token.End, Roles.Element);
            }
        }

        private ContentNode Leaf(string kind, int from, int to, string role) =>
            ContentNode.Leaf(kind, this.Text[from..to], role, offset: _at + from);

        /// <summary>
        /// The characters from <paramref name="from"/> to <paramref name="to"/> as one piece — or, where they run over the marks
        /// of the containers holding the lines, as the stretches on each line with those marks between them as trivia.
        /// </summary>
        private ContentNode Spanning(string kind, int from, int to, string role)
        {
            if (!this.Gapped(from, to)) return this.Leaf(kind, from, to, role);

            var parts = new List<ContentNode>();
            this.Marks(parts, from, to, Roles.Element, kind);

            return ContentNode.Branch(kind, parts, role);
        }

        /// <summary>Whether the marks of a container stand anywhere between <paramref name="from"/> and <paramref name="to"/>.</summary>
        private bool Gapped(int from, int to)
        {
            if (_tokens.Owned is { } owned)
            {
                foreach (var (start, end) in owned)
                    if (start <= from && to <= end) return false;

                return true;
            }

            for (var line = _tokens.LineOf(from); line + 1 < _tokens.Lines.Count && _tokens.Ending(line) < to; line++)
                if (_tokens.Lines[line + 1].From > _tokens.Ending(line)) return true;

            return false;
        }

        /// <summary>
        /// The characters from <paramref name="from"/> to <paramref name="to"/>: what stands on each line, with its line ending, as
        /// a piece of <paramref name="role"/>, and the marks of the containers holding the next line between them as trivia.
        /// </summary>
        private void Marks(List<ContentNode> parts, int from, int to, string role, string kind = Kinds.Token)
        {
            // Slices of lines holding other things too: only what the words own is theirs.
            if (_tokens.Owned is { } owned)
            {
                foreach (var (start, end) in owned)
                {
                    var (cut, stop) = (Math.Max(start, from), Math.Min(end, to));
                    if (stop > cut) parts.Add(role == Roles.Trivia ? this.Gap(cut, stop) : this.Leaf(kind, cut, stop, role));
                }

                return;
            }

            var next = from;
            var line = _tokens.LineOf(from);

            while (next < to)
            {
                var ending = _tokens.Ending(line);

                if (next < ending)
                {
                    var stop = Math.Min(ending, to);
                    parts.Add(role == Roles.Trivia ? this.Gap(next, stop) : this.Leaf(kind, next, stop, role));
                    next = stop;
                    continue;
                }

                var own = line + 1 < _tokens.Lines.Count ? Math.Min(_tokens.Lines[line + 1].From, to) : to;
                if (own > next) parts.Add(this.Gap(next, own));

                next = own;
                line++;
            }
        }
        /// <summary>Characters standing between two pieces, as the trivia they are: space as space, anything else as a mark.</summary>
        private ContentNode Gap(int from, int to)
        {
            var text = this.Text[from..to];
            return ContentNode.Leaf(text.AsSpan().IsWhiteSpace() ? Kinds.Space : Kinds.Token, text, Roles.Trivia, offset: _at + from);
        }
    }
}
