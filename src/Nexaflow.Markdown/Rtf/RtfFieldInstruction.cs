using System.Text;
using System.Globalization;

namespace Nexaflow.Markdown.Rtf;

/// <summary>
/// Reading what a field is from its instruction — <c>HYPERLINK "https://…" \l "section" \o "tip"</c> — the arguments
/// quoted or bare, and the switches each followed by the argument it takes.
/// </summary>
internal static class RtfFieldInstruction
{
    /// <summary>
    /// Where a <c>HYPERLINK</c> field leads: its address, a place in the document after <c>\l</c>, or the two together —
    /// null for any other field, or one that leads nowhere.
    /// </summary>
    public static string? Hyperlink(string instruction)
    {
        var words = Arguments(instruction);
        if (words.Count == 0 || !string.Equals(words[0].Text, "HYPERLINK", StringComparison.OrdinalIgnoreCase)) return null;

        string? address = null;
        string? place = null;

        for (var at = 1; at < words.Count; at++)
        {
            var (text, quoted) = words[at];

            if (!quoted && text.StartsWith('\\'))
            {
                // \l names a place in the document, \o a tip and \t a frame; \m and \n take nothing after them.
                var switched = text.ToLowerInvariant();
                var takes = switched is "\\l" or "\\o" or "\\t" && at + 1 < words.Count;

                if (takes && switched == "\\l") place = words[at + 1].Text;
                if (takes) at++;

                continue;
            }

            address ??= text;
        }

        if (place is { Length: > 0 }) return (address ?? string.Empty) + "#" + place;

        return address is { Length: > 0 } ? address : null;
    }

    /// <summary>
    /// The character a <c>SYMBOL</c> field draws — <c>SYMBOL 74 \f "Wingdings" \s 12</c> — by its number, read as a byte of the
    /// font's code page unless <c>\u</c> says it is Unicode or <c>\j</c> Shift-JIS, with the font and the size in points it names;
    /// null for any other field, or one naming no character.
    /// </summary>
    public static RtfSymbol? Symbol(string instruction)
    {
        var words = Arguments(instruction);
        if (words.Count < 2 || !string.Equals(words[0].Text, "SYMBOL", StringComparison.OrdinalIgnoreCase)) return null;
        if (Number(words[1].Text) is not { } code || code <= 0) return null;

        string? font = null;
        double? points = null;
        var set = RtfSymbolSet.Ansi;

        for (var at = 2; at < words.Count; at++)
        {
            var (text, quoted) = words[at];
            if (quoted || !text.StartsWith('\\')) continue;

            switch (text.ToLowerInvariant())
            {
                case "\\f" when at + 1 < words.Count:
                    font = words[++at].Text;
                    break;

                case "\\s" when at + 1 < words.Count:
                    if (double.TryParse(words[++at].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size > 0) points = size;
                    break;

                case "\\u": set = RtfSymbolSet.Unicode; break;
                case "\\j": set = RtfSymbolSet.ShiftJis; break;
                case "\\a": set = RtfSymbolSet.Ansi; break;
            }
        }

        return new RtfSymbol(code, font, points, set);
    }

    /// <summary>A number as a field writes one: decimal, or hexadecimal after <c>0x</c>.</summary>
    private static int? Number(string text) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>
    /// The arguments of an instruction, each as written: a quoted one without its quotes and with a doubled backslash read
    /// as the one it stands for, a bare one up to the space after it.
    /// </summary>
    private static List<(string Text, bool Quoted)> Arguments(string instruction)
    {
        var words = new List<(string, bool)>();
        var text = new StringBuilder();

        for (var at = 0; at < instruction.Length;)
        {
            var c = instruction[at];

            if (char.IsWhiteSpace(c))
            {
                at++;

                continue;
            }

            text.Clear();

            if (c == '"')
            {
                at++;

                while (at < instruction.Length && instruction[at] != '"')
                {
                    if (instruction[at] == '\\' && at + 1 < instruction.Length && instruction[at + 1] is '\\' or '"') at++;

                    text.Append(instruction[at++]);
                }

                at++;
                words.Add((text.ToString(), true));

                continue;
            }

            while (at < instruction.Length && !char.IsWhiteSpace(instruction[at])) text.Append(instruction[at++]);

            words.Add((text.ToString(), false));
        }

        return words;
    }
}

/// <summary>A <c>SYMBOL</c> field's character: its number, the font it is in, its size in points, and how the number is read.</summary>
internal sealed record RtfSymbol(int Code, string? Font, double? Points, RtfSymbolSet Set);

/// <summary>How a <c>SYMBOL</c> field's number names its character.</summary>
internal enum RtfSymbolSet
{
    /// <summary>A byte of the font's code page — or, in a symbol font, the glyph in that place.</summary>
    Ansi,

    /// <summary>A Unicode code point (<c>\u</c>).</summary>
    Unicode,

    /// <summary>A Shift-JIS double byte (<c>\j</c>).</summary>
    ShiftJis,
}
