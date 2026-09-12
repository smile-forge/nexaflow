using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Music.LilyPond;

/// <summary>
/// Reads LilyPond into a tree that prints back exactly what was read.
///
/// <para>
/// <strong>The parser only ever copies</strong> — the promise ABC's parser makes, for the same reason. Every
/// leaf's text is found in the source at the offset the tree puts it at, so an editor holding a half-typed
/// <c>\relative c' { c4 d</c> holds exactly that, unclosed brace and all.
/// </para>
/// <para>
/// It reads the shape LilyPond's own grammar gives what was typed, and nothing it means. LilyPond is commands
/// and the arguments they take, so a <c>\relative</c> is one node holding its start pitch and the music it
/// applies to, a <c>\new Staff</c> holds its name and its music, and a definition holds the name and what it is
/// set to. Which octave a note is in, how long it lasts, where the bars fall and which notes beam together are
/// facts about what came before, and are left to the stages and the builder.
/// </para>
/// <para>
/// Three lexers in one, because LilyPond has three: inside <c>\lyricmode</c> a <c>--</c> is a hyphen and "sky."
/// is a word, and inside <c>\chordmode</c> <c>g1:7</c> is a chord's name. Which one reads a group is decided by
/// the command that opens it, which is when LilyPond decides it too.
/// </para>
/// <para>
/// Anything unreadable is <em>held</em> — a verbatim leaf saying why — rather than lost or thrown.
/// </para>
/// </summary>
public static class LilyPondParser
{
    /// <summary>Reads LilyPond.</summary>
    public static ContentNode Parse(string source) =>
        ContentNode.Branch(LilyPondKinds.File, new Reader(source).Items(Mode.Music));

    /// <summary>Which of LilyPond's lexers is reading.</summary>
    private enum Mode { Music, Lyrics, Chords }

    /// <summary>The modes a <c>\key</c> can be in.</summary>
    private static readonly HashSet<string> Modes =
        ["major", "minor", "ionian", "dorian", "phrygian", "lydian", "mixolydian", "aeolian", "locrian"];

    private sealed class Reader(string s)
    {
        private int _at;

        /// <summary>
        /// What closes each group being read, innermost last. A closer belonging to a group further out still
        /// stops the ones inside it, so a brace left open inside a <c>&lt;&lt; &gt;&gt;</c> ends at the
        /// <c>&gt;&gt;</c> rather than swallowing everything after it.
        /// </summary>
        private readonly List<string> _closers = [];

        // ── Runs of things ──────────────────────────────────────────────────

        /// <summary>Everything up to what closes the group being read, or to the end.</summary>
        public List<ContentNode> Items(Mode mode)
        {
            var items = new List<ContentNode>();

            while (_at < s.Length)
            {
                if (Trivia() is { } trivia) { items.Add(trivia); continue; }
                if (Closes()) break;
                items.Add(Item(mode));
            }

            return items;
        }

        /// <summary>Whether what is here closes a group being read — its own, or one around it.</summary>
        private bool Closes()
        {
            for (var i = _closers.Count - 1; i >= 0; i--)
                if (At(_closers[i])) return true;

            return false;
        }

        private bool At(string text) => s.AsSpan(_at).StartsWith(text, StringComparison.Ordinal);

        /// <summary>One thing, which always takes at least one character.</summary>
        private ContentNode Item(Mode mode)
        {
            var c = s[_at];

            if (c == '{') return Group(LilyPondKinds.Sequential, "{", "}", mode);
            if (At("<<")) return Group(LilyPondKinds.Simultaneous, "<<", ">>", mode);
            if (c == '"') return Quoted(mode);
            if (c is '#' or '$') return Scheme();
            if (c == '\\') return Backslash(mode);

            if (c == '}') return Held(1, "there is nothing for this } to close");
            if (At(">>")) return Held(2, "there is nothing for this >> to close");
            if (c == '>') return Held(1, "there is nothing for this > to close");

            return mode switch
            {
                Mode.Lyrics => Lyric(),
                Mode.Chords => ChordName(),
                _ => Music(),
            };
        }

