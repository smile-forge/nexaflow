using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Music.Abc;

/// <summary>
/// Reads ABC notation into a tree that prints back exactly what was read.
///
/// <para>
/// <strong>The parser only ever copies.</strong> Every leaf's text appears in the source at the offset the
/// tree puts it at; nothing is synthesized, normalized or inserted. That is the invariant holding the
/// round trip up — <c>Print(Parse(s)) == s</c> alone would be satisfied by a parser that returned the
/// whole input as one verbatim leaf, and equally by one that quietly repaired what it read. The
/// temptation on meeting <c>[CEG</c> is to close the bracket, and a parser that does that round-trips
/// everything except the half-finished tunes an editor spends its whole life holding.
/// </para>
/// <para>
/// So it reads, and it does not interpret. It does not know what key a note is in, how long it lasts,
/// which accidental prints, where a bar ends or which notes beam together — those are facts about the
/// header, the meter and the neighbours, and each is a pipeline stage that hangs its answer underneath
/// the piece it is about. What is here is only the shape of what was typed.
/// </para>
/// <para>
/// Anything unreadable is <em>held</em>, not lost: it becomes a verbatim leaf rather than an exception.
/// The editor is then never in a state where the reader has typed something the tree cannot hold, which
/// is what half-finished input always is.
/// </para>
/// </summary>
public static class AbcParser
{
    /// <summary>The field letters ABC 2.1 defines. A line starting with any of them and a colon is a field.</summary>
    private const string FieldLetters = "ABCDFGHIKLMmNOPQRrSsTUVWwXZ+";

    /// <summary>The single characters that decorate the note after them. None is a note letter.</summary>
    private const string Decorations = ".~HLMOPSTuv";

    /// <summary>Reads a tune.</summary>
    public static ContentNode Parse(string source)
    {
        var lines = new List<ContentNode>();
        var at = 0;

        while (at < source.Length)
        {
            var end = at;
            while (end < source.Length && source[end] is not ('\n' or '\r')) end++;

            var body = source[at..end];

            var stop = end;
            if (stop < source.Length && source[stop] == '\r') stop++;
            if (stop < source.Length && source[stop] == '\n') stop++;
            var terminator = source[end..stop];

            lines.Add(Line(body, terminator));
            at = stop;
        }

        return ContentNode.Branch(AbcKinds.Tune, lines);
    }

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>
    /// One source line and the characters that ended it.
    /// <para>
    /// The terminator belongs to the line rather than to whatever comes next, because a line break in ABC
    /// is not whitespace: it suggests a system break, and in a lyric line it says which music line the
    /// syllables belong under. Owning it also means inserting or removing a whole line is one edit to one
    /// node rather than two edits either side of a character nobody owns.
    /// </para>
    /// </summary>
    private static ContentNode Line(string body, string terminator)
    {
        var pieces = new List<ContentNode>();
        var kind = AbcKinds.Line;

        if (IsField(body, out var letter))
        {
            kind = letter is 'w' ? AbcKinds.LyricLine : AbcKinds.Field;

            // The name is the letter and its colon: what makes it a field, and what an edit changing the
            // key replaces nothing of.
            pieces.Add(ContentNode.Leaf(Kinds.Token, body[..2], Roles.Name));

            var value = body[2..];
            var comment = CommentAt(value);
            if (comment < 0)
            {
                if (value.Length > 0) pieces.Add(ContentNode.Leaf(AbcKinds.Text, value, AbcRoles.Value));
            }
            else
            {
                if (comment > 0) pieces.Add(ContentNode.Leaf(AbcKinds.Text, value[..comment], AbcRoles.Value));
                pieces.Add(ContentNode.Leaf(Kinds.Comment, value[comment..], Roles.Trivia));
            }
        }
        else if (body.StartsWith('%'))
        {
            pieces.Add(ContentNode.Leaf(Kinds.Comment, body, Roles.Trivia));
        }
        else if (body.Trim().Length == 0)
        {
            if (body.Length > 0) pieces.Add(ContentNode.Leaf(Kinds.Space, body, Roles.Trivia));
        }
        else
        {
            Music(body, pieces);
        }

        if (terminator.Length > 0) pieces.Add(ContentNode.Leaf(Kinds.Space, terminator, Roles.Trivia));

        return ContentNode.Branch(kind, pieces);
    }

    /// <summary>Whether this line is an information field, and which one.</summary>
    private static bool IsField(string line, out char letter)
    {
        letter = '\0';
        if (line.Length < 2 || line[1] != ':') return false;
        if (!FieldLetters.Contains(line[0])) return false;

        letter = line[0];
        return true;
    }

