using System.Text;

namespace Nexaflow.Markdown.Rtf;

/// <summary>A font, as the document's text is read in it.</summary>
/// <param name="Name">What the family is called.</param>
/// <param name="CodePage">What its bytes are read as, or null for the document's own code page.</param>
/// <param name="Symbol">Whether its bytes name glyphs rather than characters.</param>
internal sealed record RtfFont(string Name, int? CodePage, bool Symbol)
{
    /// <summary>A font the document never declared: no name, and the document's code page.</summary>
    public static RtfFont Undeclared { get; } = new(string.Empty, null, Symbol: false);
}

/// <summary>
/// The font table, read entry by entry — <c>\f1\fcharset204 Arial;</c>, each in a group of its own or one after another —
/// and what each font's bytes are read as. Both readers of RTF need it: what is drawn is set in a font, and the bytes of
/// text in it are in its code page.
/// </summary>
internal sealed class RtfFontTable
{
    private readonly Dictionary<int, RtfFont> _fonts = [];

    private int _number = -1;
    private int? _charset;
    private int? _codePage;
    private readonly StringBuilder _name = new();
    private readonly List<byte> _bytes = [];

    /// <summary>The font <paramref name="number"/> names, or <see cref="RtfFont.Undeclared"/>.</summary>
    public RtfFont For(int number) => _fonts.TryGetValue(number, out var font) ? font : RtfFont.Undeclared;

    /// <summary>The font the table declares by <paramref name="name"/>, or <see cref="RtfFont.Undeclared"/> where it declares none so called.</summary>
    public RtfFont Named(string name)
    {
        foreach (var font in _fonts.Values)
            if (string.Equals(font.Name, name, StringComparison.OrdinalIgnoreCase)) return font;

        return RtfFont.Undeclared;
    }

    /// <summary><c>\fN</c>: an entry begins, ending any still being read.</summary>
    public void Begin(int number, int documentPage)
    {
        Commit(documentPage);
        _number = number;
    }

    /// <summary><c>\fcharsetN</c>.</summary>
    public void Charset(int charset) => _charset = charset;

    /// <summary><c>\cpgN</c>.</summary>
    public void CodePage(int codePage) => _codePage = codePage;

    /// <summary>Characters of the entry's name, up to the <c>;</c> that ends it.</summary>
    public void Text(ReadOnlySpan<char> text, int documentPage)
    {
        foreach (var c in text)
        {
            if (c == ';')
            {
                Commit(documentPage);

                continue;
            }

            if (_number < 0) continue;

            if (c >= 0x80) _bytes.Add((byte)c);
            else
            {
                Pending(documentPage);
                _name.Append(c);
            }
        }
    }

    /// <summary>A byte of the entry's name, spelled in hex.</summary>
    public void Byte(byte value)
    {
        if (_number >= 0) _bytes.Add(value);
    }

    /// <summary>A character of the entry's name, spelled as a <c>\u</c>.</summary>
    public void Unicode(char unit, int documentPage)
    {
        if (_number < 0) return;

        Pending(documentPage);
        _name.Append(unit);
    }

    /// <summary>The entry being read, finished — where one is.</summary>
    public void Commit(int documentPage)
    {
        if (_number < 0) return;

        Pending(documentPage);

        var page = _codePage is > 0 ? _codePage : _charset is { } charset ? RtfCodePages.OfCharset(charset) : null;
        _fonts[_number] = new RtfFont(_name.ToString().Trim(), page, _charset == RtfCodePages.SymbolCharset);

        (_number, _charset, _codePage) = (-1, null, null);
        _name.Clear();
    }

    /// <summary>The bytes of the name read so far, decoded in the entry's own code page.</summary>
    private void Pending(int documentPage)
    {
        if (_bytes.Count == 0) return;

        var page = _codePage is > 0 ? _codePage.Value
                 : _charset is { } charset && RtfCodePages.OfCharset(charset) is { } named ? named
                 : documentPage;

        _name.Append(RtfCodePages.Decode([.. _bytes], page, symbol: false));
        _bytes.Clear();
    }

    /// <summary>A copy of the table as it stands, to be read on separately.</summary>
    public RtfFontTable Copy()
    {
        var copy = new RtfFontTable { _number = _number, _charset = _charset, _codePage = _codePage };

        foreach (var (number, font) in _fonts) copy._fonts[number] = font;
        copy._name.Append(_name);
        copy._bytes.AddRange(_bytes);

        return copy;
    }

    /// <summary>Whether <paramref name="other"/> declares the same fonts, and is part-way through the same entry.</summary>
    public bool Alike(RtfFontTable other) =>
        _number == other._number && _charset == other._charset && _codePage == other._codePage
        && _name.Equals(other._name) && _bytes.SequenceEqual(other._bytes)
        && _fonts.Count == other._fonts.Count
        && _fonts.All(entry => other._fonts.TryGetValue(entry.Key, out var font) && font == entry.Value);
}
