namespace Nexaflow.Markdown.Prose;

/// <summary>
/// <c>[name]: where "title"</c> at the head of a paragraph — as many as are written there — each a block of its own in
/// front of what is left of the paragraph, which the stages read for what it defines.
///
/// <para>
/// Read as CommonMark reads one: a name of up to 999 characters holding no unescaped bracket, a colon, an address in
/// angle brackets or one without space whose parentheses balance, and a title in quotes or parentheses, parted from the
/// address by space or one line ending. A title followed by anything but space is no title, and the definition ends with
/// the address's line — so long as nothing but space follows the address there.
/// </para>
/// </summary>
internal sealed class LinkDefinitions : IParagraphReading
{
    private readonly WrittenRule _written = new();

    public void Read(MarkdownScanner scan, ScannedBlock paragraph)
    {
        var text = scan.Text;
        var lines = paragraph.Lines;
        var taken = 0;

        while (taken < lines.Count && new Reader(text, lines, taken).Defined() is { } through)
        {
            var (from, _) = lines[taken];
            while (text[from] is ' ' or '\t') from++;

            var (_, to) = lines[through];
            while (to > from && text[to - 1] is ' ' or '\t') to--;

            var definition = new ScannedBlock(_written, ScannedKind.Reference, from) { Line = paragraph.Line + taken, IsOpen = false };
            definition.Reach(to);

            var parent = paragraph.Parent!;
            parent.Insert(parent.IndexOf(paragraph), definition);
            parent.Reach(definition.End);

            taken = through + 1;
        }

        if (taken == 0) return;

        lines.RemoveRange(0, taken);

        if (lines.Count > 0)
        {
            var from = lines[0].From;
            while (from < lines[0].To && text[from] is ' ' or '\t') from++;
            paragraph.Start = from;
        }
    }

    /// <summary>What a definition is held as: written where it stands, and closed.</summary>
    private sealed class WrittenRule : MarkdownBlockRule
    {
        public override Continued Continues(MarkdownScanner scan, ScannedBlock block) => Continued.Unmatched;
    }

    /// <summary>A walk over a paragraph's lines, as one run of characters with a line ending between each two.</summary>
    private struct Reader
    {
        private readonly string _text;
        private readonly List<(int From, int To)> _lines;
        private int _line;
        private int _at;

        public Reader(string text, List<(int From, int To)> lines, int line)
        {
            _text = text;
            _lines = lines;
            _line = line;
            _at = lines[line].From;
        }

        private readonly char Current =>
            _at < _lines[_line].To ? _text[_at] : _line + 1 < _lines.Count ? '\n' : '\0';

        private readonly bool AtLineEnd => _at >= _lines[_line].To;

        private void Next()
        {
            if (_at < _lines[_line].To) _at++;
            else if (_line + 1 < _lines.Count) _at = _lines[++_line].From;
        }

        private void SkipSpaces()
        {
            while (this.Current is ' ' or '\t') this.Next();
        }

        /// <summary>Space, and at most one line ending among it; whether there was any.</summary>
        private bool SkipWhitespace()
        {
            var start = (_line, _at);

            this.SkipSpaces();
            if (this.Current == '\n')
            {
                this.Next();
                this.SkipSpaces();
            }

            return (_line, _at) != start;
        }

        private static bool Escapable(char character) => character is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';

        /// <summary>The last line of a definition starting where the walk stands, or null where none does.</summary>
        public int? Defined()
        {
            this.SkipSpaces();

            if (!this.Label() || this.Current != ':') return null;
            this.Next();

            this.SkipWhitespace();
            if (!this.Destination()) return null;

            var afterDestination = (_line, _at);

            if (this.SkipWhitespace() && this.Current is '"' or '\'' or '(' && this.Title())
            {
                this.SkipSpaces();
                if (this.AtLineEnd) return _line;
            }

            // No title, or one followed by more: the definition is the address's line, if nothing else stands on it.
            (_line, _at) = afterDestination;
            this.SkipSpaces();

            return this.AtLineEnd ? _line : null;
        }

        private bool Label()
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
                        return worded;

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

        private bool Destination()
        {
            if (this.Current == '<')
            {
                this.Next();

                while (true)
                {
                    var character = this.Current;

                    if (character == '>')
                    {
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

            return count > 0 && depth == 0;
        }

        private bool Title()
        {
            var opener = this.Current;
            var closer = opener == '(' ? ')' : opener;
            this.Next();

            while (true)
            {
                var character = this.Current;

                if (character == '\0') return false;

                if (character == closer)
                {
                    this.Next();
                    return true;
                }

                if (opener == '(' && character == '(') return false;

                this.Next();
                if (character == '\\' && Escapable(this.Current)) this.Next();
            }
        }
    }
}
