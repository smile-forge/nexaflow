using Nexaflow.IO.Protocol.Values;
using System.Globalization;
using System.Text;

namespace Nexaflow.IO.Protocol.Expressions;

internal enum TokenKind
{
    End, Int, Number, Text, Ident,
    LParen, RParen, LBracket, RBracket, Comma, Dot, Colon, Question,
    Plus, Minus, Star, Slash, Percent,
    Shl, Shr, Amp, Caret, Pipe, Tilde, Bang,
    Lt, Le, Gt, Ge, EqEq, NotEq,
    AndAnd, OrOr, Coalesce, PipeGt,
    Arrow, Assign,
}

internal readonly record struct Token(TokenKind Kind, string Textual, ProtoValue? Literal, int Position);

/// <summary>
/// Tokeniser for the pipeline expression language.
///
/// <para>
/// Two lexical decisions carry real weight. <c>|&gt;</c> is scanned before <c>|</c> and <c>||</c>, because
/// the pipeline and bitwise-or are now distinct operators — in the first grammar they were the same
/// character, which meant <c>2 ^ poll</c> quietly computed a xor and an NTP poll interval came out as 4
/// seconds instead of 64. And a decimal literal containing <c>.</c> or an exponent lexes as
/// <see cref="ProtoValue.Num"/> rather than <see cref="ProtoValue.Int"/>, without which
/// <c>keepAlive * 0.75</c> truncates to zero.
/// </para>
/// </summary>
internal sealed class Lexer(string source)
{
    private readonly string _s = source;
    private int _i;

    public List<Token> Tokenise()
    {
        List<Token> tokens = [];
        while (true)
        {
            var t = Next();
            tokens.Add(t);
            if (t.Kind == TokenKind.End) return tokens;
        }
    }

    private Token Next()
    {
        while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        if (_i >= _s.Length) return new Token(TokenKind.End, "", null, _i);

        int start = _i;
        char c = _s[_i];

        // ── Numbers ──────────────────────────────────────────────────────────
        if (char.IsAsciiDigit(c)) return ScanNumber(start);

        // ── Identifiers and word operators ───────────────────────────────────
        if (char.IsAsciiLetter(c) || c == '_' || c == '@' || c == '$')
        {
            _i++;
            while (_i < _s.Length && (char.IsAsciiLetterOrDigit(_s[_i]) || _s[_i] is '_' or '@' or '$')) _i++;
            var word = _s[start.._i];

            // NOTE: the word forms `band`/`bxor`/`bor` stay Ident tokens. They are legal in two positions —
            // infix (`5 band 3`) and as pipeline converters (`fc |> band(0x80)`) — and rewriting them to
            // operator tokens here would make the second form a syntax error. The parser recognises them
            // infix; the converter table supplies the pipeline form.
            return word switch
            {
                "true" => new Token(TokenKind.Ident, word, ProtoValue.Of(true), start),
                "false" => new Token(TokenKind.Ident, word, ProtoValue.Of(false), start),
                "null" => new Token(TokenKind.Ident, word, ProtoValue.Nothing, start),
                _ => new Token(TokenKind.Ident, word, null, start),
            };
        }

        // ── Quoted text ──────────────────────────────────────────────────────
        if (c == '\'' || c == '"') return ScanText(start, c);

        // ── Operators, longest match first ───────────────────────────────────
        foreach (var (text, kind) in Operators)
        {
            if (_i + text.Length > _s.Length) continue;
            if (string.CompareOrdinal(_s, _i, text, 0, text.Length) != 0) continue;

            _i += text.Length;
            return new Token(kind, text, null, start);
        }

        throw new ProtoSyntaxException($"unexpected character '{c}'", start);
    }

