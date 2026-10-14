using System.Text;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// A walk over the lines some text is written on, as one run of characters with a line ending between each two — and what
/// CommonMark reads in it that a link is spelled with: a label in brackets, a destination, a title.
///
/// <para>
/// A link's definition and a link written inline spell these the same way, so both are read here and nowhere else
/// (<see cref="LinkDefinitions"/>, <see cref="MarkdownTokenizer"/>). The lines are where what each holds starts and ends, past
/// whatever its containers put in front of it, so a title written over two lines of a quote is read without the quote's
/// <c>&gt;</c>.
/// </para>
/// </summary>
internal struct MarkdownLinkSyntax
{
    private readonly string _text;
    private readonly IReadOnlyList<(int From, int To)> _lines;
    private int _line;
    private int _at;

    /// <param name="line">Which of <paramref name="lines"/> the walk starts on.</param>
    /// <param name="at">Where on it: from where what it holds starts to where it ends.</param>
    public MarkdownLinkSyntax(string text, IReadOnlyList<(int From, int To)> lines, int line, int at)
    {
        _text = text;
        _lines = lines;
        _line = line;
        _at = at;
    }

    /// <summary>Where the walk stands, in the text.</summary>
    public readonly int At => _at;

    /// <summary>Which line the walk stands on.</summary>
    public readonly int Line => _line;

    /// <summary>The character the walk stands on: a line's own, a line ending between two lines, or nothing past the last.</summary>
    public readonly char Current =>
        _at < _lines[_line].To ? _text[_at] : _line + 1 < _lines.Count ? '\n' : '\0';

    /// <summary>Whether the walk stands at the end of a line, past everything it holds.</summary>
    public readonly bool AtLineEnd => _at >= _lines[_line].To;

    public void Next()
    {
        if (_at < _lines[_line].To) _at++;
        else if (_line + 1 < _lines.Count) _at = _lines[++_line].From;
    }

    /// <summary>Back to where the walk stood — a place <see cref="Line"/> and <see cref="At"/> said.</summary>
    public void Restore(int line, int at)
    {
        _line = line;
        _at = at;
    }

    public void SkipSpaces()
    {
        while (this.Current is ' ' or '\t') this.Next();
    }

    /// <summary>Space, and at most one line ending among it; whether there was any.</summary>
    public bool SkipWhitespace()
    {
        var (line, at) = (_line, _at);

        this.SkipSpaces();
        if (this.Current == '\n')
        {
            this.Next();
            this.SkipSpaces();
        }

        return (_line, _at) != (line, at);
    }

    /// <summary>Which characters a backslash takes the meaning off — CommonMark's ASCII punctuation.</summary>
    public static bool Escapable(char character) =>
        character is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';

    /// <summary>
    /// A label: a <c>[</c>, up to 999 characters holding no bracket a backslash does not take the meaning off, and a
    /// <c>]</c> — the walk past it where one is written here. A label of nothing but space names nothing, so a definition
    /// takes none; after a link's words it is a label all the same (<paramref name="blank"/>), and <c>[]</c> names the words.
    /// </summary>
    public bool Label(bool blank = false)
    {
        if (this.Current != '[') return false;
        this.Next();

        var length = 0;
        var worded = false;

        while (true)
        {
            var character = this.Current;

            switch (character)
            {
                case '\0' or '[':
                    return false;

                case ']':
                    this.Next();
                    return worded || blank;

                case '\\':
                    this.Next();
                    worded = true;
                    length++;

                    if (Escapable(this.Current))
                    {
                        this.Next();
                        length++;
                    }

                    break;

                default:
                    worded |= !char.IsWhiteSpace(character);
                    this.Next();
                    length++;
                    break;
            }

            if (length > 999) return false;
        }
    }

    /// <summary>
    /// A destination: an address in angle brackets, or one without space whose parentheses balance — the walk past it, and
    /// where the address itself stands, angle brackets left out. Written inline, a link may name none at all, so long as its
    /// parenthesis closes straight after (<paramref name="inline"/>).
    /// </summary>
    public bool Destination(bool inline, out int from, out int to)
    {
        from = to = _at;

        if (this.Current == '<')
        {
            this.Next();
            from = _at;

            while (true)
            {
                var character = this.Current;

                if (character == '>')
                {
                    to = _at;
                    this.Next();
                    return true;
                }

                if (character is '\n' or '\0' or '<') return false;

                this.Next();
                if (character == '\\' && Escapable(this.Current)) this.Next();
            }
        }

        var depth = 0;
        var count = 0;

        while (true)
        {
            var character = this.Current;

            if (character <= ' ' || character == '\u007f') break;

            if (character == '\\')
            {
                this.Next();
                count++;
                if (Escapable(this.Current)) this.Next();
                continue;
            }

            if (character == '(')
            {
                if (++depth > 32) return false;
            }
            else if (character == ')')
            {
                if (depth == 0) break;
                depth--;
            }

            this.Next();
            count++;
        }

        to = _at;
        return depth == 0 && (count > 0 || (inline && this.Current == ')'));
    }

    /// <summary>A title in double quotes, single quotes or parentheses — the walk past it, and where what it says stands.</summary>
    public bool Title(out int from, out int to)
    {
        var opener = this.Current;
        var closer = opener == '(' ? ')' : opener;
        from = to = _at;

        if (opener is not ('"' or '\'' or '(')) return false;

        this.Next();
        from = _at;

        while (true)
        {
            var character = this.Current;

            if (character == '\0') return false;

            if (character == closer)
            {
                to = _at;
                this.Next();
                return true;
            }

            if (opener == '(' && character == '(') return false;

            this.Next();
            if (character == '\\' && Escapable(this.Current)) this.Next();
        }
    }

    /// <summary>
    /// What an address or a title written this way says: each backslash taken off the punctuation it escapes, and each entity
    /// the character it stands for — <paramref name="written"/> itself where it holds neither.
    /// </summary>
    public static string Unescaped(string written)
    {
        if (written.AsSpan().IndexOfAny('\\', '&') < 0) return written;

        var said = new StringBuilder(written.Length);

        for (var at = 0; at < written.Length; at++)
        {
            var character = written[at];

            if (character == '\\' && at + 1 < written.Length && Escapable(written[at + 1]))
            {
                said.Append(written[++at]);
                continue;
            }

            if (character == '&' && MarkdownEntities.Length(written, at) is > 0 and var length)
            {
                said.Append(MarkdownEntities.Decoded(written.AsSpan(at, length)));
                at += length - 1;
                continue;
            }

            said.Append(character);
        }

        return said.ToString();
    }

    /// <summary>What stands between <paramref name="from"/> and <paramref name="to"/> on these lines, a line ending between each two.</summary>
    public static string Between(string text, IReadOnlyList<(int From, int To)> lines, int from, int to)
    {
        var said = new StringBuilder(Math.Max(to - from, 0));
        var first = true;

        foreach (var (start, end) in lines)
        {
            if (end < from) continue;
            if (start > to) break;

            if (!first) said.Append('\n');
            first = false;

            var cut = Math.Max(start, from);
            if (Math.Min(end, to) > cut) said.Append(text, cut, Math.Min(end, to) - cut);
        }

        return said.ToString();
    }
}