        /// <summary>Space and comments, which belong to wherever they fell.</summary>
        private ContentNode? Trivia()
        {
            if (_at >= s.Length) return null;

            var from = _at;

            if (char.IsWhiteSpace(s[_at]))
            {
                while (_at < s.Length && char.IsWhiteSpace(s[_at])) _at++;
                return ContentNode.Leaf(Kinds.Space, s[from.._at], Roles.Trivia);
            }

            if (s[_at] != '%') return null;

            if (At("%{"))
            {
                var close = s.IndexOf("%}", _at + 2, StringComparison.Ordinal);
                _at = close < 0 ? s.Length : close + 2;
            }
            else
            {
                var end = s.IndexOf('\n', _at);
                _at = end < 0 ? s.Length : end;
            }

            return ContentNode.Leaf(Kinds.Comment, s[from.._at], Roles.Trivia);
        }

        /// <summary>A bracketed group and everything in it, read by <paramref name="mode"/>'s lexer.</summary>
        private ContentNode Group(string kind, string open, string close, Mode mode)
        {
            var parts = new List<ContentNode> { ContentNode.Leaf(Kinds.Token, open, Roles.Open) };
            _at += open.Length;

            _closers.Add(close);
            parts.AddRange(Items(mode));
            _closers.RemoveAt(_closers.Count - 1);

            // A group still open at the end is what somebody halfway through typing it has written.
            if (At(close))
            {
                parts.Add(ContentNode.Leaf(Kinds.Token, close, Roles.Close));
                _at += close.Length;
            }

            return ContentNode.Branch(kind, parts);
        }

        // ── Music ───────────────────────────────────────────────────────────

        private ContentNode Music()
        {
            switch (s[_at])
            {
                case '<': return Chord();
                case '|': return One(LilyPondKinds.BarCheck);
                case '~': return One(LilyPondKinds.Tie);
                case '(': return One(LilyPondKinds.SlurOpen);
                case ')': return One(LilyPondKinds.SlurClose);
                case '[': return One(LilyPondKinds.BeamOpen);
                case ']': return One(LilyPondKinds.BeamClose);
                case '-' or '^' or '_': return Directed();
                case '=': return Held(1, "an = with nothing to set");
            }

            if (!IsWordChar(s[_at])) return Held(1, $"nothing here reads '{s[_at]}'");

            var word = Run(IsWordChar);

            // A word with an = after it is a name being defined, whatever it would otherwise have been — a
            // variable can perfectly well be called a.
            if (Assigning()) return Assignment(ContentNode.Leaf(LilyPondKinds.Word, word), Mode.Music);

            return Event(word) ?? ContentNode.Leaf(LilyPondKinds.Word, word);
        }

        /// <summary>
        /// A chord: its notes between angle brackets, and the duration written straight after the closing
        /// one, which belongs to the chord rather than to its last note.
        /// </summary>
        private ContentNode Chord()
        {
            var parts = new List<ContentNode> { ContentNode.Leaf(Kinds.Token, "<", Roles.Open) };
            _at++;

            _closers.Add(">");
            foreach (var member in Items(Mode.Music))
                parts.Add(member.Kind == LilyPondKinds.Note ? member.As(LilyPondRoles.Note) : member);
            _closers.RemoveAt(_closers.Count - 1);

            if (_at >= s.Length || s[_at] != '>') return ContentNode.Branch(LilyPondKinds.Chord, parts);

            parts.Add(ContentNode.Leaf(Kinds.Token, ">", Roles.Close));
            _at++;

            var end = _at;
            while (end < s.Length && IsWordChar(s[end])) end++;

            var tail = new List<ContentNode>();
            var at = 0;
            var word = s[_at..end];
            if (word.Length > 0 && Tail(word, ref at, tail))
            {
                parts.AddRange(tail);
                _at = end;
            }

            return ContentNode.Branch(LilyPondKinds.Chord, parts);
        }

        /// <summary>
        /// A <c>-</c>, <c>^</c> or <c>_</c>: text or a mark put on the note before it in that direction, an
        /// articulation spelled as punctuation, or a fingering.
        /// </summary>
        private ContentNode Directed()
        {
            var c = s[_at];
            var next = _at + 1 < s.Length ? s[_at + 1] : '\0';

            if (next is '"' or '\\')
            {
                var direction = ContentNode.Leaf(Kinds.Token, s[_at..(_at + 1)], Roles.Name);
                _at++;

                var target = next == '"' ? QuotedText() : Backslash(Mode.Music);
                return ContentNode.Branch(LilyPondKinds.Script, [direction, target]);
            }

            if ((c == '-' && next is '.' or '>' or '-' or '_' or '!' or '^' or '+')
                || (c is '^' or '_' && next is '.' or '>' or '-' or '!' or '+' or '^' or '_'))
            {
                _at += 2;
                return ContentNode.Leaf(LilyPondKinds.Articulation, s[(_at - 2).._at]);
            }

            if (char.IsAsciiDigit(next))
            {
                var from = _at;
                _at++;
                while (_at < s.Length && char.IsAsciiDigit(s[_at])) _at++;
                return ContentNode.Leaf(LilyPondKinds.Articulation, s[from.._at]);
            }

            return Held(1, $"a '{c}' puts something on a note, and nothing follows it");
        }

