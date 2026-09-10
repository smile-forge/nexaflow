using System.Text;
using System.Xml;

namespace Nexaflow.Features.Office.Search;

/// <summary>
/// The text gathered for one search read, capped at the allowance the search contract hands an extractor.
/// <para>
/// Every append reports whether there is room left, so a reader stops the moment the budget is spent rather
/// than parsing the rest of a document it will never use. Text is streamed in through
/// <see cref="AppendElementText"/> in fixed-size chunks, so no single XML node is ever held whole — the memory
/// bound is the budget itself, even for a crafted file with one multi-gigabyte text run.
/// </para>
/// </summary>
internal sealed class TextBudget
{
    /// <summary>UTF-16 — the rate <c>PdfTextReader</c> charges too, so two extractors given the same byte
    /// allowance agree on how much text it buys.</summary>
    private const int BytesPerChar = 2;

    private readonly StringBuilder _text = new();
    private readonly long _capacity;
    private readonly char[] _chunk = new char[4096];

    public TextBudget(long maxBytes) => _capacity = Math.Max(0, maxBytes / BytesPerChar);

    public bool IsFull => _text.Length >= _capacity;

    /// <summary>Appends <paramref name="c"/> if there is room. False once the budget is spent.</summary>
    public bool Append(char c)
    {
        if (IsFull) return false;
        _text.Append(c);
        return !IsFull;
    }

    /// <summary>Appends as much of <paramref name="text"/> as fits. False once the budget is spent.</summary>
    public bool Append(ReadOnlySpan<char> text)
    {
        var room = (int)Math.Min(text.Length, _capacity - _text.Length);
        if (room > 0) _text.Append(text[..room]);
        return !IsFull;
    }

    /// <summary>
    /// Ends the current line — a paragraph, a line break, a property value. Consecutive ends collapse, and
    /// none is written before any text, so a document made only of empty paragraphs reads as the empty string
    /// ("read it, there is no text") rather than as whitespace.
    /// </summary>
    public bool EndLine()
    {
        if (_text.Length == 0 || _text[_text.Length - 1] == '\n') return !IsFull;
        return Append('\n');
    }

    /// <summary>
    /// Appends the text content of the element <paramref name="reader"/> is positioned on, and leaves the
    /// reader on that element's end tag. Whitespace nodes count: in WordprocessingML a
    /// <c>&lt;w:t xml:space="preserve"&gt; &lt;/w:t&gt;</c> run is the space between two words.
    /// </summary>
    public bool AppendElementText(XmlReader reader)
    {
        if (reader.IsEmptyElement) return !IsFull;

        var depth = reader.Depth;
        while (reader.Read() && reader.Depth > depth)
        {
            if (reader.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA
                                        or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace))
                continue;

            int read;
            while ((read = reader.ReadValueChunk(_chunk, 0, _chunk.Length)) > 0)
                if (!Append(_chunk.AsSpan(0, read))) return false;
        }
        return !IsFull;
    }

    public override string ToString() => _text.ToString();
}
