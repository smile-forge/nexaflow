using System.Globalization;
using System.Text;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The tokens a run of words is written in, read where they are asked for: CommonMark's inline grammar, with the extensions
/// the reading enables — the extra emphasis marks, citations, formulas between dollars, addresses written bare.
///
/// <para>
/// <strong>A token is what the characters at one place say, and nothing else.</strong> Whether a <c>]</c> closes a link,
/// whether a run of asterisks opens emphasis — whether an address written bare is a link at all, inside a link's words — is
/// for whoever matches them to say (<see cref="MarkdownDelimiters"/>). So a token is the same token whichever way the
/// matching goes, and the matching can ask for the token at any place it reaches, as often as it likes.
/// </para>
/// <para>
/// The words are lines, each from where what it holds starts — past its containers' marks — to where it ends, with a line
/// ending between each two. A place is where a character stands in the text; the end of a line stands for its line ending.
/// </para>
/// </summary>
internal sealed class MarkdownTokenizer
{
    private readonly string _text;
    private readonly IReadOnlyList<(int From, int To)> _lines;

    /// <summary>The line the last place asked about stands on, which is where the next nearly always does.</summary>
    private int _line;

    /// <summary>For each length of a run of backticks, the place from which no run as long stands to close it.</summary>
    private Dictionary<int, int>? _unclosed;

    public MarkdownTokenizer(string text, IReadOnlyList<(int From, int To)> lines)
    {
        _text = text;
        _lines = lines;
    }

    public string Text => _text;

    public IReadOnlyList<(int From, int To)> Lines => _lines;

    /// <summary>Where the words start.</summary>
    public int Start => _lines[0].From;

    /// <summary>Where they end.</summary>
    public int End => _lines[^1].To;

    // ── Places ───────────────────────────────────────────────────────────────

    /// <summary>Which line <paramref name="at"/> stands on: the last that starts at or before it.</summary>
    public int LineOf(int at)
    {
        var line = _line;

        if (_lines[line].From > at || (line + 1 < _lines.Count && _lines[line + 1].From <= at))
        {
            var low = 0;
            var high = _lines.Count - 1;

            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                if (_lines[middle].From <= at) low = middle;
                else high = middle - 1;
            }

            line = low;
        }