        // ── Words ───────────────────────────────────────────────────────────

        /// <summary>A note, a rest or a repeated chord, or null where the word is none of them.</summary>
        private static ContentNode? Event(string word) => NoteOf(word) ?? RestOf(word);

        /// <summary>
        /// A note — its name, octave marks, a forced accidental, a duration and a tremolo, each a leaf — or
        /// null where the word is not one. Strict, so that <c>bass</c> or <c>default</c> stays a word.
        /// </summary>
        private static ContentNode? NoteOf(string word, bool durations = true)
        {
            if (word.Length == 0 || word[0] is < 'a' or > 'g') return null;

            var at = 1;
            while (at < word.Length && char.IsAsciiLetterLower(word[at])) at++;
            if (LilyPondTheory.Name(word[..at]) is null) return null;

            var parts = new List<ContentNode>(5)
            {
                ContentNode.Leaf(LilyPondKinds.NoteName, word[..at], LilyPondRoles.NoteName),
            };

            var from = at;
            while (at < word.Length && word[at] is '\'' or ',') at++;
            if (at > from) parts.Add(ContentNode.Leaf(LilyPondKinds.Octave, word[from..at], LilyPondRoles.Octave));

            from = at;
            while (at < word.Length && word[at] is '!' or '?') at++;
            if (at > from) parts.Add(ContentNode.Leaf(LilyPondKinds.Force, word[from..at], LilyPondRoles.Force));

            if (!durations) return at == word.Length ? ContentNode.Branch(LilyPondKinds.Note, parts) : null;
            return Tail(word, ref at, parts) ? ContentNode.Branch(LilyPondKinds.Note, parts) : null;
        }

        /// <summary>A rest — <c>r</c>, <c>R</c>, <c>s</c> — or a repeated chord <c>q</c>, with its duration.</summary>
        private static ContentNode? RestOf(string word)
        {
            if (word.Length == 0 || word[0] is not ('r' or 'R' or 's' or 'q')) return null;

            var parts = new List<ContentNode>(3) { ContentNode.Leaf(Kinds.Token, word[..1], Roles.Name) };
            var at = 1;

            return Tail(word, ref at, parts)
                ? ContentNode.Branch(word[0] == 'q' ? LilyPondKinds.ChordRepeat : LilyPondKinds.Rest, parts)
                : null;
        }

        /// <summary>
        /// A duration and a tremolo, as leaves, from where <paramref name="at"/> is to the end of the word —
        /// or false where what is left of the word is not those.
        /// </summary>
        private static bool Tail(string word, ref int at, List<ContentNode> parts)
        {
            var from = at;
            while (at < word.Length && (char.IsAsciiDigit(word[at]) || (at > from && word[at] is '.' or '*' or '/')))
                at++;

            if (at > from)
            {
                if (LilyPondTheory.Length(word[from..at]) is null) return false;
                parts.Add(ContentNode.Leaf(LilyPondKinds.Duration, word[from..at], LilyPondRoles.Duration));
            }

            if (at < word.Length && word[at] == ':')
            {
                from = at;
                at++;
                while (at < word.Length && char.IsAsciiDigit(word[at])) at++;
                parts.Add(ContentNode.Leaf(LilyPondKinds.Tremolo, word[from..at], LilyPondRoles.Tremolo));
            }

            return at == word.Length;
        }

        // ── Lyrics and chord names ──────────────────────────────────────────

