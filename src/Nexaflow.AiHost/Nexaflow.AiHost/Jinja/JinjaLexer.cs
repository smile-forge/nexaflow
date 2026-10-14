using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexaflow.AiHost.Jinja;

internal enum JinjaTokenKind
{
    Text,
    OutputBegin,
    OutputEnd,
    StatementBegin,
    StatementEnd,
    Name,
    String,
    Integer,
    Float,
    Operator,
    End,
}

/// <param name="Value">A literal's value: the string, <see cref="long"/> or <see cref="double"/>.</param>
internal readonly record struct JinjaToken(JinjaTokenKind Kind, string Text, int Line, object? Value = null)
{
    public bool Is(JinjaTokenKind kind, string text) => Kind == kind && Text == text;

    public bool IsName(string name) => Is(JinjaTokenKind.Name, name);

    public bool IsOperator(string op) => Is(JinjaTokenKind.Operator, op);
}

/// <summary>
/// A template's tokens, with the whitespace rules chat templates are written for — transformers renders them with
/// <c>trim_blocks</c> and <c>lstrip_blocks</c>: the newline after a statement or comment tag is dropped, and so are the
/// spaces and tabs before one that begins its line. A <c>-</c> inside a tag strips every whitespace character on that
/// side; a <c>+</c> keeps the line's indent.
/// </summary>
internal static partial class JinjaLexer
{
    private static readonly string[] Operators =
        ["//", "**", "==", "!=", "<=", ">=", "+", "-", "*", "/", "%", "~", "<", ">", "=", "(", ")", "[", "]", "{", "}", ",", ".", ":", "|"];

    [GeneratedRegex(@"\G\{%[-+]?\s*raw\s*(-?)%\}")]
    private static partial Regex RawStart();

    [GeneratedRegex(@"\{%(-?)\s*endraw\s*(-?)%\}")]
    private static partial Regex RawEnd();

    public static List<JinjaToken> Tokenize(string source)
    {
        var lines = new Lines(source);
        var tokens = new List<JinjaToken>();
        int pos = 0;
        bool stripNext = false;
        while (pos < source.Length)
        {
            int tag = NextTag(source, pos);
            int textEnd = tag < 0 ? source.Length : tag;
            if (stripNext)
            {
                while (pos < textEnd && char.IsWhiteSpace(source[pos])) pos++;
                stripNext = false;
            }
            if (tag < 0)
            {
                AddText(tokens, source, lines, pos, source.Length);
                break;
            }

            char kind = source[tag + 1];
            char modifier = tag + 2 < source.Length ? source[tag + 2] : '\0';
            AddText(tokens, source, lines, pos, TextStop(source, pos, tag, kind, modifier));

            if (kind == '#')
            {
                int close = source.IndexOf("#}", tag + 2, StringComparison.Ordinal);
                if (close < 0) throw new JinjaException("A comment is never closed.", lines.Of(tag));
                pos = close + 2;
                if (source[close - 1] == '-' && close - 1 > tag + 1) stripNext = true;
                else pos = TrimBlock(source, pos);
                continue;
            }

            if (kind == '%' && RawStart().Match(source, tag) is { Success: true } raw)
            {
                var end = RawEnd().Match(source, raw.Index + raw.Length);
                if (!end.Success) throw new JinjaException("A raw block is never closed.", lines.Of(tag));
                int bodyStart = raw.Index + raw.Length, bodyEnd = end.Index;
                if (raw.Groups[1].Value == "-") while (bodyStart < bodyEnd && char.IsWhiteSpace(source[bodyStart])) bodyStart++;
                else bodyStart = TrimBlock(source, bodyStart);
                if (end.Groups[1].Value == "-") while (bodyEnd > bodyStart && char.IsWhiteSpace(source[bodyEnd - 1])) bodyEnd--;
                AddText(tokens, source, lines, bodyStart, bodyEnd);
                pos = end.Index + end.Length;
                if (end.Groups[2].Value == "-") stripNext = true;
                else pos = TrimBlock(source, pos);
                continue;
            }

            bool output = kind == '{';
            tokens.Add(new JinjaToken(output ? JinjaTokenKind.OutputBegin : JinjaTokenKind.StatementBegin, "", lines.Of(tag)));
            pos = tag + 2 + (modifier is '-' or '+' ? 1 : 0);
            pos = LexTag(source, lines, pos, output, tokens, out bool stripAfter);
            if (stripAfter) stripNext = true;
            else if (!output) pos = TrimBlock(source, pos);
        }
        tokens.Add(new JinjaToken(JinjaTokenKind.End, "", lines.Of(source.Length)));
        return tokens;
    }

