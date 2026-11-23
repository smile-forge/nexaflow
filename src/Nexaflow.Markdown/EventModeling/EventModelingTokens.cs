using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.EventModeling;

/// <summary>
/// An <c>eventmodeling</c> block as Mermaid's grammar for it reads one: a stream of tokens, with space, comments and directives hidden
/// between them wherever they stand — line endings included, so a statement may run over several rows and several may share one.
///
/// <para>
/// The tokens are the grammar's own. A word is a letter or an underscore and then letters, digits and underscores; a number is digits; a
/// data value on its row runs from its brace or its quote to the last of the same on that row; a data block opens with a brace ending
/// its row and closes with a brace at the start of one. Comments are <c>%%</c> and <c>//</c> to the end of their row and <c>/* … */</c>,
/// and a directive is <c>%%{ … }%%</c>. Inside a value or a block every character is the value's, comments and all, as the grammar's
/// tokens take them.
/// </para>
/// </summary>
internal static class EventModelingTokens
{
    /// <summary>What a token is.</summary>
    internal enum Kind
    {
        /// <summary>Nothing: the block has ended.</summary>
        End,

        /// <summary>A word: a letter or an underscore, then letters, digits and underscores.</summary>
        Word,

        /// <summary>Digits.</summary>
        Number,

        /// <summary><c>->></c>.</summary>
        Feed,

        /// <summary><c>[[</c>.</summary>
        OpenReference,

        /// <summary><c>]]</c>.</summary>
        CloseReference,

        /// <summary><c>.</c>.</summary>
        Dot,

        /// <summary>A backtick.</summary>
        Tick,

        /// <summary><c>:</c>.</summary>
        Colon,

        /// <summary>A data value on its row, in braces or quotes.</summary>
        Inline,

        /// <summary>A data block, from a brace ending its row to a brace starting one.</summary>
        Block,

        /// <summary>A character the grammar makes nothing of.</summary>
        Other,
    }

    /// <summary>A token: what it is, where it starts and ends, and — for a value or a block — whether it was closed.</summary>
    internal readonly record struct Token(Kind Kind, int Start, int End, bool Closed = true);

    /// <summary>The token starting at <paramref name="at"/>, which stands past any trivia.</summary>
    public static Token At(string source, int at)
    {
        if (at >= source.Length) return new Token(Kind.End, source.Length, source.Length);

        var character = source[at];

        if (Starts(character)) return new Token(Kind.Word, at, Run(source, at + 1, Carries));
        if (char.IsAsciiDigit(character)) return new Token(Kind.Number, at, Run(source, at + 1, char.IsAsciiDigit));
        if (Sees(source, at, "->>")) return new Token(Kind.Feed, at, at + 3);
        if (Sees(source, at, "[[")) return new Token(Kind.OpenReference, at, at + 2);
        if (Sees(source, at, "]]")) return new Token(Kind.CloseReference, at, at + 2);

        return character switch
        {
            '.' => new Token(Kind.Dot, at, at + 1),
            '`' => new Token(Kind.Tick, at, at + 1),
            ':' => new Token(Kind.Colon, at, at + 1),
            '{' when Opens(source, at) => Block(source, at),
            '{' or '"' or '\'' => Inline(source, at),
            _ => new Token(Kind.Other, at, at + 1),
        };
    }

    /// <summary>Where the trivia starting at <paramref name="at"/> ends: space, comments and directives, as far as they run.</summary>
    public static int Past(string source, int at)
    {
        while (at < source.Length)
        {
            if (char.IsWhiteSpace(source[at])) at++;
            else if (Trivium(source, at) is { } trivium) at = trivium.End;
            else break;
        }

        return at;
    }