        /// <summary>
        /// A syllable, or what joins two: <c>--</c> a hyphen, <c>__</c> a held syllable, <c>_</c> a note sung
        /// on nothing. Anything but space and a brace is part of a word here — "sky." is a word.
        /// </summary>
        private ContentNode Lyric()
        {
            var word = Run(c => !char.IsWhiteSpace(c) && c is not ('{' or '}' or '"' or '\\' or '%' or '#'));
            if (word.Length == 0) return Held(1, $"nothing here reads '{s[_at]}'");

            return word is "--" or "__" or "_"
                ? ContentNode.Leaf(LilyPondKinds.LyricMark, word, Roles.Separator)
                : ContentNode.Leaf(LilyPondKinds.Syllable, word);
        }

        /// <summary>
        /// A chord's name — <c>g1:7</c>, <c>d2:m7/f</c> — where a colon and a slash are part of it, or a rest where the
        /// chord line waits.
        /// </summary>
        private ContentNode ChordName()
        {
            if (s[_at] == '|') return One(LilyPondKinds.BarCheck);
            if (s[_at] == '~') return One(LilyPondKinds.Tie);

            var word = Run(c => !char.IsWhiteSpace(c)
                                && c is not ('{' or '}' or '<' or '>' or '|' or '"' or '\\' or '%' or '#' or '~'));
            if (word.Length == 0) return Held(1, $"nothing here reads '{s[_at]}'");

            return RestOf(word) ?? ChordOf(word) ?? ContentNode.Leaf(LilyPondKinds.Word, word);
        }

        /// <summary>
        /// A chord's name as its parts: the root, its octave marks, the duration, and what kind of chord it is —
        /// <c>:m7</c>, <c>/f</c> — each a leaf, so how long it lasts can be hung on it and what it is read off it.
        /// </summary>
        private static ContentNode? ChordOf(string word)
        {
            if (word.Length == 0 || word[0] is < 'a' or > 'g') return null;

            var at = 1;
            while (at < word.Length && char.IsAsciiLetterLower(word[at])) at++;
            if (LilyPondTheory.Name(word[..at]) is null) return null;

            var parts = new List<ContentNode>(4)
            {
                ContentNode.Leaf(LilyPondKinds.NoteName, word[..at], LilyPondRoles.NoteName),
            };

            var from = at;
            while (at < word.Length && word[at] is '\'' or ',') at++;
            if (at > from) parts.Add(ContentNode.Leaf(LilyPondKinds.Octave, word[from..at], LilyPondRoles.Octave));

            // A slash here is the bass, not a fraction, so a duration in a chord's name stops short of one.
            from = at;
            while (at < word.Length && (char.IsAsciiDigit(word[at]) || (at > from && word[at] is '.' or '*'))) at++;
            if (at > from)
            {
                if (LilyPondTheory.Length(word[from..at]) is null) return null;
                parts.Add(ContentNode.Leaf(LilyPondKinds.Duration, word[from..at], LilyPondRoles.Duration));
            }

            if (at < word.Length)
            {
                if (word[at] is not (':' or '/')) return null;
                parts.Add(ContentNode.Leaf(LilyPondKinds.Word, word[at..], LilyPondRoles.Quality));
            }

            return ContentNode.Branch(LilyPondKinds.ChordName, parts);
        }

        // ── Strings and Scheme ──────────────────────────────────────────────

        /// <summary>A quoted string — or, followed by an =, a name being defined in quotes.</summary>
        private ContentNode Quoted(Mode mode)
        {
            var text = QuotedText();
            return text.Kind == LilyPondKinds.Quoted && Assigning() ? Assignment(text, mode) : text;
        }

        /// <summary>
        /// Text in double quotes, the quotes kept. A quote with no partner on its line is held on its own, so
        /// that typing one does not turn the rest of the source into a string.
        /// </summary>
        private ContentNode QuotedText()
        {
            var from = _at;
            var at = _at + 1;

            while (at < s.Length && s[at] is not ('"' or '\n'))
                at += s[at] == '\\' && at + 1 < s.Length && s[at + 1] != '\n' ? 2 : 1;

            if (at >= s.Length || s[at] != '"') return Held(1, "this quotation is never closed");

            _at = at + 1;
            return ContentNode.Leaf(LilyPondKinds.Quoted, s[from.._at]);
        }

