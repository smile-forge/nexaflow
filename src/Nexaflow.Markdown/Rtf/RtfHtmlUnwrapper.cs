using System.Text;

namespace Nexaflow.Markdown.Rtf;

/// <summary>
/// The HTML a mail client wrapped in RTF, taken back out — MS-OXRTFEX's de-encapsulation. Outlook keeps an HTML message's
/// body as RTF that says <c>\fromhtml1</c> in its header: the tags in <c>{\*\htmltag …}</c> groups, the text between them
/// as RTF, and whatever RTF it added only to make the result readable as RTF marked off by <c>\htmlrtf</c>.
///
/// <para>
/// <strong>Extraction, not conversion.</strong> What comes out is the HTML that went in, character for character up to
/// insignificant space and line endings — so a message written in HTML is shown as HTML however its body was stored.
/// RTF that was never HTML is not this class's to read; <see cref="Unwrap"/> says so with a null, and the caller shows it
/// as the RTF it is (<see cref="RtfParser"/>).
/// </para>
/// </summary>
public static class RtfHtmlUnwrapper
{
    /// <summary>How many of the first tokens may say what the document was made from, as the specification bounds it.</summary>
    private const int HeaderTokens = 10;

    /// <summary>
    /// The HTML <paramref name="rtf"/> encapsulates, or null where its header does not say it was made from HTML. The source
    /// is the document's bytes, one character to each, as <see cref="RtfTokenizer"/> reads it.
    /// </summary>
    public static string? Unwrap(string? rtf)
    {
        if (rtf is null || !rtf.StartsWith("{\\rtf1", StringComparison.Ordinal)) return null;

        var tokens = RtfTokenizer.Read(rtf);

        return FromHtml(tokens) ? new Unwrapping(rtf).Read(tokens) : null;
    }

    /// <summary>Whether the header says the document was made from HTML — within its first ten group marks and control words.</summary>
    private static bool FromHtml(List<RtfToken> tokens)
    {
        var seen = 0;

        foreach (var token in tokens)
        {
            if (token.Kind == RtfTokenKind.Newline) continue;
            if (token.Kind is not (RtfTokenKind.Open or RtfTokenKind.Word)) return false;
            if (token.Is("fromhtml")) return token.Parameter is not 0;
            if (token.Is("fromtext")) return false;
            if (++seen >= HeaderTokens) return false;
        }

        return false;
    }

    /// <summary>What a group is, to the unwrapping.</summary>
    private enum Within
    {
        /// <summary>The body: text not marked off by <c>\htmlrtf</c> is the HTML's.</summary>
        Body,

        /// <summary>An <c>\htmltag</c>: everything in it is the HTML's.</summary>
        Tag,

        /// <summary>The font table, read for what each font's bytes are.</summary>
        Fonts,

        /// <summary>Anything else that draws nothing — read past.</summary>
        Skip,
    }

    /// <summary>One group's state: what it is, whether its text is marked off, its font, and how a <c>\u</c> is spelled in it.</summary>
    private sealed record Group(Within Within, bool Suppressed, int Font, int Fallback, bool Starred);

    /// <summary>One unwrapping of one document.</summary>
    private sealed class Unwrapping(string rtf)
    {
        /// <summary>The groups that draw nothing and are not marked with a <c>\*</c>, as an RTF reader knows them.</summary>
        private static readonly HashSet<string> Unread =
        [
            "colortbl", "stylesheet", "info", "pict", "header", "headerl", "headerr", "headerf", "footer", "footerl",
            "footerr", "footerf", "footnote", "nonshppict", "xe", "tc", "txe", "rxe", "template",
        ];

        private readonly StringBuilder _html = new();
        private readonly List<byte> _bytes = [];
        private readonly RtfFontTable _fonts = new();
        private readonly Stack<Group> _outer = new();

        private Group _group = new(Within.Body, Suppressed: false, Font: -1, Fallback: 1, Starred: false);
        private int _codePage = RtfCodePages.Western;
        private int _bytesPage = RtfCodePages.Western;
        private int _skipping;

        public string Read(List<RtfToken> tokens)
        {
            foreach (var token in tokens) Take(token);

            Flush();

            return _html.ToString();
        }

        private void Take(RtfToken token)
        {
            switch (token.Kind)
            {
                case RtfTokenKind.Open:
                    Flush();
                    _outer.Push(_group);
                    _group = _group with { Starred = false };
                    _skipping = 0;
                    return;

                case RtfTokenKind.Close:
                    Flush();

                    if (_group.Within == Within.Fonts && _outer.Count > 0 && _outer.Peek().Within != Within.Fonts) _fonts.Commit(_codePage);
                    if (_outer.Count > 0) _group = _outer.Pop();

                    _skipping = 0;
                    return;

                case RtfTokenKind.Word: Worded(token); return;
                case RtfTokenKind.Symbol: Symbolled(token); return;
                case RtfTokenKind.Hex: Hexed(token); return;
                case RtfTokenKind.Text: Texted(token); return;
            }
        }