        return _line = line;
    }

    /// <summary>The character at <paramref name="at"/>: a line's own, a line ending at the end of a line, nothing past the last.</summary>
    public char CharAt(int at)
    {
        var line = this.LineOf(at);
        return at < _lines[line].To ? _text[at] : line + 1 < _lines.Count ? '\n' : '\0';
    }

    /// <summary>The character before <paramref name="at"/>: a line ending where a line starts, nothing before the first.</summary>
    public char Before(int at)
    {
        var line = this.LineOf(at);
        return at > _lines[line].From ? _text[at - 1] : line == 0 ? '\0' : '\n';
    }

    /// <summary>The place after <paramref name="at"/>, on to the next line past a line ending.</summary>
    public int After(int at)
    {
        var line = this.LineOf(at);
        if (at < _lines[line].To) return at + 1;

        return line + 1 < _lines.Count ? _lines[line + 1].From : at;
    }

    /// <summary>Where the line ending closing line <paramref name="line"/> ends.</summary>
    public int Ending(int line)
    {
        var at = _lines[line].To;

        if (at < _text.Length && _text[at] == '\r') at++;
        if (at < _text.Length && _text[at] == '\n') at++;

        return at;
    }

    /// <summary>
    /// Where the words go on from <paramref name="at"/>: where it stands, or — past a line ending, among the marks a container puts
    /// in front of the next line — where that line's own characters start.
    /// </summary>
    public int Resumed(int at)
    {
        var line = this.LineOf(at);

        return line + 1 < _lines.Count && at >= _lines[line].To && at >= this.Ending(line) ? _lines[line + 1].From : at;
    }

    private Rune RuneBefore(int at)
    {
        var line = this.LineOf(at);
        if (at <= _lines[line].From) return new Rune(line == 0 ? '\0' : '\n');

        return Rune.DecodeLastFromUtf16(_text.AsSpan(_lines[line].From, at - _lines[line].From), out var rune, out _) == System.Buffers.OperationStatus.Done
            ? rune
            : new Rune(_text[at - 1] is var unit && char.IsSurrogate(unit) ? '\uFFFD' : unit);
    }

    private Rune RuneAt(int at)
    {
        var line = this.LineOf(at);
        if (at >= _lines[line].To) return new Rune(line + 1 < _lines.Count ? '\n' : '\0');

        return Rune.DecodeFromUtf16(_text.AsSpan(at, _lines[line].To - at), out var rune, out _) == System.Buffers.OperationStatus.Done
            ? rune
            : new Rune('\uFFFD');
    }

    // ── Tokens ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The token at <paramref name="at"/> — with no address written bare starting there, where <paramref name="plain"/> says
    /// one was found and turned out to be none.
    /// </summary>
    public MarkdownToken Token(int at, bool plain = false)
    {
        var line = this.LineOf(at);
        var (from, to) = _lines[line];

        if (at >= to) return this.Ending(line, at);

        var character = _text[at];

        if (character is ' ' or '\t' && (at == from || this.Trailing(at, to))) return this.Spaced(at, to, plain);

        var token = character switch
        {
            '\\' => this.Escaped(at, line),
            '`' => this.Code(at),
            '&' => MarkdownEntities.Length(_text.AsSpan(0, to), at) is > 0 and var length
                ? new MarkdownToken(MarkdownTokenKind.Entity, at, at + length)
                : (MarkdownToken?)null,
            '<' => this.Autolink(at, to) ?? this.Html(at),
            '[' => new MarkdownToken(MarkdownTokenKind.Open, at, at + 1),
            ']' => new MarkdownToken(MarkdownTokenKind.Close, at, at + 1),
            '!' when at + 1 < to && _text[at + 1] == '[' => new MarkdownToken(MarkdownTokenKind.Picture, at, at + 2),
            '$' => this.Formula(at, to),
            '*' or '_' or '~' or '^' or '+' or '=' or '"' => this.Run(at, to),
            'h' or 'f' or 'm' or 't' or 'w' when !plain => this.Bare(at, to),
            _ => null,
        };

        return token is { Kind: not MarkdownTokenKind.Text } found ? found : this.Words(at, token?.End ?? at + 1, to, plain);
    }

    /// <summary>
    /// Characters that are only themselves, from <paramref name="at"/> — where nothing could be read, as far as
    /// <paramref name="past"/> at least — up to the next character that could start something.
    /// </summary>
    private MarkdownToken Words(int at, int past, int to, bool plain)
    {
        var next = past;

        while (next < to)
        {
            var character = _text[next];

            if (character is ' ' or '\t')
            {
                var run = next;
                while (run < to && _text[run] is ' ' or '\t') run++;

                if (run == to) break;

                next = run;
                continue;
            }

            if (character < 128 ? Stops[character] : false) break;

            if (character is 'h' or 'f' or 'm' or 't' or 'w' && Bares(_text[next - 1]) && Prefixed(_text.AsSpan(next, to - next))) break;

            next++;
        }

        return new MarkdownToken(MarkdownTokenKind.Text, at, next, Plain: plain);
    }

    /// <summary>Which characters can start something other than words.</summary>
    private static readonly bool[] Stops = Stopping("\\`&<[]!$*_~^+=\"");

    private static bool[] Stopping(string characters)
    {
        var stops = new bool[128];
        foreach (var character in characters) stops[character] = true;
        return stops;
    }

    /// <summary>Whether space from <paramref name="at"/> runs on to the end of its line.</summary>
    private bool Trailing(int at, int to)
    {
        while (at < to && _text[at] is ' ' or '\t') at++;
        return at == to;
    }

    /// <summary>Space at the head of a line, or at its end — every character of it, to the first that is not space.</summary>
    private MarkdownToken Spaced(int at, int to, bool plain)
    {
        var end = at;
        while (end < to && _text[end] is ' ' or '\t') end++;

        return new MarkdownToken(MarkdownTokenKind.Space, at, end, Plain: plain);
    }

    /// <summary>The line ending at the end of a line, and whether two spaces before it asked for it to be kept.</summary>
    private MarkdownToken Ending(int line, int at)
    {
        var (from, to) = _lines[line];
        var hard = to - 2 >= from && _text[to - 1] == ' ' && _text[to - 2] == ' ';

        return new MarkdownToken(hard ? MarkdownTokenKind.HardBreak : MarkdownTokenKind.Break, at, this.Ending(line));
    }

    /// <summary>A backslash: taking the meaning off the punctuation after it, or — the last character on a line — keeping the line ending.</summary>
    private MarkdownToken? Escaped(int at, int line)
    {
        var to = _lines[line].To;

        if (at + 1 == to && line + 1 < _lines.Count) return new MarkdownToken(MarkdownTokenKind.HardBreak, at, this.Ending(line));
        if (at + 1 < to && MarkdownLinkSyntax.Escapable(_text[at + 1])) return new MarkdownToken(MarkdownTokenKind.Escape, at, at + 2);

        return null;
    }

    // ── Code ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A code span: a run of backticks, and the next run exactly as long closing it, on whichever line it stands. A run
    /// nothing closes is the backticks it is.
    /// </summary>
    private MarkdownToken? Code(int at)
    {
        var to = _lines[_line].To;
        var opened = at;
        while (opened < to && _text[opened] == '`') opened++;

        var length = opened - at;

        if (_unclosed is not null && _unclosed.TryGetValue(length, out var past) && opened >= past)
            return new MarkdownToken(MarkdownTokenKind.Text, at, opened);

        for (var line = _line; line < _lines.Count; line++)
        {
            var (from, end) = _lines[line];

            for (var next = line == _line ? opened : from; next < end; next++)
            {
                if (_text[next] != '`') continue;

                var run = next;
                while (run < end && _text[run] == '`') run++;

                if (run - next == length) return new MarkdownToken(MarkdownTokenKind.Code, at, run, opened, next);

                next = run - 1;
            }
        }

        (_unclosed ??= [])[length] = opened;

        return new MarkdownToken(MarkdownTokenKind.Text, at, opened);
    }

    // ── Angle brackets ───────────────────────────────────────────────────────

    /// <summary><c>&lt;scheme:address&gt;</c> or <c>&lt;name@domain&gt;</c>, on one line.</summary>
    private MarkdownToken? Autolink(int at, int to)
    {
        var next = at + 1;

        // A scheme: a letter, then letters, digits, plus, full stop or hyphen — two to thirty-two of them in all — and a colon.
        var scheme = next;
        while (next < to && (char.IsAsciiLetterOrDigit(_text[next]) || _text[next] is '+' or '.' or '-')) next++;

        if (next - scheme is >= 2 and <= 32 && char.IsAsciiLetter(_text[scheme]) && next < to && _text[next] == ':')
        {
            for (next++; next < to; next++)
            {
                var character = _text[next];
                if (character == '>') return new MarkdownToken(MarkdownTokenKind.Autolink, at, next + 1, at + 1, next);
                if (character is '<' or ' ' || char.IsControl(character)) break;
            }

            return null;
        }

        // An address: what may stand before an @, and a domain of labels parted by full stops.
        next = at + 1;
        while (next < to && (char.IsAsciiLetterOrDigit(_text[next]) || ".!#$%&'*+/=?^_`{|}~-".Contains(_text[next]))) next++;

        if (next == at + 1 || next >= to || _text[next] != '@') return null;

        while (true)
        {
            var label = ++next;
            while (next < to && (char.IsAsciiLetterOrDigit(_text[next]) || _text[next] == '-') && next - label < 63) next++;

            if (next == label || _text[label] == '-' || _text[next - 1] == '-') return null;
            if (next >= to) return null;
            if (_text[next] == '>') return new MarkdownToken(MarkdownTokenKind.Autolink, at, next + 1, at + 1, next);
            if (_text[next] != '.') return null;
        }
    }

    /// <summary>Raw HTML, as CommonMark reads it — a tag, a comment, a declaration — over as many lines as it takes.</summary>
    private MarkdownToken? Html(int at)
    {
        var end = this.Tag(this.After(at));
        this.LineOf(at);

        return end > at ? new MarkdownToken(MarkdownTokenKind.Html, at, end) : null;
    }

    /// <summary>Where raw HTML whose <c>&lt;</c> stands just before <paramref name="at"/> ends, or -1 where none is written.</summary>
    private int Tag(int at)
    {
        var character = this.CharAt(at);

        switch (character)
        {
            case '/':
            {
                var next = this.Named(this.After(at));
                if (next < 0) return -1;

                next = this.Spaces(next, out _);
                return this.CharAt(next) == '>' ? this.After(next) : -1;
            }

            case '?':
                return this.Until(this.After(at), "?>");

            case '!':
            {
                var next = this.After(at);

                if (this.Says(next, "--"))
                {
                    next = this.After(this.After(next));
                    if (this.CharAt(next) == '>') return this.After(next);
                    if (this.Says(next, "->")) return this.After(this.After(next));

                    return this.Until(next, "-->");
                }

                if (this.Says(next, "[CDATA[")) return this.Until(this.Past(next, 7), "]]>");

                return char.IsAsciiLetter(this.CharAt(next)) ? this.Until(next, ">") : -1;
            }
        }

        var after = this.Named(at);
        if (after < 0) return -1;

        // Attributes, each with space in front of it.
        while (true)
        {
            var spaced = this.Spaces(after, out var any);
            var name = spaced;

            if (!any || !(char.IsAsciiLetter(this.CharAt(name)) || this.CharAt(name) is '_' or ':'))
            {
                after = spaced;
                break;
            }

            var next = this.After(name);
            while (char.IsAsciiLetterOrDigit(this.CharAt(next)) || this.CharAt(next) is '_' or '.' or ':' or '-') next = this.After(next);

            after = next;

            var valued = this.Spaces(next, out _);
            if (this.CharAt(valued) != '=') continue;

            var value = this.Spaces(this.After(valued), out _);
            var quote = this.CharAt(value);

            if (quote is '"' or '\'')
            {
                var shut = this.After(value);
                while (this.CharAt(shut) != quote)
                {
                    if (this.CharAt(shut) == '\0') return -1;
                    shut = this.After(shut);
                }

                after = this.After(shut);
                continue;
            }

            var unquoted = value;
            while (this.CharAt(unquoted) is not ('\0' or ' ' or '\t' or '\n' or '"' or '\'' or '=' or '<' or '>' or '`')) unquoted = this.After(unquoted);

            if (unquoted == value) return -1;
            after = unquoted;
        }

        if (this.CharAt(after) == '/') after = this.After(after);

        return this.CharAt(after) == '>' ? this.After(after) : -1;
    }

    /// <summary>Past a tag's name — a letter, then letters, digits and hyphens — or -1 where none starts here.</summary>
    private int Named(int at)
    {
        if (!char.IsAsciiLetter(this.CharAt(at))) return -1;

        var next = this.After(at);
        while (char.IsAsciiLetterOrDigit(this.CharAt(next)) || this.CharAt(next) == '-') next = this.After(next);

        return next;
    }

    /// <summary>Past spaces, tabs and at most one line ending; whether there were any.</summary>
    private int Spaces(int at, out bool any)
    {
        var next = at;
        var endings = 0;

        while (this.CharAt(next) is ' ' or '\t' or '\n' && (this.CharAt(next) != '\n' || endings++ == 0)) next = this.After(next);

        any = next != at;
        return next;
    }

    /// <summary>Whether <paramref name="what"/> is written from <paramref name="at"/> on.</summary>
    private bool Says(int at, string what)
    {
        foreach (var character in what)
        {
            if (this.CharAt(at) != character) return false;
            at = this.After(at);
        }

        return true;
    }

    private int Past(int at, int count)
    {
        for (var step = 0; step < count; step++) at = this.After(at);
        return at;
    }

    /// <summary>Past the first <paramref name="what"/> from <paramref name="at"/> on, or -1 where none is written.</summary>
    private int Until(int at, string what)
    {
        for (var next = at; this.CharAt(next) != '\0'; next = this.After(next))
            if (this.Says(next, what)) return this.Past(next, what.Length);

        return -1;
    }

    // ── Formulas ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A formula between one dollar or two each side, on one line — as Markdig reads one: the opening dollars after space or
    /// punctuation, the closing ones before it, and space inside either both ends or neither. What it holds starts past the
    /// space after the opening dollars and ends before the space before the closing ones.
    /// </summary>
    private MarkdownToken? Formula(int at, int to)
    {
        var before = this.Before(at);
        if (before == '$') return null;

        var opened = at + 1 < to && _text[at + 1] == '$' ? 2 : 1;
        var next = at + opened;
        var character = next < to ? _text[next] : '\0';

        Classified(before, out var beforeSpace, out var beforePunctuation);
        Classified(character, out var afterSpace, out _);

        if (!beforeSpace && !beforePunctuation) return null;

        while (character is ' ' or '\t')
        {
            next++;
            character = next < to ? _text[next] : '\0';
        }

        var start = next;
        var previous = '$';
        var space = -1;
        var closed = 0;

        while (character != '\0')
        {
            if (previous != '\\')
            {
                if (character is ' ' or '\t')
                {
                    if (space < 0) space = next;
                }
                else
                {
                    var dollars = character == '$';

                    if (dollars)
                    {
                        while (next < to && _text[next] == '$')
                        {
                            closed++;
                            next++;
                        }

                        character = next < to ? _text[next] : '\0';
                    }

                    if (closed >= opened) break;

                    space = -1;

                    if (dollars)
                    {
                        previous = '$';
                        continue;
                    }
                }
            }

            if (closed > 0)
            {
                closed = 0;
            }
            else
            {
                previous = character;
                next++;
                character = next < to ? _text[next] : '\0';
            }
        }

        // A formula is on one line: one its line ends inside is none.
        if (closed < opened) return null;

        Classified(previous, out var shutSpace, out _);
        Classified(character, out var nextSpace, out var nextPunctuation);

        if ((!nextPunctuation && !nextSpace) || afterSpace != shutSpace) return null;

        var last = shutSpace && space >= 0 ? space + opened - 1 : next - 1;

        return new MarkdownToken(MarkdownTokenKind.Formula, at, next, start, last - opened + 1);
    }

    // ── Emphasis ─────────────────────────────────────────────────────────────

    /// <summary>How many of a character a run of it needs and can use, and whether one inside a word counts.</summary>
    private readonly record struct Emphasis(int Least, int Most, bool WithinWords);

    /// <summary>Each character emphasis is written with: CommonMark's two, the extras, and the citation's quotes.</summary>
    public static int Least(char mark) => Emphasised(mark).Least;

    public static int Most(char mark) => Emphasised(mark).Most;

    private static Emphasis Emphasised(char mark) => mark switch
    {
        '*' => new Emphasis(1, 2, true),
        '_' => new Emphasis(1, 2, false),
        '~' => new Emphasis(1, 2, true),
        '^' => new Emphasis(1, 1, true),
        '+' => new Emphasis(2, 2, true),
        '=' => new Emphasis(2, 2, true),
        _ => new Emphasis(2, 2, false),
    };

    /// <summary>
    /// A run of one emphasis character, and whether it can open or close — CommonMark's flanking, with the characters on
    /// either side as Markdig sees them: an entity next to the run is what it stands for.
    /// </summary>
    private MarkdownToken? Run(int at, int to)
    {
        var mark = _text[at];
        var emphasis = Emphasised(mark);

        var end = at;
        while (end < to && _text[end] == mark) end++;

        // A run's characters left over from one that could not be used are those characters.
        if (this.Before(at) == mark && (at - 2 < _lines[_line].From || _text[at - 2] != '\\')) return new MarkdownToken(MarkdownTokenKind.Text, at, end);
        if (end - at < emphasis.Least) return new MarkdownToken(MarkdownTokenKind.Text, at, end);

        var before = this.EntityBefore(at) ?? this.RuneBefore(at);
        var after = this.EntityAfter(end, to) ?? this.RuneAt(end);

        Classified(before, out var beforeSpace, out var beforePunctuation);
        Classified(after, out var afterSpace, out var afterPunctuation);

        var beforeExcepted = beforePunctuation && Excepted(before);
        var afterExcepted = afterPunctuation && Excepted(after);

        var opens = !afterSpace && (!afterPunctuation || afterExcepted || beforeSpace || beforePunctuation);
        var closes = !beforeSpace && (!beforePunctuation || beforeExcepted || afterSpace || afterPunctuation);

        if (!emphasis.WithinWords)
        {
            var left = opens;
            opens = opens && (!closes || beforePunctuation);
            closes = closes && (!left || afterPunctuation);
        }

        return opens || closes
            ? new MarkdownToken(MarkdownTokenKind.Run, at, end, Mark: mark, Opens: opens, Closes: closes)
            : new MarkdownToken(MarkdownTokenKind.Text, at, end);
    }

    /// <summary>What an entity ending just before <paramref name="at"/> stands for, last character first — or null where none does.</summary>
    private Rune? EntityBefore(int at)
    {
        var from = _lines[this.LineOf(at)].From;
        if (at - 1 <= from || _text[at - 1] != ';') return null;

        var amp = _text.LastIndexOf('&', at - 1, Math.Min(at - from, 40));
        if (amp < from || MarkdownEntities.Length(_text.AsSpan(0, at), amp) != at - amp) return null;

        var decoded = MarkdownEntities.Decoded(_text.AsSpan(amp, at - amp));
        return decoded is { Length: > 0 } && Rune.DecodeLastFromUtf16(decoded, out var rune, out _) == System.Buffers.OperationStatus.Done ? rune : null;
    }

    /// <summary>What an entity starting at <paramref name="at"/> stands for, first character first — or null where none does.</summary>
    private Rune? EntityAfter(int at, int to)
    {
        if (at >= to || _text[at] != '&') return null;

        var length = MarkdownEntities.Length(_text.AsSpan(0, to), at);
        if (length == 0) return null;

        var decoded = MarkdownEntities.Decoded(_text.AsSpan(at, length));
        return decoded is { Length: > 0 } && Rune.DecodeFromUtf16(decoded, out var rune, out _) == System.Buffers.OperationStatus.Done ? rune : null;
    }

    /// <summary>
    /// Whether a character is space and whether it is punctuation, as CommonMark has them — Unicode's space and its
    /// punctuation and symbols — with nothing at all, before the words or after, counted as both.
    /// </summary>
    private static void Classified(Rune rune, out bool space, out bool punctuation) =>
        Classified(rune.Value, rune.Value <= 127 ? UnicodeCategory.OtherNotAssigned : Rune.GetUnicodeCategory(rune), out space, out punctuation);

    /// <summary>The same, of one character on its own — half of a pair counting as neither.</summary>
    private static void Classified(char character, out bool space, out bool punctuation) =>
        Classified(character, character <= 127 ? UnicodeCategory.OtherNotAssigned : char.GetUnicodeCategory(character), out space, out punctuation);

    private static void Classified(int value, UnicodeCategory category, out bool space, out bool punctuation)
    {
        if (value is '\t' or '\n' or '\f' or '\r' or ' ' || (value >= 0xA0 && category == UnicodeCategory.SpaceSeparator))
        {
            space = true;
            punctuation = false;
            return;
        }

        if (value <= 127)
        {
            space = value == 0;
            punctuation = value == 0 || (value is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~');
            return;
        }

        space = false;
        punctuation = category is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol
            or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol;
    }

    /// <summary>The punctuation Markdig lets a run open or close against as if it were a letter: minus signs, plus, daggers.</summary>
    private static bool Excepted(Rune rune) => rune.Value is '−' or '-' or '+' or '†' or '‡';

    // ── Addresses written bare ───────────────────────────────────────────────

    /// <summary>The characters an address written bare may follow besides space: what opens emphasis round it, or a parenthesis.</summary>
    private static bool Bares(char before) => before is '\0' or ' ' or '\t' or '\n' or '\r' or '\f' or '*' or '_' or '~' or '('
                                              || (before >= 0xA0 && char.GetUnicodeCategory(before) == UnicodeCategory.SpaceSeparator);

    /// <summary>Whether what is written here starts the way an address written bare does.</summary>
    private static bool Prefixed(ReadOnlySpan<char> written) =>
        written.Length >= 4 && written[0] switch
        {
            'h' => written.StartsWith("https://", StringComparison.Ordinal) || written.StartsWith("http://", StringComparison.Ordinal),
            'w' => written.StartsWith("www.", StringComparison.Ordinal),
            'f' => written.StartsWith("ftp://", StringComparison.Ordinal),
            'm' => written.StartsWith("mailto:", StringComparison.Ordinal),
            _ => written.StartsWith("tel:", StringComparison.Ordinal),
        };

    /// <summary>
    /// An address written bare — <c>https://</c>, <c>www.</c>, <c>ftp://</c>, <c>mailto:</c>, <c>tel:</c> — as far as Markdig
    /// reads one: to space, a <c>&lt;</c>, an entity, or a parenthesis it does not open, and short of the punctuation that ends
    /// a sentence. Whether it is a link is not settled here (<see cref="Bared"/>).
    /// </summary>
    private MarkdownToken? Bare(int at, int to)
    {
        if (!Bares(this.Before(at)) || !Prefixed(_text.AsSpan(at, to - at))) return null;

        var next = at;
        var depth = 0;

        while (true)
        {
            var character = next < to ? _text[next] : '\n';

            if (character == '(') depth++;

            if (character == ')' && --depth < 0) break;

            if (Ends(character)) break;
            if (character == '&' && Entity(_text.AsSpan(next, to - next))) break;
            if (character is '?' or '!' or '.' or ',' or ':' or '*' or '_' or '~' && Ends(next + 1 < to ? _text[next + 1] : '\n')) break;

            next++;
        }

        return depth > 0 ? null : new MarkdownToken(MarkdownTokenKind.Bare, at, next);

        static bool Ends(char character) => character is '\0' or ' ' or '\t' or '<' || char.IsControl(character);
    }

    /// <summary>Whether an entity is spelled here — by its shape alone, as Markdig ends an address at one.</summary>
    private static bool Entity(ReadOnlySpan<char> written)
    {
        if (written.Length < 4) return false;

        var next = 1;

        if (written[next] == '#')
        {
            next++;
            var hex = next < written.Length && written[next] is 'x' or 'X';
            if (hex) next++;

            var digits = next;
            while (next < written.Length && (hex ? char.IsAsciiHexDigit(written[next]) : char.IsAsciiDigit(written[next]))) next++;

            return next - digits is >= 1 and var count && count <= (hex ? 6 : 7) && next < written.Length && written[next] == ';';
        }

        if (!char.IsAsciiLetter(written[next])) return false;

        var start = next;
        while (next < written.Length && char.IsAsciiLetterOrDigit(written[next])) next++;

        return next - start is >= 2 and <= 32 && next < written.Length && written[next] == ';';
    }

    /// <summary>
    /// Where an address written bare from <paramref name="at"/> to <paramref name="end"/> ends once it is a link — short of any
    /// of <paramref name="pending"/> at its end, the characters of emphasis opened round it — or -1 where it is none: nothing
    /// after its scheme, or no domain.
    /// </summary>
    public int Bared(int at, int end, ReadOnlySpan<char> pending)
    {
        var link = _text.AsSpan(at, end - at);

        if (pending.Length > 0)
        {
            var kept = link.Length;
            while (kept > 0 && pending.Contains(link[kept - 1])) kept--;

            link = link[..kept];
        }

        var domain = 0;

        switch (link[0])
        {
            case 'h':
                if (link is "http://" or "https://") return -1;
                domain = link[4] == 's' ? 8 : 7;
                break;

            case 'w':
                domain = 4;
                break;

            case 'f':
                if (link is "ftp://") return -1;
                domain = 6;
                break;

            case 'm':
                var address = link.IndexOf('@');
                if (address is -1 or 7) return -1;
                domain = address + 1;
                break;

            // A number's scheme, and nothing after it but the colon a sentence ends on.
            case 't':
                if (link is "tel") return -1;
                break;
        }

        if (link[0] != 't' && !Domain(link, domain)) return -1;

        return at + link.Length;
    }

    /// <summary>
    /// Whether an address names a domain: letters, digits, hyphens and underscores in labels parted by full stops, at least
    /// one stop, and no underscore in the last two labels — as GitHub reads one, letters of any alphabet allowed.
    /// </summary>
    private static bool Domain(ReadOnlySpan<char> link, int from)
    {
        var labels = 1;
        var worded = false;
        var underscored = -1;

        for (var at = from; at < link.Length; at++)
        {
            var character = link[at];

            if (!char.IsAsciiLetterOrDigit(character))
            {
                if (character == '.')
                {
                    if (!worded) return false;

                    labels++;
                    worded = false;
                    continue;
                }

                if (character is '/' or '?' or '#' or ':') break;

                if (character == '_') underscored = labels;
                else if (character != '-' && Spaced(character)) return false;
            }

            worded = true;
        }

        return labels != 1 && worded && labels - underscored >= 2;

        static bool Spaced(char character) => character <= 127
            ? character is <= ' ' or (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~')
            : char.GetUnicodeCategory(character) is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator or UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation;
    }

    // ── What follows a bracket ───────────────────────────────────────────────

    /// <summary>
    /// What a <c>]</c> ending at <paramref name="at"/> is followed by that makes it a link written inline — <c>(where "title")</c>
    /// — and where each part of that stands; null where nothing like one follows.
    /// </summary>
    public MarkdownTail? Inline(int at)
    {
        if (this.CharAt(at) != '(') return null;

        var line = this.LineOf(at);
        var walk = new MarkdownLinkSyntax(_text, _lines, line, at);
        walk.Next();
        walk.SkipWhitespace();

        if (!walk.Destination(inline: true, out var from, out var to)) return null;

        (int From, int To)? title = null;

        if (walk.SkipWhitespace())
        {
            var (spacedLine, spaced) = (walk.Line, walk.At);

            if (walk.Title(out var says, out var said))
            {
                title = (says, said);
                walk.SkipWhitespace();
            }
            else
            {
                walk.Restore(spacedLine, spaced);
            }
        }

        if (walk.Current != ')') return null;
        walk.Next();

        return new MarkdownTail(walk.At, (from, to), title);
    }

    /// <summary>Where a label written at <paramref name="at"/> — <c>[name]</c>, or <c>[]</c> — ends, or -1 where none is written there.</summary>
    public int Label(int at)
    {
        if (this.CharAt(at) != '[') return -1;

        var walk = new MarkdownLinkSyntax(_text, _lines, this.LineOf(at), at);
        return walk.Label(blank: true) ? walk.At : -1;
    }

    /// <summary>What stands between two places, a line ending between each two lines.</summary>
    public string Between(int from, int to) => MarkdownLinkSyntax.Between(_text, _lines, from, to);
}