    /// <summary>The trivia between <paramref name="from"/> and <paramref name="to"/> as pieces, each where it was written.</summary>
    public static IEnumerable<ContentNode> Trivia(string source, int from, int to)
    {
        for (var at = from; at < to;)
        {
            if (char.IsWhiteSpace(source[at]))
            {
                var past = Run(source, at, char.IsWhiteSpace, to);
                yield return ContentNode.Leaf(Kinds.Space, source[at..past], Roles.Trivia, offset: at);
                at = past;
            }
            else if (Trivium(source, at) is { } trivium)
            {
                yield return ContentNode.Leaf(trivium.Kind, source[at..trivium.End], Roles.Trivia, offset: at);
                at = trivium.End;
            }
            else
            {
                // Nothing that is not trivia is ever handed here; held whole rather than lost should that change.
                yield return ContentNode.Leaf(Kinds.Space, source[at..to], Roles.Trivia, offset: at);
                yield break;
            }
        }
    }

    /// <summary>Where the row holding <paramref name="at"/> ends, before its line ending.</summary>
    public static int RowEnd(string source, int at)
    {
        var newline = source.IndexOf('\n', at);
        var end = newline < 0 ? source.Length : newline;
        return end > at && source[end - 1] == '\r' ? end - 1 : end;
    }

    /// <summary>Whether a word may start with <paramref name="character"/>.</summary>
    public static bool Starts(char character) => char.IsAsciiLetter(character) || character == '_';

    /// <summary>Whether a word carries on with <paramref name="character"/>.</summary>
    public static bool Carries(char character) => char.IsAsciiLetterOrDigit(character) || character == '_';

    /// <summary>Whether <paramref name="source"/> holds <paramref name="text"/> at <paramref name="at"/>.</summary>
    public static bool Sees(string source, int at, string text) => source.AsSpan(at).StartsWith(text, StringComparison.Ordinal);

    /// <summary>A comment or a directive starting at <paramref name="at"/>: what it is and where it ends — or null where none starts there.</summary>
    private static (string Kind, int End)? Trivium(string source, int at)
    {
        if (Sees(source, at, "%%{") && source.IndexOf("}%%", at + 3, StringComparison.Ordinal) is var shut and >= 0)
            return (EventModelingKinds.Directive, shut + 3);

        if (Sees(source, at, "%%") || Sees(source, at, "//")) return (Kinds.Comment, RowEnd(source, at));

        if (Sees(source, at, "/*"))
        {
            var close = source.IndexOf("*/", at + 2, StringComparison.Ordinal);
            return (Kinds.Comment, close < 0 ? source.Length : close + 2);
        }

        return null;
    }

    /// <summary>Whether the brace at <paramref name="at"/> opens a block: nothing but spaces and tabs after it on its row, and another row under it.</summary>
    private static bool Opens(string source, int at)
    {
        var past = Run(source, at + 1, character => character is ' ' or '\t');
        if (past < source.Length && source[past] == '\r') past++;
        return past < source.Length && source[past] == '\n';
    }

    /// <summary>
    /// A data block from its brace: to the first brace standing at the start of a row with nothing but space after it, as the
    /// grammar's block runs — or to the end of the block where none does.
    /// </summary>
    private static Token Block(string source, int at)
    {
        for (var close = source.IndexOf("\n}", at, StringComparison.Ordinal); close >= 0; close = source.IndexOf("\n}", close + 1, StringComparison.Ordinal))
        {
            var after = close + 2;
            if (after >= source.Length || char.IsWhiteSpace(source[after])) return new Token(Kind.Block, at, after);
        }

        return new Token(Kind.Block, at, source.Length, Closed: false);
    }

    /// <summary>
    /// A data value on its row, from its brace or its quote to the last closing brace or the last such quote on the row, as the
    /// grammar's greedy value runs — or to the end of the row where nothing closes it there.
    /// </summary>
    private static Token Inline(string source, int at)
    {
        var closing = source[at] == '{' ? '}' : source[at];
        var end = RowEnd(source, at);
        var close = end > at + 1 ? source.LastIndexOf(closing, end - 1, end - at - 1) : -1;

        return close > at ? new Token(Kind.Inline, at, close + 1) : new Token(Kind.Inline, at, end, Closed: false);
    }

    /// <summary>Where a run of characters <paramref name="carries"/> starting at <paramref name="at"/> ends, never past <paramref name="limit"/>.</summary>
    private static int Run(string source, int at, Func<char, bool> carries, int limit = int.MaxValue)
    {
        var end = Math.Min(limit, source.Length);
        while (at < end && carries(source[at])) at++;
        return at;
    }
}
