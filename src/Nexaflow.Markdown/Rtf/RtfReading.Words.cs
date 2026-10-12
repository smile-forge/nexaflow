using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Markdown.Rtf;

/// <summary>
/// What each control word means: the destination a group is, how the characters and the paragraph are set, where a
/// paragraph, a cell or a row ends, and what the document declares about itself.
/// </summary>
internal sealed partial class RtfReading
{
    /// <summary>
    /// Destinations a reader of the text has no use for and that are written without the <c>\*</c> that would say so —
    /// the document's own description, its styles, its running heads and feet, the notes at its foot, its index entries.
    /// </summary>
    private static readonly HashSet<string> Unread =
    [
        "stylesheet", "info", "header", "headerl", "headerr", "headerf", "footer", "footerl", "footerr", "footerf",
        "footnote", "nonshppict", "shprslt", "xe", "tc", "txe", "rxe", "template", "author", "operator", "title", "subject",
        "keywords", "doccomm", "comment", "company", "creatim", "revtim", "printim", "buptim",
    ];

    /// <summary>Every way RTF has of underlining — dotted, doubled, waved, thick — each of which is drawn as an underline.</summary>
    private static readonly HashSet<string> Underlines =
    [
        "ul", "uld", "uldash", "uldashd", "uldashdd", "uldb", "ulhwave", "ulldash", "ulth", "ulthd", "ulthdash",
        "ulthdashd", "ulthdashdd", "ulthldash", "ululdbwave", "ulw", "ulwave",
    ];

    /// <summary>Control words that stand for a character.</summary>
    private static readonly Dictionary<string, string> Characters = new(StringComparer.Ordinal)
    {
        ["emdash"] = "—",
        ["endash"] = "–",
        ["emspace"] = "\u2003",
        ["enspace"] = "\u2002",
        ["qmspace"] = "\u2005",
        ["bullet"] = "\u2022",
        ["lquote"] = "‘",
        ["rquote"] = "’",
        ["ldblquote"] = "“",
        ["rdblquote"] = "”",
        ["zwj"] = "\u200D",
        ["zwnj"] = "\u200C",
        ["zwbo"] = "\u200B",
        ["zwnbo"] = "\uFEFF",
        ["ltrmark"] = "\u200E",
        ["rtlmark"] = "\u200F",
    };

    private void Worded(RtfToken token)
    {
        if (Destined(token)) return;

        var drawn = _group.Destination switch
        {
            RtfDestination.Text => Texting(token),
            RtfDestination.Settings => Setting(token),
            RtfDestination.Fonts => Fonting(token),
            RtfDestination.Colours => Colouring(token),
            RtfDestination.Picture => Picturing(token),
            RtfDestination.Instruction => Instructing(token),
            _ => false,
        };

        if (drawn) return;

        if (Drawing) Machinery(Leaf(token, RtfKinds.Control, Roles.Name));
        else Silent(token, RtfKinds.Control);
    }

    // ── Destinations ────────────────────────────────────────────────────────