        /// <summary>
        /// Scheme, read as far as where it ends: a balanced <c>#( … )</c>, a <c>#" … "</c>, a
        /// <c>#{ … #}</c>, or an atom — <c>##t</c>, <c>#'left</c>, <c>#red</c> — which is the value of whatever
        /// it is set on, and so has to survive as a piece rather than vanish.
        /// </summary>
        private ContentNode Scheme()
        {
            var from = _at;
            _at++;

            if (_at < s.Length && s[_at] == '#') _at++;
            if (_at < s.Length && s[_at] is '\'' or '`') _at++;

            if (_at < s.Length && s[_at] == '(')
            {
                if (!Balanced())
                {
                    var end = s.IndexOf('\n', from);
                    _at = end < 0 ? s.Length : end;
                    return ContentNode.Shown(s[from.._at], "this ( is never closed");
                }
            }
            else if (_at < s.Length && s[_at] == '"')
            {
                var inner = QuotedText();
                if (inner.Kind != LilyPondKinds.Quoted) return ContentNode.Shown(s[from.._at], inner.Trouble);
            }
            else if (_at < s.Length && s[_at] == '{')
            {
                var close = s.IndexOf("#}", _at, StringComparison.Ordinal);
                _at = close < 0 ? s.Length : close + 2;
            }
            else
            {
                while (_at < s.Length && !char.IsWhiteSpace(s[_at]) && s[_at] is not ('{' or '}' or '(' or ')' or '"'))
                    _at++;
            }

            return ContentNode.Leaf(LilyPondKinds.Scheme, s[from.._at]);
        }

        /// <summary>Steps over a balanced parenthesis from here, strings and comments inside it included.</summary>
        private bool Balanced()
        {
            var depth = 0;

            for (var at = _at; at < s.Length; at++)
            {
                switch (s[at])
                {
                    case '"':
                        at++;
                        while (at < s.Length && s[at] != '"') at += s[at] == '\\' ? 2 : 1;
                        continue;

                    case ';':
                        while (at < s.Length && s[at] != '\n') at++;
                        continue;

                    case '(':
                        depth++;
                        continue;

                    case ')':
                        if (--depth > 0) continue;
                        _at = at + 1;
                        return true;
                }
            }

            return false;
        }

        // ── Commands ────────────────────────────────────────────────────────

        /// <summary>What a backslash begins: a command, a quoted variable, a phrasing slur, or two voices' divide.</summary>
        private ContentNode Backslash(Mode mode)
        {
            var from = _at;
            var next = _at + 1 < s.Length ? s[_at + 1] : '\0';

            if (next == '\\')
            {
                _at += 2;
                return ContentNode.Leaf(LilyPondKinds.VoiceSeparator, s[from.._at]);
            }

            if (next is '(' or ')')
            {
                _at += 2;
                return ContentNode.Leaf(next == '(' ? LilyPondKinds.SlurOpen : LilyPondKinds.SlurClose, s[from.._at]);
            }

            if (next == '"')
            {
                // A variable named in quotes — `\"voice1"` — which is how a name holding a digit is written.
                var close = _at + 2;
                while (close < s.Length && s[close] is not ('"' or '\n')) close++;
                if (close >= s.Length || s[close] != '"') return Held(2, "this quotation is never closed");

                _at = close + 1;
                return ContentNode.Branch(LilyPondKinds.Command, [ContentNode.Leaf(Kinds.Token, s[from.._at], Roles.Name)]);
            }

            if (char.IsAsciiLetter(next))
            {
                _at++;
                while (_at < s.Length && char.IsAsciiLetter(s[_at])) _at++;
                return Command(s[(from + 1).._at], mode, from);
            }

            if (next == '\0' || char.IsWhiteSpace(next)) return Held(1, @"a \ with no command after it");

            // \< \> \! and their kind: a command whose name is one character of punctuation.
            _at += 2;
            return ContentNode.Branch(LilyPondKinds.Command, [ContentNode.Leaf(Kinds.Token, s[from.._at], Roles.Name)]);
        }

