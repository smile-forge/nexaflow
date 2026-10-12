namespace Nexaflow.Markdown.Rtf;

/// <summary>What one stretch of RTF source is to whatever reads it.</summary>
public enum RtfTokenKind
{
    /// <summary><c>{</c> — a group opens, and everything set inside it is undone where it closes.</summary>
    Open,

    /// <summary><c>}</c>.</summary>
    Close,

    /// <summary><c>\b</c>, <c>\fs24</c>, <c>\par </c> — a control word, with its parameter and the space that ended it.</summary>
    Word,

    /// <summary><c>\~</c>, <c>\{</c>, <c>\*</c> — a backslash and the one character after it that is not a letter.</summary>
    Symbol,

    /// <summary><c>\'e9</c> — one byte of text, written in hex.</summary>
    Hex,

    /// <summary>Characters that are only themselves.</summary>
    Text,

    /// <summary>The bytes a <c>\binN</c> said follow it, held raw.</summary>
    Binary,

    /// <summary>A carriage return or a line feed, which RTF reads past as if it were not there.</summary>
    Newline,
}

/// <summary>
/// One stretch of RTF source and what it is.
/// </summary>
/// <param name="Kind">What it is.</param>
/// <param name="Start">Where it starts in the source.</param>
/// <param name="Length">How many characters of the source it is.</param>
/// <param name="Name">A control word's name, or the character a symbol is; empty for anything else.</param>
/// <param name="Parameter">A control word's number, or the byte a <see cref="RtfTokenKind.Hex"/> spells; null where there is none.</param>
public readonly record struct RtfToken(RtfTokenKind Kind, int Start, int Length, string Name, int? Parameter)
{
    /// <summary>Where it ends in the source.</summary>
    public int End => Start + Length;

    /// <summary>Whether it is the control word <paramref name="name"/>.</summary>
    public bool Is(string name) => Kind == RtfTokenKind.Word && string.Equals(Name, name, StringComparison.Ordinal);
}

/// <summary>
/// Cuts RTF source into its tokens, every character of it in exactly one — so whatever is built from them can be printed
/// back as the source it came from.
///
/// <para>
/// <strong>The source is the document's bytes, one character to a byte.</strong> RTF is a seven-bit format that spells
/// anything else in hex, but writers put raw eight-bit bytes in their text too, and <c>\bin</c> carries bytes that are
/// nothing to do with text at all. Read the file as Latin-1 and every byte is the character of the same number, so both
/// survive and the code page a run of text is in is still the reader's to apply (<see cref="RtfCodePages"/>).
/// </para>
/// <para>
/// Nothing here knows what a control word means. That is the reader's (<see cref="RtfParser"/>) — and the unwrapper's
/// (<see cref="RtfHtmlUnwrapper"/>), which reads the same tokens for another purpose.
/// </para>
/// </summary>
public static class RtfTokenizer
{
    /// <summary>The longest control word name the format allows; anything longer is cut there.</summary>
    private const int LongestName = 32;

    /// <summary>The most digits a parameter is read for; the format's are sixteen- or thirty-two-bit.</summary>
    private const int LongestNumber = 10;

    /// <summary>Every token in <paramref name="source"/>, in order, covering every character of it.</summary>
    public static List<RtfToken> Read(string source)
    {
        var tokens = new List<RtfToken>(Math.Max(16, source.Length / 6));

        for (var at = 0; at < source.Length;)
        {
            var token = Next(source, at);
            tokens.Add(token);
            at = token.End;

            // What a \bin says follows it is bytes, whatever they look like: a brace in there closes nothing.
            if (token.Is("bin") && token.Parameter is > 0 and var count && at < source.Length)
            {
                var length = Math.Min(count, source.Length - at);
                tokens.Add(new RtfToken(RtfTokenKind.Binary, at, length, string.Empty, null));
                at += length;
            }
        }

        return tokens;
    }

    private static RtfToken Next(string source, int at)
    {
        switch (source[at])
        {
            case '{': return new RtfToken(RtfTokenKind.Open, at, 1, string.Empty, null);
            case '}': return new RtfToken(RtfTokenKind.Close, at, 1, string.Empty, null);
            case '\r':
            case '\n': return new RtfToken(RtfTokenKind.Newline, at, 1, string.Empty, null);
            case '\\': return Control(source, at);
        }

        var end = at + 1;
        while (end < source.Length && source[end] is not ('{' or '}' or '\\' or '\r' or '\n')) end++;

        return new RtfToken(RtfTokenKind.Text, at, end - at, string.Empty, null);
    }

    /// <summary>A backslash and what follows it: a control word, a hex byte, or a control symbol.</summary>
    private static RtfToken Control(string source, int at)
    {
        // A backslash with nothing after it ends the file; it is only itself.
        if (at + 1 >= source.Length) return new RtfToken(RtfTokenKind.Text, at, 1, string.Empty, null);

        var next = source[at + 1];

        if (IsLetter(next)) return Word(source, at);

        if (next == '\'' && Hex(source, at + 2) is { } value)
            return new RtfToken(RtfTokenKind.Hex, at, 4, string.Empty, value);

        return new RtfToken(RtfTokenKind.Symbol, at, 2, next.ToString(), null);
    }

    /// <summary>
    /// <c>\name</c>, then perhaps a signed number, then perhaps one space — which is part of the word, there only to say
    /// where it ends. Anything else that ends it is the next token's.
    /// </summary>
    private static RtfToken Word(string source, int at)
    {
        var end = at + 1;
        while (end < source.Length && end - at - 1 < LongestName && IsLetter(source[end])) end++;

        var name = source[(at + 1)..end];
        int? parameter = null;

        var digits = end;
        var negative = digits < source.Length && source[digits] == '-' && digits + 1 < source.Length && char.IsAsciiDigit(source[digits + 1]);
        if (negative) digits++;

        if (digits < source.Length && char.IsAsciiDigit(source[digits]))
        {
            long value = 0;
            var read = 0;

            while (digits < source.Length && char.IsAsciiDigit(source[digits]))
            {
                if (read++ < LongestNumber) value = (value * 10) + (source[digits] - '0');
                digits++;
            }

            value = Math.Clamp(negative ? -value : value, int.MinValue, int.MaxValue);
            parameter = (int)value;
            end = digits;
        }

        if (end < source.Length && source[end] == ' ') end++;

        return new RtfToken(RtfTokenKind.Word, at, end - at, name, parameter);
    }

    /// <summary>The byte two hex digits at <paramref name="at"/> spell, or null where they are not two hex digits.</summary>
    private static int? Hex(string source, int at)
    {
        if (at + 1 >= source.Length) return null;

        var high = Digit(source[at]);
        var low = Digit(source[at + 1]);

        return high < 0 || low < 0 ? null : (high << 4) | low;
    }

    private static int Digit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private static bool IsLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