    private static int NextTag(string source, int from)
    {
        for (int i = source.IndexOf('{', from); i >= 0 && i + 1 < source.Length; i = source.IndexOf('{', i + 1))
            if (source[i + 1] is '{' or '%' or '#') return i;
        return -1;
    }

    /// <summary>Where the text before a tag ends once the tag's whitespace control has had its way.</summary>
    private static int TextStop(string source, int from, int tag, char kind, char modifier)
    {
        int stop = tag;
        if (modifier == '-')
        {
            while (stop > from && char.IsWhiteSpace(source[stop - 1])) stop--;
            return stop;
        }
        if (kind == '{' || modifier == '+') return stop;
        while (stop > from && source[stop - 1] is ' ' or '\t') stop--;
        return stop == 0 || source[stop - 1] == '\n' ? stop : tag;
    }

    /// <summary>Past the one newline <c>trim_blocks</c> drops after a statement or comment.</summary>
    private static int TrimBlock(string source, int pos)
    {
        if (pos < source.Length && source[pos] == '\n') return pos + 1;
        if (pos + 1 < source.Length && source[pos] == '\r' && source[pos + 1] == '\n') return pos + 2;
        return pos;
    }

    private static void AddText(List<JinjaToken> tokens, string source, Lines lines, int from, int to)
    {
        if (to > from) tokens.Add(new JinjaToken(JinjaTokenKind.Text, source[from..to], lines.Of(from)));
    }

    /// <summary>Lexes a tag's inside up to its close — only where no bracket is open, so a dict literal's <c>}}</c>
    /// is not taken for the end.</summary>
    private static int LexTag(string source, Lines lines, int pos, bool output, List<JinjaToken> tokens, out bool stripAfter)
    {
        string close = output ? "}}" : "%}";
        int depth = 0;
        while (true)
        {
            while (pos < source.Length && char.IsWhiteSpace(source[pos])) pos++;
            if (pos >= source.Length) throw new JinjaException($"A tag is never closed with '{close}'.", lines.Of(pos));
            int line = lines.Of(pos);

            if (depth == 0)
            {
                if (string.CompareOrdinal(source, pos, "-" + close, 0, 3) == 0)
                {
                    tokens.Add(new JinjaToken(output ? JinjaTokenKind.OutputEnd : JinjaTokenKind.StatementEnd, "", line));
                    stripAfter = true;
                    return pos + 3;
                }
                if (string.CompareOrdinal(source, pos, close, 0, 2) == 0)
                {
                    tokens.Add(new JinjaToken(output ? JinjaTokenKind.OutputEnd : JinjaTokenKind.StatementEnd, "", line));
                    stripAfter = false;
                    return pos + 2;
                }
            }

            char c = source[pos];
            if (c is '\'' or '"')
            {
                pos = ReadString(source, lines, pos, out string value);
                tokens.Add(new JinjaToken(JinjaTokenKind.String, value, line, value));
            }
            else if (char.IsDigit(c))
            {
                pos = ReadNumber(source, pos, out var token, line);
                tokens.Add(token);
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int start = pos;
                while (pos < source.Length && (char.IsLetterOrDigit(source[pos]) || source[pos] == '_')) pos++;
                tokens.Add(new JinjaToken(JinjaTokenKind.Name, source[start..pos], line));
            }
            else
            {
                string? op = Operators.FirstOrDefault(o => string.CompareOrdinal(source, pos, o, 0, o.Length) == 0)
                             ?? throw new JinjaException($"Unexpected character '{c}'.", line);
                if (op is "(" or "[" or "{") depth++;
                else if (op is ")" or "]" or "}") depth = Math.Max(0, depth - 1);
                tokens.Add(new JinjaToken(JinjaTokenKind.Operator, op, line));
                pos += op.Length;
            }
        }
    }