    /// <summary>
    /// Whether <paramref name="token"/> names what its group is — and makes it so. A group a <c>\*</c> marked, naming a
    /// destination this does not read, is read past: that is what the star asks of a reader that does not know it.
    /// </summary>
    private bool Destined(RtfToken token)
    {
        var starred = _group.Starred;
        _group.Starred = false;

        // Inside what is read past, nothing names anything.
        if (_group.Destination == RtfDestination.Skip) return false;

        switch (token.Name)
        {
            case "fonttbl": Becomes(token, RtfDestination.Fonts); return true;
            case "colortbl": Becomes(token, RtfDestination.Colours); return true;

            case "pict":
            // A shape's pictures are its properties' values, and only one of them is what the shape shows.
            if (_group.Destination == RtfDestination.Shape && !Showing())
            {
                Becomes(token, RtfDestination.Skip);
                return true;
            }

            Becomes(token, RtfDestination.Picture);
                _group.Picture = new RtfPicture();
                _group.OpensPicture = true;
                return true;

            case "fldinst":
                Becomes(token, _group.Field is null ? RtfDestination.Skip : RtfDestination.Instruction);
                return true;

            case "fldrslt":
                Machinery(Leaf(token, RtfKinds.Control, Roles.Name));
                _group.Destination = RtfDestination.Text;
                _group.Link = _group.Field is { } field ? Linked(field) : 0;
                return true;

            // A drawing shape is a list of properties, of which the picture it shows is one (pib) and the words written in it are
            // another (shptxt): those are what is drawn, where the shape stands. Its result — the same thing again for a reader that
            // draws no shapes — is then passed over (Unread).
            case "shpinst":
                Becomes(token, RtfDestination.Shape);
                _group.Shaped = true;
                _shapePictured = false;
                return true;

            case "sn" when _group.Destination == RtfDestination.Shape:
                Becomes(token, RtfDestination.Property);
                _property.Clear();
                return true;

            // The words in a shape are drawn where it shows no picture; a picture shape's are the picture again.
            case "shptxt" when _group.Destination == RtfDestination.Shape:
                Becomes(token, _shapePictured ? RtfDestination.Skip : RtfDestination.Text);
                return true;

            // A nested table's row is defined, and ended, in a destination of its own.
            case "nesttableprops": Becomes(token, RtfDestination.Settings); return true;

            // What holds a picture, or the text a list's marker is drawn as, is read as the text it is.
            case "shppict":
            case "listtext":
            case "pntext":
                Machinery(Leaf(token, RtfKinds.Control, Roles.Name));
                return true;
        }

        if (!starred && !Unread.Contains(token.Name)) return false;

        Becomes(token, RtfDestination.Skip);

        return true;
    }

    /// <summary>The group is now <paramref name="destination"/>, named by <paramref name="token"/>.</summary>
    private void Becomes(RtfToken token, RtfDestination destination)
    {
        var drawing = Drawing;
        _group.Destination = destination;

        if (drawing) Machinery(Leaf(token, RtfKinds.Control, Roles.Name));
        else Silent(token, RtfKinds.Control);
    }

    // ── The text ────────────────────────────────────────────────────────────

    /// <summary>A control word in the document's text: something drawn, the end of something, or how something is set.</summary>
    private bool Texting(RtfToken token)
    {
        if (Skipped(token)) return true;

        switch (token.Name)
        {
            case "par":
            case "sect":
                Ended(token, RtfEnd.Paragraph, 0);
                return true;

            case "cell":
                Ended(token, RtfEnd.Cell, 1);
                return true;

            case "nestcell":
                Ended(token, RtfEnd.Cell, Math.Max(_group.Depth, 2));
                return true;

            case "row":
                Ended(token, RtfEnd.Row, 1);
                return true;

            case "nestrow":
                Ended(token, RtfEnd.Row, Math.Max(_group.Depth, 2));
                return true;
        }

        if (!_group.Hidden)
        {
            switch (token.Name)
            {
                case "tab":
                    Tab(token.Start, token.Length);
                    return true;

                case "line":
                case "column":
                Broken(token);
                return true;

                // A page break inside a table's cell is a cell going on to the next page, which this continuous page has no use for.
                case "page":
                if (_group.Depth > 0) Broken(token);
                else Paged(token);
                return true;

                case "u":
                    Coded(token, ((char)Unit(token.Parameter)).ToString());
                    _group.Skipping = _group.Fallback;
                    return true;
            }

            if (Characters.TryGetValue(token.Name, out var means))
            {
                Coded(token, means);

                return true;
            }
        }

        Set(token);

        return false;
    }

    /// <summary>A control word where settings are read and nothing is drawn: a nested row's definition, and its end.</summary>
    private bool Setting(RtfToken token)
    {
        if (token.Is("nestrow"))
        {
            Ended(token, RtfEnd.Row, Math.Max(_group.Depth, 2));

            return true;
        }

        Set(token);

        return false;
    }

    /// <summary>What a <c>\u</c> names: a UTF-16 unit, written signed where it is past the sixteen-bit signed range.</summary>
    private static int Unit(int? parameter) => parameter is { } n ? (n < 0 ? n + 65536 : n) & 0xFFFF : '?';