    // Order matters: every multi-character operator precedes its own prefix, so "|>" is never scanned as
    // "|" followed by ">", and "<=" is never "<" then "=".
    private static readonly (string Text, TokenKind Kind)[] Operators =
    [
        ("|>", TokenKind.PipeGt),
        ("->", TokenKind.Arrow),      // before "-", so a lambda arrow is never a minus then a greater-than
        ("||", TokenKind.OrOr),
        ("&&", TokenKind.AndAnd),
        ("??", TokenKind.Coalesce),
        ("==", TokenKind.EqEq),
        ("!=", TokenKind.NotEq),
        ("<=", TokenKind.Le),
        (">=", TokenKind.Ge),
        ("<<", TokenKind.Shl),
        (">>", TokenKind.Shr),
        ("(", TokenKind.LParen), (")", TokenKind.RParen),
        ("[", TokenKind.LBracket), ("]", TokenKind.RBracket),
        (",", TokenKind.Comma), (".", TokenKind.Dot), (":", TokenKind.Colon), ("?", TokenKind.Question),
        ("=", TokenKind.Assign),      // `let x = …` only; there is no assignment operator
        ("+", TokenKind.Plus), ("-", TokenKind.Minus),
        ("*", TokenKind.Star), ("/", TokenKind.Slash), ("%", TokenKind.Percent),
        ("&", TokenKind.Amp), ("^", TokenKind.Caret), ("|", TokenKind.Pipe),
        ("~", TokenKind.Tilde), ("!", TokenKind.Bang),
        ("<", TokenKind.Lt), (">", TokenKind.Gt),
    ];

    private Token ScanNumber(int start)
    {
        // Hex is always Int — 0x40 is a byte value, never a quantity to multiply by 0.75.
        if (_s[_i] == '0' && _i + 1 < _s.Length && (_s[_i + 1] is 'x' or 'X'))
        {
            _i += 2;
            int hexStart = _i;
            while (_i < _s.Length && Uri.IsHexDigit(_s[_i])) _i++;
            if (_i == hexStart) throw new ProtoSyntaxException("0x with no hex digits", start);

            var hex = _s[hexStart.._i];
            return new Token(TokenKind.Int, _s[start.._i],
                ProtoValue.Of(long.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture)), start);
        }

        bool isReal = false;
        while (_i < _s.Length && char.IsAsciiDigit(_s[_i])) _i++;

        // A '.' only makes this a real if a digit follows — otherwise it is member access on an integer,
        // which the parser handles (and which no sane document writes, but the lexer must not guess).
        if (_i + 1 < _s.Length && _s[_i] == '.' && char.IsAsciiDigit(_s[_i + 1]))
        {
            isReal = true;
            _i++;
            while (_i < _s.Length && char.IsAsciiDigit(_s[_i])) _i++;
        }

        if (_i < _s.Length && (_s[_i] is 'e' or 'E'))
        {
            int save = _i;
            _i++;
            if (_i < _s.Length && (_s[_i] is '+' or '-')) _i++;
            if (_i < _s.Length && char.IsAsciiDigit(_s[_i]))
            {
                isReal = true;
                while (_i < _s.Length && char.IsAsciiDigit(_s[_i])) _i++;
            }
            else _i = save;   // 'e' was the start of an identifier, not an exponent
        }

        var text = _s[start.._i];
        return isReal
            ? new Token(TokenKind.Number, text,
                        ProtoValue.Of(double.Parse(text, CultureInfo.InvariantCulture)), start)
            : new Token(TokenKind.Int, text,
                        ProtoValue.Of(long.Parse(text, CultureInfo.InvariantCulture)), start);
    }

    private Token ScanText(int start, char quote)
    {
        _i++;   // opening quote
        var sb = new StringBuilder();

        while (true)
        {
            if (_i >= _s.Length) throw new ProtoSyntaxException("unterminated string", start);

            char c = _s[_i];
            if (c == quote) { _i++; break; }

            if (c == '\\' && _i + 1 < _s.Length)
            {
                _i++;
                sb.Append(_s[_i] switch
                {
                    'n' => '\n', 'r' => '\r', 't' => '\t', '0' => '\0',
                    '\\' => '\\', '\'' => '\'', '"' => '"',
                    var other => throw new ProtoSyntaxException($"unknown escape '\\{other}'", _i),
                });
                _i++;
                continue;
            }

            sb.Append(c);
            _i++;
        }

        return new Token(TokenKind.Text, sb.ToString(), ProtoValue.Of(sb.ToString()), start);
    }
}