        /// <summary>
        /// A command and the arguments LilyPond's grammar gives it. A command this does not know takes none,
        /// and whatever follows it is read as what it is — which is also what a variable's name does.
        /// </summary>
        private ContentNode Command(string name, Mode mode, int from)
        {
            var parts = new List<ContentNode> { ContentNode.Leaf(Kinds.Token, s[from.._at], Roles.Name) };

            switch (name)
            {
                case "relative":
                    Arg(parts, Pitch);
                    Arg(parts, () => MusicOf(mode));
                    break;

                case "fixed":
                    Arg(parts, Pitch);
                    Arg(parts, () => MusicOf(mode));
                    break;

                case "transpose":
                    Arg(parts, Pitch);
                    Arg(parts, Pitch);
                    Arg(parts, () => MusicOf(mode));
                    break;

                case "absolute" or "sequential" or "simultaneous" or "grace" or "acciaccatura" or "appoggiatura"
                    or "slashedGrace" or "alternative" or "once" or "temporary" or "undo" or "score" or "book"
                    or "bookpart" or "figuremode" or "figures" or "drummode" or "drums":
                    Arg(parts, () => MusicOf(mode));
                    break;

                case "afterGrace":
                    Arg(parts, () => Word(w => LilyPondTheory.Fraction(w) is not null));
                    Arg(parts, () => MusicOf(mode));
                    Arg(parts, () => MusicOf(mode));
                    break;

                case "new" or "context":
                    Arg(parts, () => QuotedArg() ?? Word(w => char.IsAsciiLetter(w[0])));
                    if (Arg(parts, Assign)) Arg(parts, () => QuotedArg() ?? Word(_ => true));
                    Arg(parts, With);
                    Arg(parts, () => MusicOf(mode));
                    break;

                case "with" or "header" or "paper" or "layout" or "midi":
                    Arg(parts, () => s[_at] == '{' ? Group(LilyPondKinds.Sequential, "{", "}", Mode.Music).As(Roles.Body) : null);
                    break;

                case "repeat":
                    Arg(parts, () => Word(w => char.IsAsciiLetter(w[0])));
                    Arg(parts, () => Word(w => w.All(char.IsAsciiDigit)));
                    Arg(parts, () => MusicOf(mode));
                    break;

                case "volta":
                    Arg(parts, () => Word(w => w.Split(',').All(number => number.Length > 0 && number.All(char.IsAsciiDigit))));
                    Arg(parts, () => MusicOf(mode));
                    break;

                case "tuplet":
                    Arg(parts, () => Word(w => LilyPondTheory.Fraction(w) is not null));
                    Arg(parts, () => Word(w => LilyPondTheory.Length(w) is not null));
                    Arg(parts, () => MusicOf(mode));
                    break;

                case "times":
                    Arg(parts, () => Word(w => LilyPondTheory.Fraction(w) is not null));
                    Arg(parts, () => MusicOf(mode));
                    break;

                case "time":
                    Arg(parts, () => Word(w => w.Contains(',') && w.All(c => char.IsAsciiDigit(c) || c == ',')));
                    Arg(parts, () => Word(w => LilyPondTheory.Fraction(w) is not null));
                    break;

                case "key":
                    Arg(parts, Pitch);
                    Arg(parts, KeyMode);
                    break;

                case "clef":
                    Arg(parts, () => QuotedArg() ?? Word(_ => true, c => char.IsAsciiLetterOrDigit(c) || c is '_' or '^'));
                    break;

                case "partial" or "skip":
                    Arg(parts, () => Word(w => LilyPondTheory.Length(w) is not null));
                    break;

                case "bar" or "version" or "include" or "language":
                    Arg(parts, QuotedArg);
                    break;

                case "tempo":
                    Arg(parts, () => QuotedArg() ?? (At(@"\markup") ? Backslash(Mode.Music).As(LilyPondRoles.Argument) : null));
                    Arg(parts, () => Word(w => LilyPondTheory.Length(w) is not null));
                    if (Arg(parts, Assign)) Arg(parts, () => Word(_ => true, c => char.IsAsciiDigit(c) || c == '-'));
                    break;

                case "mark" or "sectionLabel" or "markup" or "markuplist":
                    Arg(parts, Markup);
                    break;

                case "set" or "override":
                    Arg(parts, Path);
                    if (Arg(parts, Assign)) Arg(parts, Value);
                    break;

                case "unset" or "revert" or "omit" or "hide":
                    Arg(parts, Path);
                    break;

                case "tweak":
                    Arg(parts, Path);
                    Arg(parts, Value);
                    break;

                case "lyricmode" or "lyrics" or "addlyrics":
                    Arg(parts, () => MusicOf(Mode.Lyrics));
                    break;

                case "lyricsto":
                    Arg(parts, () => QuotedArg() ?? Word(_ => true));
                    Arg(parts, () => MusicOf(Mode.Lyrics));
                    break;

                case "chordmode" or "chords":
                    Arg(parts, () => MusicOf(Mode.Chords));
                    break;
            }

            return ContentNode.Branch(LilyPondKinds.Command, parts);
        }