    // ── How things are set ──────────────────────────────────────────────────

    /// <summary>What a control word sets: the characters, the paragraph, a table's row, or the document.</summary>
    private void Set(RtfToken token)
    {
        var n = token.Parameter;
        var on = n is not 0;
        var group = _group;

        switch (token.Name)
        {
            // The characters.
            case "plain": group.Plain(_defaultFont); break;
            case "b": group.Bold = on; break;
            case "i": group.Italic = on; break;
            case "strike":
            case "striked": group.Strike = on; break;
            case "caps":
            case "scaps": group.Capitals = on; break;
            case "v": group.Hidden = on; break;
            case "ulnone": group.Underline = false; break;
            case "sub": group.Script = TextScript.Sub; break;
            case "super": group.Script = TextScript.Super; break;
            case "nosupersub": group.Script = TextScript.None; break;
            case "up": group.Script = n is > 0 ? TextScript.Super : TextScript.None; break;
            case "dn": group.Script = n is > 0 ? TextScript.Sub : TextScript.None; break;
            case "f": group.Font = n ?? _defaultFont; break;
            case "fs": group.Size = n is > 0 ? n.Value : OrdinarySize; break;
            case "cf": group.Ink = n ?? -1; break;
            case "cb":
            case "chcbpat": group.Background = n ?? 0; break;
            case "highlight": group.Highlight = n ?? 0; break;
            case "uc": group.Fallback = Math.Max(n ?? 1, 0); break;

            // The paragraph.
            case "pard": group.Ordinary(); break;
            case "intbl": group.Depth = Math.Max(group.Depth, 1); break;
            case "itap": group.Depth = Math.Max(n ?? 1, 0); break;
            case "ql": group.Align = ParagraphAlign.Start; break;
            case "qc": group.Align = ParagraphAlign.Centre; break;
            case "qr": group.Align = ParagraphAlign.End; break;
            case "qj":
            case "qd": group.Align = ParagraphAlign.Justified; break;
            case "li":
            case "lin": group.Indent = n ?? 0; break;
            case "ri":
            case "rin": group.EndIndent = n ?? 0; break;
            case "fi": group.FirstLine = n ?? 0; break;
            case "sb": group.Before = Math.Max(n ?? 0, 0); break;
            case "sa": group.After = Math.Max(n ?? 0, 0); break;
            case "tx":
            case "tb": group.Stops = [.. group.Stops.Append(n ?? 0).Order()]; break;

            // A table's row.
            case "trowd":
                _defined[Math.Max(group.Depth, 1)] = [];
                _defining = new RtfCellDefinition();
                break;

            case "clmgf": _defining.MergesAcross = true; break;
            case "clmrg": _defining.MergedAcross = true; break;
            case "clvmgf": _defining.MergesDown = true; break;
            case "clvmrg": _defining.MergedDown = true; break;

            case "cellx":
                _defining.Right = n;
                if (!_defined.TryGetValue(Math.Max(group.Depth, 1), out var row)) _defined[Math.Max(group.Depth, 1)] = row = [];
                row.Add(_defining);
                _defining = new RtfCellDefinition();
                break;

            // The document.
            case "ansi": _codePage = RtfCodePages.Western; break;
            case "mac": _codePage = 10000; break;
            case "pc": _codePage = 437; break;
            case "pca": _codePage = 850; break;
            case "ansicpg": _codePage = n is > 0 ? n.Value : RtfCodePages.Western; break;
            case "deftab": _defaultStop = n is > 0 ? n.Value : 720; break;

            case "deff":
                _defaultFont = n ?? -1;
                if (group.Font < 0) group.Font = _defaultFont;
                break;

            case "field": group.Field = new RtfField(++_fields); break;

            default:
                // Every kind of underline is an underline, and \ul0 is none.
                if (Underlines.Contains(token.Name)) group.Underline = on;
                break;
        }
    }

    // ── The font table ──────────────────────────────────────────────────────