        /// <summary>Whether what is read now is the HTML's: anything in a tag, and the body's text where nothing marks it off.</summary>
        private bool Copying => _group.Within == Within.Tag || (_group.Within == Within.Body && !_group.Suppressed);

        private void Worded(RtfToken token)
        {
            var starred = _group.Starred;
            if (starred) _group = _group with { Starred = false };

            if (_group.Within == Within.Skip) return;

            // What a group is: named by the first word in it.
            switch (token.Name)
            {
                case "htmltag":
                    _group = _group with { Within = Within.Tag };
                    return;

                case "fonttbl":
                    _group = _group with { Within = Within.Fonts };
                    return;
            }

            if (starred || Unread.Contains(token.Name))
            {
                Flush();
                _group = _group with { Within = Within.Skip };

                return;
            }

            if (_group.Within == Within.Fonts)
            {
                switch (token.Name)
                {
                    case "f": _fonts.Begin(token.Parameter ?? 0, _codePage); break;
                    case "fcharset": _fonts.Charset(token.Parameter ?? 0); break;
                    case "cpg": _fonts.CodePage(token.Parameter ?? 0); break;
                }

                return;
            }

            switch (token.Name)
            {
                // The current font is followed even where \htmlrtf marks it off: it says what the next bytes are.
                case "f": Flush(); _group = _group with { Font = token.Parameter ?? -1 }; return;
                case "ansicpg": _codePage = token.Parameter is > 0 ? token.Parameter.Value : RtfCodePages.Western; return;
                case "uc": _group = _group with { Fallback = Math.Max(token.Parameter ?? 1, 0) }; return;
                case "htmlrtf": _group = _group with { Suppressed = token.Parameter is not 0 }; return;
            }

            if (!Copying) return;

            if (_skipping > 0)
            {
                _skipping--;

                return;
            }

            switch (token.Name)
            {
                case "par":
                case "line":
                    Append("\r\n");
                    break;

                case "tab":
                    Append("\t");
                    break;

                case "u":
                    Append(((char)((token.Parameter is { } n ? (n < 0 ? n + 65536 : n) : '?') & 0xFFFF)).ToString());
                    _skipping = _group.Fallback;
                    break;

                case "emdash": Append("—"); break;
                case "endash": Append("–"); break;
                case "bullet": Append("\u2022"); break;
                case "lquote": Append("‘"); break;
                case "rquote": Append("’"); break;
                case "ldblquote": Append("“"); break;
                case "rdblquote": Append("”"); break;
                case "emspace": Append("\u2003"); break;
                case "enspace": Append("\u2002"); break;
            }
        }

        private void Symbolled(RtfToken token)
        {
            var symbol = token.Name[0];

            if (symbol == '*')
            {
                _group = _group with { Starred = true };

                return;
            }

            if (_group.Within == Within.Fonts && symbol is '\\' or '{' or '}')
            {
                _fonts.Text(token.Name, _codePage);

                return;
            }

            if (!Copying) return;

            if (_skipping > 0)
            {
                _skipping--;

                return;
            }

            switch (symbol)
            {
                case '\\' or '{' or '}': Append(token.Name); break;
                case '~': Append("\u00A0"); break;
                case '_': Append("\u2011"); break;
                case '-': Append("\u00AD"); break;
                case '\r' or '\n': Append("\r\n"); break;
            }
        }

        private void Hexed(RtfToken token)
        {
            var value = (byte)token.Parameter!.Value;

            if (_group.Within == Within.Fonts)
            {
                _fonts.Byte(value);

                return;
            }

            if (!Copying) return;

            if (_skipping > 0)
            {
                _skipping--;

                return;
            }

            Byte(value);
        }

        private void Texted(RtfToken token)
        {
            var text = rtf.AsSpan(token.Start, token.Length);

            if (_group.Within == Within.Fonts)
            {
                _fonts.Text(text, _codePage);

                return;
            }

            if (!Copying) return;

            if (_skipping > 0)
            {
                var skip = Math.Min(_skipping, text.Length);
                _skipping -= skip;
                text = text[skip..];
            }

            foreach (var c in text)
            {
                if (c >= 0x80) Byte((byte)c);
                else
                {
                    Flush();
                    _html.Append(c);
                }
            }
        }

        /// <summary>
        /// A byte of text, in the code page it is to be read in: a tag's in the document's, the body's in its font's.
        /// </summary>
        private void Byte(byte value)
        {
            var page = _group.Within == Within.Tag ? _codePage : _fonts.For(_group.Font).CodePage ?? _codePage;

            if (_bytes.Count > 0 && page != _bytesPage) Flush();

            _bytesPage = page;
            _bytes.Add(value);
        }

        private void Append(string text)
        {
            Flush();
            _html.Append(text);
        }

        /// <summary>The bytes read so far, decoded together — a character of a double-byte page is two of them.</summary>
        private void Flush()
        {
            if (_bytes.Count == 0) return;

            _html.Append(RtfCodePages.Decode([.. _bytes], _bytesPage, symbol: false));
            _bytes.Clear();
        }
    }
}