        /// <summary>
        /// An argument, with the space before it, added to <paramref name="into"/> — or, where the next thing
        /// is not what the command takes, nothing read and nothing added, so it is read as what it is instead.
        /// </summary>
        private bool Arg(List<ContentNode> into, Func<ContentNode?> read)
        {
            var back = _at;
            var gap = new List<ContentNode>();
            while (Trivia() is { } trivia) gap.Add(trivia);

            if (_at < s.Length && !Closes() && read() is { } arg)
            {
                into.AddRange(gap);
                into.Add(arg);
                return true;
            }

            _at = back;
            return false;
        }

        /// <summary>The music a command applies to: a group, a chord, a note or rest, or another command.</summary>
        private ContentNode? MusicOf(Mode mode)
        {
            var c = s[_at];

            if (c is '{' or '\\' || At("<<")) return Item(mode).As(Roles.Body);
            if (mode != Mode.Music) return null;
            if (c == '<') return Chord().As(Roles.Body);

            return IsWordChar(c) ? Taken(IsWordChar, Event)?.As(Roles.Body) : null;
        }

        /// <summary>A pitch given to a command — <c>\relative c'</c>, <c>\key g</c> — which is not a note played.</summary>
        private ContentNode? Pitch() => Taken(IsWordChar, w => NoteOf(w, durations: false))?.As(LilyPondRoles.Argument);

        /// <summary>A word given to a command, where <paramref name="fits"/> says it is the right kind.</summary>
        private ContentNode? Word(Func<string, bool> fits, Func<char, bool>? takes = null) =>
            Taken(takes ?? IsWordChar, w => fits(w) ? ContentNode.Leaf(LilyPondKinds.Word, w, LilyPondRoles.Argument) : null);

        /// <summary>A quoted string given to a command.</summary>
        private ContentNode? QuotedArg() => s[_at] == '"' ? QuotedText().As(LilyPondRoles.Argument) : null;

        /// <summary>The <c>=</c> of <c>\new Voice = "melody"</c> or <c>\set … = …</c>.</summary>
        private ContentNode? Assign()
        {
            if (s[_at] != '=' || At("==")) return null;

            _at++;
            return ContentNode.Leaf(Kinds.Token, "=", LilyPondRoles.Assign);
        }

        /// <summary>A <c>\with { … }</c> block given to a context.</summary>
        private ContentNode? With() => At(@"\with") && !IsLetter(_at + 5) ? Backslash(Mode.Music).As(LilyPondRoles.Argument) : null;

        /// <summary>The mode a <c>\key</c> is in: <c>\major</c>, <c>\dorian</c>.</summary>
        private ContentNode? KeyMode()
        {
            if (s[_at] != '\\') return null;

            var end = _at + 1;
            while (end < s.Length && char.IsAsciiLetter(s[end])) end++;
            if (!Modes.Contains(s[(_at + 1)..end])) return null;

            var from = _at;
            _at = end;
            return ContentNode.Branch(LilyPondKinds.Command,
                [ContentNode.Leaf(Kinds.Token, s[from.._at], Roles.Name)], LilyPondRoles.Argument);
        }