    /// <summary>A string literal with Python's escapes; an escape Python does not know keeps its backslash.</summary>
    private static int ReadString(string source, Lines lines, int pos, out string value)
    {
        char quote = source[pos++];
        var text = new StringBuilder();
        while (true)
        {
            if (pos >= source.Length) throw new JinjaException("A string is never closed.", lines.Of(pos));
            char c = source[pos++];
            if (c == quote) break;
            if (c != '\\' || pos >= source.Length)
            {
                text.Append(c);
                continue;
            }
            char e = source[pos++];
            switch (e)
            {
                case 'n': text.Append('\n'); break;
                case 't': text.Append('\t'); break;
                case 'r': text.Append('\r'); break;
                case '0': text.Append('\0'); break;
                case 'a': text.Append('\a'); break;
                case 'b': text.Append('\b'); break;
                case 'f': text.Append('\f'); break;
                case 'v': text.Append('\v'); break;
                case '\\': text.Append('\\'); break;
                case '\'': text.Append('\''); break;
                case '"': text.Append('"'); break;
                case '\n': break;
                case 'x' when Hex(source, pos, 2) is { } x: text.Append((char)x); pos += 2; break;
                case 'u' when Hex(source, pos, 4) is { } u: text.Append((char)u); pos += 4; break;
                case 'U' when Hex(source, pos, 8) is { } big: text.Append(char.ConvertFromUtf32(big)); pos += 8; break;
                default: text.Append('\\').Append(e); break;
            }
        }
        value = text.ToString();
        return pos;
    }

    private static int? Hex(string source, int pos, int digits)
        => pos + digits <= source.Length
           && int.TryParse(source.AsSpan(pos, digits), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int value)
            ? value : null;

    private static int ReadNumber(string source, int pos, out JinjaToken token, int line)
    {
        int start = pos;
        while (pos < source.Length && (char.IsDigit(source[pos]) || source[pos] == '_')) pos++;
        bool isFloat = false;
        if (pos + 1 < source.Length && source[pos] == '.' && char.IsDigit(source[pos + 1]))
        {
            isFloat = true;
            pos++;
            while (pos < source.Length && (char.IsDigit(source[pos]) || source[pos] == '_')) pos++;
        }
        if (pos < source.Length && source[pos] is 'e' or 'E')
        {
            int exponent = pos + 1;
            if (exponent < source.Length && source[exponent] is '+' or '-') exponent++;
            if (exponent < source.Length && char.IsDigit(source[exponent]))
            {
                isFloat = true;
                pos = exponent;
                while (pos < source.Length && char.IsDigit(source[pos])) pos++;
            }
        }
        string text = source[start..pos].Replace("_", "");
        token = isFloat
            ? new JinjaToken(JinjaTokenKind.Float, text, line, double.Parse(text, CultureInfo.InvariantCulture))
            : new JinjaToken(JinjaTokenKind.Integer, text, line, long.Parse(text, CultureInfo.InvariantCulture));
        return pos;
    }

    /// <summary>The line a position is on, found among the template's newlines.</summary>
    private sealed class Lines
    {
        private readonly int[] _newlines;

        public Lines(string source)
        {
            var newlines = new List<int>();
            for (int i = source.IndexOf('\n'); i >= 0; i = source.IndexOf('\n', i + 1)) newlines.Add(i);
            _newlines = [.. newlines];
        }

        public int Of(int pos)
        {
            int index = Array.BinarySearch(_newlines, pos);
            return (index < 0 ? ~index : index) + 1;
        }
    }
}
