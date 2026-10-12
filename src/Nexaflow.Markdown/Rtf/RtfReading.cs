using System.Text;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Markdown.Rtf;

/// <summary>
/// One reading of one RTF document: the tokens, walked once, with the state every group sets, building the blocks as the
/// paragraphs end. See <see cref="RtfParser"/> for what comes out.
/// </summary>
internal sealed partial class RtfReading
{
    /// <summary>What RTF calls ordinary text where a document says nothing: twelve points, in half-points.</summary>
    public const int OrdinarySize = 24;

    /// <summary>How many twips the ordinary text size is — what a distance is measured against.</summary>
    private const double TwipsPerEm = OrdinarySize * 10;

    private readonly string _source;
    private readonly List<RtfToken> _tokens;

    // ── What the document declares ──────────────────────────────────────────

    private readonly RtfFontTable _fonts = new();
    private readonly List<uint?> _colours = [];
    private readonly Dictionary<int, string> _links = [];
    private int _codePage = RtfCodePages.Western;
    private int _defaultFont = -1;
    private int _defaultStop = 720;
    private int _fields;

    // The colour being read into the colour table.
    private int _red, _green, _blue;
    private bool _coloured;

    // ── The groups ──────────────────────────────────────────────────────────

    private RtfGroup _group = new();
    private readonly Stack<RtfGroup> _outer = new();

    public RtfReading(string source)
    {
        _source = source;
        _tokens = RtfTokenizer.Read(source);
    }

    /// <summary>Reads the whole document.</summary>
    public ContentNode Read()
    {
        foreach (var token in _tokens) Take(token);

        Finish();

        return new BlockNode(RtfParser.Language, _blocks, offset: _source.Length > 0 ? 0 : null);
    }

    private void Take(RtfToken token)
    {
        switch (token.Kind)
        {
            case RtfTokenKind.Open: Opened(token); return;
            case RtfTokenKind.Close: Closed(token); return;
            case RtfTokenKind.Word: Worded(token); return;
            case RtfTokenKind.Symbol: Symbolled(token); return;
            case RtfTokenKind.Hex: Hexed(token); return;
            case RtfTokenKind.Text: Texted(token); return;

            case RtfTokenKind.Binary:
                _group.Picture?.Bytes(_source.AsSpan(token.Start, token.Length));
                Silent(token, RtfKinds.Binary);
                return;

            case RtfTokenKind.Newline:
                Silent(token, Kinds.Space);
                return;
        }
    }

    // ── Groups ──────────────────────────────────────────────────────────────

    private void Opened(RtfToken token)
    {
        Machinery(Leaf(token, Kinds.Token, Roles.Open));

        _outer.Push(_group);
        _group = _group.Inner();
        _group.Begins = _line.Count - 1;
        _group.Line = _lines;
    }

    private void Closed(RtfToken token)
    {
        // A brace closing nothing is only itself, and says so.
        if (_outer.Count == 0)
        {
            Machinery(ContentNode.Leaf(Kinds.Token, Text(token), Roles.Close, "This closes a group that was never opened.", token.Start));

            return;
        }

        // The document's own group closing ends the paragraph written last with no mark after it — set as that group still says,
        // which it no longer does once it has closed.
        if (_outer.Count == 1) Unfinished();

        Machinery(Leaf(token, Kinds.Token, Roles.Close));

        var closing = _group;

        // The font table ends where its group does, and so does an entry with no ';' after its name.
        if (closing.Destination == RtfDestination.Fonts && _outer.Peek().Destination != RtfDestination.Fonts) _fonts.Commit(_codePage);
        if (closing.OpensPicture && closing.Picture is { } picture) Pictured(closing, picture);
        if (closing.Field is { } field && !ReferenceEquals(_outer.Peek().Field, field)) Fielded(closing, field);

        _group = _outer.Pop();
    }

    // ── Control symbols ─────────────────────────────────────────────────────

    private void Symbolled(RtfToken token)
    {
        var symbol = token.Name[0];

        if (symbol == '*')
        {
            Machinery(Leaf(token, RtfKinds.Symbol, Drawing ? Roles.Name : Roles.Silent));
            _group.Starred = true;

            return;
        }

        // A backslash ending a line is a paragraph mark, spelled the old way.
        if (symbol is '\r' or '\n')
        {
            if (Drawing && !Skipped(token)) Ended(token, RtfEnd.Paragraph, 0);
            else Silent(token, RtfKinds.Symbol);

            return;
        }

        var means = symbol switch
        {
            '~' => "\u00A0",
            '-' => "\u00AD",
            '_' => "\u2011",
            '{' or '}' or '\\' => token.Name,
            _ => null,
        };

        switch (_group.Destination)
        {
            case RtfDestination.Text when means is not null && !_group.Hidden:
                if (!Skipped(token)) Coded(token, means);
                return;

            case RtfDestination.Fonts when means is not null:
                _fonts.Text(means, _codePage);
                break;

            case RtfDestination.Instruction when means is not null:
                _group.Field?.Instruction.Append(means);
                break;
        }

        Machinery(Leaf(token, RtfKinds.Symbol, Drawing ? Roles.Name : Roles.Silent));
    }

    // ── Bytes spelled in hex ────────────────────────────────────────────────

    private void Hexed(RtfToken token)
    {
        var value = (byte)token.Parameter!.Value;

        switch (_group.Destination)
        {
            case RtfDestination.Text when !_group.Hidden:
                if (!Skipped(token)) Coded(token, value);
                return;

            case RtfDestination.Fonts:
                _fonts.Byte(value);
                break;

            case RtfDestination.Instruction:
                _group.Field?.Instruction.Append(RtfCodePages.Decode([value], _codePage, symbol: false));
                break;
        }

        Silent(token, RtfKinds.Hex);
    }