    /// <summary>
    /// Where a trailing comment starts, or -1. A <c>%</c> ends a line unless it was escaped as
    /// <c>\%</c> — which is the only escape ABC has inside a field.
    /// </summary>
    private static int CommentAt(string text)
    {
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '%' && (i == 0 || text[i - 1] != '\\')) return i;

        return -1;
    }

    // ── A line of music ─────────────────────────────────────────────────────

    private static void Music(string s, List<ContentNode> into)
    {
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];

            if (char.IsWhiteSpace(c))
            {
                into.Add(ContentNode.Leaf(Kinds.Space, Run(s, ref i, char.IsWhiteSpace), Roles.Trivia));
                continue;
            }

            if (c == '%')
            {
                into.Add(ContentNode.Leaf(Kinds.Comment, s[i..], Roles.Trivia));
                return;
            }

            // A bar line, a repeat, a repeat bracket, an inline field or a chord — all of them start with
            // one of these four, and telling them apart needs the next character or two.
            if (c is '|' or ':' or '[' or ']')
            {
                if (Barline(s, ref i) is { } bar) { into.Add(bar); continue; }
                if (c == '[' && Bracketed(s, ref i) is { } bracketed) { into.Add(bracketed); continue; }

                into.Add(Held(s, ref i, $"there is nothing a '{c}' can mean here"));
                continue;
            }

            switch (c)
            {
                case '^':
                case '_':
                case '=':
                    // An accidental in front of nothing is not a note, and is held rather than dropped.
                    into.Add(Note(s, ref i));
                    continue;

                case '"':
                    into.Add(Quoted(s, ref i));
                    continue;

                case '!':
                    into.Add(Bang(s, ref i));
                    continue;

                case '{':
                    into.Add(Grace(s, ref i));
                    continue;

                case '(':
                    into.Add(OpenParen(s, ref i));
                    continue;

                case ')':
                    into.Add(One(s, ref i, AbcKinds.SlurClose));
                    continue;

                case '-':
                    into.Add(One(s, ref i, AbcKinds.Tie));
                    continue;

                case '>':
                case '<':
                    into.Add(ContentNode.Leaf(AbcKinds.Broken, Run(s, ref i, ch => ch == c)));
                    continue;

                case '&':
                    into.Add(One(s, ref i, AbcKinds.Overlay));
                    continue;

                case 'y':
                    into.Add(Spacer(s, ref i));
                    continue;

                case '\\':
                    into.Add(One(s, ref i, AbcKinds.Continuation, Roles.Trivia));
                    continue;
            }

            if (Decorations.Contains(c))
            {
                into.Add(One(s, ref i, AbcKinds.Decoration));
                continue;
            }

            if (c is 'z' or 'x' or 'Z')
            {
                into.Add(Rest(s, ref i));
                continue;
            }

            if (IsNoteLetter(c))
            {
                into.Add(Note(s, ref i));
                continue;
            }

            into.Add(Held(s, ref i, $"nothing here reads '{c}'"));
        }
    }

    /// <summary>Whether this letter names a note. Public because an editor asks it of every keystroke.</summary>
    public static bool IsNoteLetter(char c) => c is >= 'A' and <= 'G' or >= 'a' and <= 'g';

    // ── Notes, rests and chords ─────────────────────────────────────────────

    /// <summary>
    /// A note: an optional accidental, the letter, optional octave marks, an optional length.
    ///
    /// <para>
    /// Each is its own leaf, and that is what makes the note gestures cheap. Sharpening a note replaces
    /// its accidental leaf; moving it an octave replaces its marks and the letter's case; lengthening it
    /// replaces its length. Every one of those is an edit to a leaf of this node and nothing else, so a
    /// tune somebody lined up by hand still reads that way afterwards.
    /// </para>
    /// <para>
    /// An accidental written in front of something that is not a note letter still produces one of these,
    /// with no letter in it. That is what was typed, and half of it is what somebody is in the middle of
    /// typing.
    /// </para>
    /// </summary>
    private static ContentNode Note(string s, ref int i)
    {
        var pieces = new List<ContentNode>(4);

        if (s[i] is '^' or '_' or '=')
        {
            var mark = s[i];
            var text = mark == '=' ? One(s, ref i) : Run(s, ref i, ch => ch == mark);
            pieces.Add(ContentNode.Leaf(AbcKinds.Accidental, text, AbcRoles.Accidental));
        }

        if (i < s.Length && IsNoteLetter(s[i]))
        {
            pieces.Add(ContentNode.Leaf(AbcKinds.Letter, One(s, ref i), AbcRoles.Letter));

            var marks = Run(s, ref i, ch => ch is ',' or '\'');
            if (marks.Length > 0) pieces.Add(ContentNode.Leaf(AbcKinds.Octave, marks, AbcRoles.Octave));

            AddLength(s, ref i, pieces);
        }

        return ContentNode.Branch(AbcKinds.Note, pieces);
    }

    private static ContentNode Rest(string s, ref int i)
    {
        var pieces = new List<ContentNode>(2)
        {
            ContentNode.Leaf(Kinds.Token, One(s, ref i), Roles.Name),
        };

        AddLength(s, ref i, pieces);
        return ContentNode.Branch(AbcKinds.Rest, pieces);
    }

    /// <summary>
    /// An ABC length suffix, read as the characters it is: digits, then slashes, then digits. What it
    /// multiplies the unit note length by is a later stage's answer, because the unit note length is not
    /// written here.
    /// </summary>
    private static void AddLength(string s, ref int i, List<ContentNode> pieces)
    {
        var start = i;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        while (i < s.Length && s[i] == '/') i++;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;

        if (i > start) pieces.Add(ContentNode.Leaf(AbcKinds.Length, s[start..i], AbcRoles.Length));
    }

    /// <summary>
    /// What a <c>[</c> opens once it is not a bar line: an inline field, or a chord.
    /// Null when it opens neither, which leaves the caller to hold the bracket as written.
    /// </summary>
    private static ContentNode? Bracketed(string s, ref int i)
    {
        var close = s.IndexOf(']', i);
        if (close <= i) return null;

        // An inline field: [K:G], [M:3/4]. The letter and the colon are what say so.
        if (i + 2 < s.Length && char.IsLetter(s[i + 1]) && s[i + 2] == ':')
        {
            var pieces = new List<ContentNode>(4)
            {
                ContentNode.Leaf(Kinds.Token, s[i..(i + 1)], Roles.Open),
                ContentNode.Leaf(Kinds.Token, s[(i + 1)..(i + 3)], Roles.Name),
            };

            if (close > i + 3) pieces.Add(ContentNode.Leaf(AbcKinds.Text, s[(i + 3)..close], AbcRoles.Value));
            pieces.Add(ContentNode.Leaf(Kinds.Token, s[close..(close + 1)], Roles.Close));

            i = close + 1;
            return ContentNode.Branch(AbcKinds.InlineField, pieces);
        }

        // A chord. Its members are notes, read the same way as anywhere else, and the length after the
        // closing bracket belongs to the chord rather than to its last note.
        var members = new List<ContentNode>
        {
            ContentNode.Leaf(Kinds.Token, s[i..(i + 1)], Roles.Open),
        };

        var inner = i + 1;
        while (inner < close)
        {
            var c = s[inner];
            if (c is '^' or '_' or '=' || IsNoteLetter(c))
            {
                var scan = inner;
                var note = Note(s, ref scan);
                if (scan > inner) { members.Add(note.As(AbcRoles.Note)); inner = scan; continue; }
            }

            var held = inner;
            members.Add(Held(s, ref held, $"nothing here reads '{c}' inside a chord"));
            inner = held;
        }

        members.Add(ContentNode.Leaf(Kinds.Token, s[close..(close + 1)], Roles.Close));
        i = close + 1;

        AddLength(s, ref i, members);
        return ContentNode.Branch(AbcKinds.Chord, members);
    }

    /// <summary>Grace notes: <c>{gAG}</c>, or <c>{/g}</c> for a slashed acciaccatura.</summary>
    private static ContentNode Grace(string s, ref int i)
    {
        var close = s.IndexOf('}', i);
        if (close < 0) return Held(s, ref i, "this { is never closed");

        var pieces = new List<ContentNode>
        {
            ContentNode.Leaf(Kinds.Token, s[i..(i + 1)], Roles.Open),
        };

        var inner = i + 1;
        if (inner < close && s[inner] == '/')
        {
            pieces.Add(ContentNode.Leaf(Kinds.Token, s[inner..(inner + 1)], Roles.Name));
            inner++;
        }

        while (inner < close)
        {
            var c = s[inner];
            if (c is '^' or '_' or '=' || IsNoteLetter(c))
            {
                var scan = inner;
                var note = Note(s, ref scan);
                if (scan > inner) { pieces.Add(note.As(AbcRoles.Note)); inner = scan; continue; }
            }

            var held = inner;
            pieces.Add(Held(s, ref held, $"nothing here reads '{c}' among grace notes"));
            inner = held;
        }

        pieces.Add(ContentNode.Leaf(Kinds.Token, s[close..(close + 1)], Roles.Close));
        i = close + 1;
        return ContentNode.Branch(AbcKinds.Grace, pieces);
    }

    /// <summary>A <c>(</c> is a tuplet marker when digits follow it, and a slur otherwise.</summary>
    private static ContentNode OpenParen(string s, ref int i)
    {
        if (i + 1 >= s.Length || !char.IsAsciiDigit(s[i + 1])) return One(s, ref i, AbcKinds.SlurOpen);

        var start = i;
        i++;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;

        for (var colons = 0; colons < 2; colons++)
        {
            if (i >= s.Length || s[i] != ':') break;
            i++;
            while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        }

        return ContentNode.Leaf(AbcKinds.Tuplet, s[start..i]);
    }

    /// <summary>A <c>y</c> and its length: room on the page, and no time.</summary>
    private static ContentNode Spacer(string s, ref int i)
    {
        var pieces = new List<ContentNode>(2)
        {
            ContentNode.Leaf(Kinds.Token, One(s, ref i), Roles.Name),
        };

        AddLength(s, ref i, pieces);
        return ContentNode.Branch(AbcKinds.Spacer, pieces);
    }

    /// <summary>A double-quoted run: a chord symbol, or a text annotation placed by its first character.</summary>
    private static ContentNode Quoted(string s, ref int i)
    {
        var close = s.IndexOf('"', i + 1);
        if (close < 0) return Held(s, ref i, "this quotation is never closed");

        var text = s[i..(close + 1)];
        i = close + 1;
        return ContentNode.Leaf(AbcKinds.Annotation, text);
    }

    /// <summary>A named decoration: <c>!trill!</c>, <c>!fermata!</c>.</summary>
    private static ContentNode Bang(string s, ref int i)
    {
        var close = s.IndexOf('!', i + 1);
        if (close < 0) return Held(s, ref i, "this ! is never closed");

        var text = s[i..(close + 1)];
        i = close + 1;
        return ContentNode.Leaf(AbcKinds.Decoration, text);
    }

    // ── Bar lines ───────────────────────────────────────────────────────────

    /// <summary>
    /// A bar line and the repeat bracket that may follow it. Null when what is here is not one — a
    /// <c>[</c> opening a chord, a lone <c>:</c>, a stray <c>]</c>.
    ///
    /// <para>
    /// A bar line carries a span like anything else somebody typed, which is what lets a reader point at
    /// one and what lets a whole bar be selected: a piece of layout drawn from nothing cannot be part of
    /// a selection, and a selection of every note in a bar has to cover the line that closes it before it
    /// can grow into the bar.
    /// </para>
    /// </summary>
    private static ContentNode? Barline(string s, ref int i)
    {
        var start = i;
        var c = s[i];
        var next = i + 1 < s.Length ? s[i + 1] : '\0';

        switch (c)
        {
            case ':' when next == '|':
                i += 2;
                while (i < s.Length && s[i] == '|') i++;
                if (i < s.Length && s[i] == ':') i++;
                break;

            case ':' when next == ':':
                i += 2;
                break;

            case ':':
                return null;

            case '|' when next == ':':
                i += 2;
                while (i < s.Length && s[i] == ':') i++;
                break;

            case '|' when next is ']' or '|':
                i += 2;
                break;

            case '|':
                i++;
                break;

            case '[' when next == '|':
                i += 2;
                break;

            case '[' when char.IsAsciiDigit(next):
                i++;                    // '[1' — a bracket with no line before it
                break;

            default:
                return null;
        }

        var line = start == i ? null : ContentNode.Leaf(AbcKinds.Barline, s[start..i], Roles.Separator);

        var volta = start;
        volta = i;
        while (volta < s.Length && (char.IsAsciiDigit(s[volta]) || s[volta] is ',' or '-')) volta++;

        if (volta == i) return line;

        var label = ContentNode.Leaf(AbcKinds.Volta, s[i..volta]);
        i = volta;

        return line is null ? label : ContentNode.Branch(AbcKinds.Barline, [line.As(Roles.Name), label]);
    }

    // ── Small helpers ───────────────────────────────────────────────────────

    private static string One(string s, ref int i) => s[i..++i];

    private static ContentNode One(string s, ref int i, string kind, string role = Roles.Element) =>
        ContentNode.Leaf(kind, One(s, ref i), role);

    private static string Run(string s, ref int i, Func<char, bool> takes)
    {
        var start = i;
        while (i < s.Length && takes(s[i])) i++;
        return s[start..i];
    }

    /// <summary>
    /// One character nothing here reads, kept as written with the reason beside it. Never an exception
    /// and never dropped: an editor holds half-finished input all day, and a tree that could not hold it
    /// would be empty every other keystroke.
    /// </summary>
    private static ContentNode Held(string s, ref int i, string trouble) =>
        ContentNode.Shown(One(s, ref i), trouble);
}