        /// <summary>A property's path — <c>Staff.TimeSignature.break-visibility</c> — which may hold a hyphen.</summary>
        private ContentNode? Path() =>
            char.IsAsciiLetter(s[_at])
                ? Word(_ => true, c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')
                : null;

        /// <summary>What a property is set to: a string, Scheme, a word, a markup, or a block.</summary>
        private ContentNode? Value() => s[_at] switch
        {
            '"' => QuotedText().As(LilyPondRoles.Argument),
            '#' or '$' => Scheme().As(LilyPondRoles.Argument),
            '\\' => Backslash(Mode.Music).As(LilyPondRoles.Argument),
            '{' => Group(LilyPondKinds.Sequential, "{", "}", Mode.Music).As(LilyPondRoles.Argument),
            _ => IsWordChar(s[_at]) ? Word(_ => true) : null,
        };

        /// <summary>
        /// A markup: a string, a word, a braced list, or a markup command and what it applies to.
        /// </summary>
        private ContentNode? Markup() => s[_at] switch
        {
            '"' => QuotedText().As(LilyPondRoles.Argument),
            '{' => Group(LilyPondKinds.Sequential, "{", "}", Mode.Music).As(LilyPondRoles.Argument),
            '#' or '$' => Scheme().As(LilyPondRoles.Argument),
            '\\' => MarkupCommand().As(LilyPondRoles.Argument),
            _ => IsWordChar(s[_at]) ? Word(_ => true) : null,
        };

        /// <summary>
        /// A markup command — <c>\bold</c>, <c>\column</c>, <c>\fontsize #2</c> — with the Scheme it is given and
        /// the markup it applies to. Only a string, a brace or another command is taken as that markup, so
        /// <c>\mark \default</c> does not swallow the note after it.
        /// </summary>
        private ContentNode MarkupCommand()
        {
            if (!IsLetter(_at + 1)) return Backslash(Mode.Music);

            var from = _at;
            _at++;
            while (_at < s.Length && (char.IsAsciiLetter(s[_at]) || (s[_at] == '-' && IsLetter(_at + 1)))) _at++;

            var parts = new List<ContentNode> { ContentNode.Leaf(Kinds.Token, s[from.._at], Roles.Name) };
            if (s[(from + 1).._at] == "default") return ContentNode.Branch(LilyPondKinds.Command, parts);

            while (Arg(parts, () => s[_at] is '#' or '$' ? Scheme().As(LilyPondRoles.Argument) : null)) { }
            Arg(parts, () => s[_at] is '"' or '{' or '\\' ? Markup() : null);

            return ContentNode.Branch(LilyPondKinds.Command, parts);
        }

        // ── Definitions ─────────────────────────────────────────────────────

        /// <summary>Whether an <c>=</c> comes next, which makes what was just read a name being defined.</summary>
        private bool Assigning()
        {
            var at = _at;
            while (at < s.Length && char.IsWhiteSpace(s[at])) at++;
            return at < s.Length && s[at] == '=' && (at + 1 >= s.Length || s[at + 1] != '=');
        }

        /// <summary>A definition: the name, its <c>=</c>, and what it is set to.</summary>
        private ContentNode Assignment(ContentNode name, Mode mode)
        {
            var parts = new List<ContentNode> { name.As(Roles.Name) };
            while (Trivia() is { } trivia) parts.Add(trivia);

            parts.Add(ContentNode.Leaf(Kinds.Token, "=", LilyPondRoles.Assign));
            _at++;

            Arg(parts, () => Item(mode).As(LilyPondRoles.Value));
            return ContentNode.Branch(LilyPondKinds.Assignment, parts);
        }

        // ── Small helpers ───────────────────────────────────────────────────

        /// <summary>A word that is not punctuation to the music lexer.</summary>
        private static bool IsWordChar(char c) =>
            !char.IsWhiteSpace(c) && c is not ('{' or '}' or '<' or '>' or '=' or '|' or '"' or '\\' or '%' or '#'
                                               or '$' or '(' or ')' or '[' or ']' or '~' or '^' or '_' or '-');

        private bool IsLetter(int at) => at < s.Length && char.IsAsciiLetter(s[at]);

        private string Run(Func<char, bool> takes)
        {
            var from = _at;
            while (_at < s.Length && takes(s[_at])) _at++;
            return s[from.._at];
        }

        /// <summary>
        /// The word here, made into a node by <paramref name="make"/> — or, where it makes nothing, nothing
        /// read, so the word is left for whatever reads next.
        /// </summary>
        private ContentNode? Taken(Func<char, bool> takes, Func<string, ContentNode?> make)
        {
            var end = _at;
            while (end < s.Length && takes(s[end])) end++;
            if (end == _at || make(s[_at..end]) is not { } node) return null;

            _at = end;
            return node;
        }

        private ContentNode One(string kind)
        {
            _at++;
            return ContentNode.Leaf(kind, s[(_at - 1).._at]);
        }

        /// <summary>
        /// Characters nothing here reads, kept as written with the reason beside them. Never an exception and
        /// never dropped: an editor holds half-finished input all day.
        /// </summary>
        private ContentNode Held(int length, string trouble)
        {
            var text = s.Substring(_at, Math.Min(length, s.Length - _at));
            _at += text.Length;
            return ContentNode.Shown(text, trouble);
        }
    }
}