    // ── Text ────────────────────────────────────────────────────────────────

    private void Texted(RtfToken token)
    {
        switch (_group.Destination)
        {
            case RtfDestination.Text when !_group.Hidden:
                Words(token.Start, token.End);
                return;

            case RtfDestination.Fonts:
                _fonts.Text(_source.AsSpan(token.Start, token.Length), _codePage);
                break;

            case RtfDestination.Colours:
                foreach (var c in _source.AsSpan(token.Start, token.Length))
                    if (c == ';') Coloured();
                break;

            case RtfDestination.Instruction:
                _group.Field?.Instruction.Append(_source, token.Start, token.Length);
                break;

            case RtfDestination.Picture:
            _group.Picture?.Hex(_source.AsSpan(token.Start, token.Length));
            break;

            case RtfDestination.Property:
            _property.Append(_source, token.Start, token.Length);
            break;
        }

        Silent(token, RtfKinds.Data);
    }

    /// <summary>
    /// A stretch of the document's text, cut where what it is changes: the characters that are only themselves, the bytes
    /// that are characters of a code page, the tabs, and whatever stands in for a <c>\u</c> just read and is read past.
    /// </summary>
    private void Words(int start, int end)
    {
        var at = start;

        while (at < end && _group.Skipping > 0)
        {
            var take = Math.Min(_group.Skipping, end - at);
            Silent(at, take, RtfKinds.Data);
            _group.Skipping -= take;
            at += take;
        }

        var font = Font();

        while (at < end)
        {
            var c = _source[at];

            if (c == '\t')
            {
                Tab(at, 1);
                at++;

                continue;
            }

            // A symbol font's every byte names a glyph, and a byte past seven bits is a character of the font's code page:
            // either way what is drawn is not what was written.
            var coded = font.Symbol || c >= 0x80;
            var stop = at + 1;

            while (stop < end && _source[stop] != '\t' && (font.Symbol || _source[stop] >= 0x80) == coded) stop++;

            if (coded)
                Coded(at, stop - at);
            else
                Drawn(ContentNode.Leaf(MarkdownKinds.Word, _source[at..stop], Roles.Element, offset: at));

            at = stop;
        }
    }

    // ── What the reading is in ──────────────────────────────────────────────

    /// <summary>Whether the group's characters are the document's text.</summary>
    private bool Drawing => _group.Destination == RtfDestination.Text;

    /// <summary>The font the group's text is in, as its bytes are read — the document's default where it names none.</summary>
    private RtfFont Font() => _fonts.For(_group.Font < 0 ? _defaultFont : _group.Font);

    /// <summary>The code page <paramref name="font"/>'s bytes are read in: its own, or the document's.</summary>
    private int CodePageOf(RtfFont font) => font.CodePage ?? _codePage;

    /// <summary>
    /// Whether <paramref name="token"/> is one of the characters standing in for a <c>\u</c> just read — read past as one
    /// character, whatever it is.
    /// </summary>
    private bool Skipped(RtfToken token)
    {
        if (_group.Skipping <= 0) return false;

        _group.Skipping--;
        Silent(token, RtfKinds.Data);

        return true;
    }

    /// <summary>The colour a table entry names, or null for the reader's own.</summary>
    private uint? Colour(int entry) => entry >= 0 && entry < _colours.Count ? _colours[entry] : null;

    /// <summary>What a wash names — nought being none at all, whatever the table's first entry is.</summary>
    private uint? Washed(int entry) => entry > 0 ? Colour(entry) : null;

    /// <summary>How the group's text is set now.</summary>
    private TextLook Look()
    {
        var font = Font();

        var look = new TextLook
        {
            Font = font.Name.Length > 0 ? font.Name : null,
            Scale = _group.Size > 0 ? _group.Size / (double)OrdinarySize : 1,
            Bold = _group.Bold,
            Italic = _group.Italic,
            Underline = _group.Underline,
            Strike = _group.Strike,
            Capitals = _group.Capitals,
            Script = _group.Script,
            Ink = Colour(_group.Ink),
            Wash = Washed(_group.Highlight) ?? Washed(_group.Background),
        };

        return look == TextLook.Plain ? TextLook.Plain : look;
    }

    /// <summary>How the paragraph being read is set, with every distance in the ordinary text size.</summary>
    private ParagraphLook Paragraph() => new()
    {
        Align = _group.Align,
        Indent = _group.Indent / TwipsPerEm,
        EndIndent = _group.EndIndent / TwipsPerEm,
        FirstLine = _group.FirstLine / TwipsPerEm,
        Before = _group.Before / TwipsPerEm,
        After = _group.After / TwipsPerEm,
        Stops = [.. _group.Stops.Select(stop => stop / TwipsPerEm)],
        EveryStop = _defaultStop / TwipsPerEm,
    };

    // ── Leaves ──────────────────────────────────────────────────────────────

    private string Text(RtfToken token) => _source.Substring(token.Start, token.Length);

    private ContentNode Leaf(RtfToken token, string kind, string role) =>
        ContentNode.Leaf(kind, Text(token), role, offset: token.Start);

    /// <summary>A token that is written and stands for nothing drawn, not even a space.</summary>
    private void Silent(RtfToken token, string kind) => Silent(token.Start, token.Length, kind);

    private void Silent(int start, int length, string kind) =>
        Quiet(ContentNode.Leaf(kind, _source.Substring(start, length), Roles.Silent, offset: start));
}