    private bool Fonting(RtfToken token)
    {
        switch (token.Name)
        {
            case "f": _fonts.Begin(token.Parameter ?? 0, _codePage); break;
            case "fcharset": _fonts.Charset(token.Parameter ?? 0); break;
            case "cpg": _fonts.CodePage(token.Parameter ?? 0); break;
            case "u": _fonts.Unicode((char)Unit(token.Parameter), _codePage); break;
            case "deff": _defaultFont = token.Parameter ?? -1; break;
        }

        return false;
    }

    // ── The colour table ────────────────────────────────────────────────────

    private bool Colouring(RtfToken token)
    {
        var n = Math.Clamp(token.Parameter ?? 0, 0, 255);

        switch (token.Name)
        {
            case "red": _red = n; _coloured = true; break;
            case "green": _green = n; _coloured = true; break;
            case "blue": _blue = n; _coloured = true; break;
        }

        return false;
    }

    /// <summary>A colour table entry, finished by its <c>;</c> — the reader's own colour where it gave none.</summary>
    private void Coloured()
    {
        _colours.Add(_coloured ? 0xFF000000u | (uint)(_red << 16) | (uint)(_green << 8) | (uint)_blue : null);
        (_red, _green, _blue, _coloured) = (0, 0, 0, false);
    }

    // ── Pictures and fields ─────────────────────────────────────────────────

    private bool Picturing(RtfToken token)
    {
        _group.Picture?.Said(token.Name, token.Parameter);

        return false;
    }

    private bool Instructing(RtfToken token)
    {
        if (token.Is("u")) _group.Field?.Instruction.Append((char)Unit(token.Parameter));

        return false;
    }

    /// <summary>
    /// The link a field's result leads along, where its instruction is a <c>HYPERLINK</c> — or nought. Where it leads is its
    /// address, or a place in the document after <c>\l</c>, or both.
    /// </summary>
    private int Linked(RtfField field)
    {
        if (_links.ContainsKey(field.Number)) return field.Number;

        var said = RtfFieldInstruction.Hyperlink(field.Instruction.ToString());
        if (said is not { Length: > 0 } destination) return 0;

        _links[field.Number] = destination;

        return field.Number;
    }

    /// <summary>
    /// A field whose group has closed. A <c>SYMBOL</c> field is a character named by number in a font — the result Word writes
    /// for it is often empty, the character being drawn from the instruction — so where its result drew nothing, the whole of
    /// its source is drawn as that character, in that font, at the size it says.
    /// </summary>
    private void Fielded(RtfGroup group, RtfField field)
    {
        if (group.Line != _lines || group.Begins < 0 || group.Begins >= _line.Count) return;
        if (RtfFieldInstruction.Symbol(field.Instruction.ToString()) is not { } symbol) return;

        for (var at = group.Begins; at < _line.Count; at++)
            if (_line[at].Drawn) return;

        var font = symbol.Font is { Length: > 0 } named ? _fonts.Named(named) : Font();
        var means = symbol.Set switch
        {
            RtfSymbolSet.Unicode => char.ConvertFromUtf32(symbol.Code),
            RtfSymbolSet.ShiftJis => RtfCodePages.Decode([(byte)(symbol.Code >> 8), (byte)symbol.Code], 932, symbol: false),
            _ when symbol.Code > byte.MaxValue => char.ConvertFromUtf32(symbol.Code),
            _ => RtfCodePages.Decode([(byte)symbol.Code], CodePageOf(font), font.Symbol),
        };

        var look = Look() with
        {
            Font = symbol.Font is { Length: > 0 } family ? family : Look().Font,
            Scale = symbol.Points is { } points ? points * 2 / OrdinarySize : Look().Scale,
        };

        List<ContentNode> inside = [];
        for (var at = group.Begins; at < _line.Count; at++) inside.Add(_line[at].Node);
        inside.Add(Fact(MarkdownKinds.Decoded, MarkdownRoles.Decoded, means));

        _line.RemoveRange(group.Begins, _line.Count - group.Begins);
        _line.Add(new Inline(ContentNode.Branch(MarkdownKinds.Encoded, inside, Roles.Element, Begins(inside)), Drawn: true, look, group.Link));
    }
}
