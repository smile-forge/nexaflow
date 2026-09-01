namespace Nexaflow.Maths.Latex;

/// <summary>What a token is. TeX's own categories, minus the ones maths mode never sees.</summary>
internal enum TexTokenKind
{
    /// <summary>One ordinary character of content.</summary>
    Character,

    /// <summary>A backslash and the letters after it: <c>\alpha</c>, <c>\frac</c>, <c>\begin</c>.</summary>
    ControlWord,

    /// <summary>A backslash and one character that is not a letter: <c>\\</c>, <c>\{</c>, <c>\,</c>.</summary>
    ControlSymbol,

    OpenBrace,
    CloseBrace,
    Ampersand,
    Superscript,
    Subscript,
    Space,
    Comment,
}

/// <summary>A token, holding the very characters it was cut from.</summary>
/// <remarks>
/// Carrying the text rather than a span is what makes "the parser only ever copies" structural: there is
/// nowhere for a synthesized character to come from, because the parser never sees the source — only
/// pieces of it.
/// </remarks>
internal readonly record struct TexToken(TexTokenKind Kind, string Text)
{
    /// <summary>Whether this is the control word <paramref name="name"/> — <c>\end</c>, say.</summary>
    public bool Is(string name) => this.Kind == TexTokenKind.ControlWord && this.Text == name;

    /// <summary>Whether this is the control symbol <paramref name="text"/> — <c>\\</c>, say.</summary>
    public bool Symbol(string text) => this.Kind == TexTokenKind.ControlSymbol && this.Text == text;

    /// <summary>Whether this is space or a comment — there but saying nothing.</summary>
    public bool IsTrivia => this.Kind is TexTokenKind.Space or TexTokenKind.Comment;
}

/// <summary>
/// The source, cut into tokens. Every character of the input lands in exactly one of them, in order, so
/// pasting their text back together gives the input.
/// </summary>
internal static class TexLexer
{
    public static List<TexToken> Scan(string latex)
    {
        var tokens = new List<TexToken>();
        var at = 0;

        while (at < latex.Length)
        {
            var c = latex[at];

            switch (c)
            {
                case '{': tokens.Add(new TexToken(TexTokenKind.OpenBrace, "{")); at++; continue;
                case '}': tokens.Add(new TexToken(TexTokenKind.CloseBrace, "}")); at++; continue;
                case '&': tokens.Add(new TexToken(TexTokenKind.Ampersand, "&")); at++; continue;
                case '^': tokens.Add(new TexToken(TexTokenKind.Superscript, "^")); at++; continue;
                case '_': tokens.Add(new TexToken(TexTokenKind.Subscript, "_")); at++; continue;
            }

            if (c == '\\')
            {
                // A trailing backslash is a backslash. TeX would complain; an editor holds it, because
                // it is what every command looks like a keystroke before it is one.
                if (at + 1 >= latex.Length)
                {
                    tokens.Add(new TexToken(TexTokenKind.ControlSymbol, "\\"));
                    at++;
                    continue;
                }

                if (char.IsLetter(latex[at + 1]))
                {
                    var end = at + 1;
                    while (end < latex.Length && char.IsLetter(latex[end])) end++;
                    tokens.Add(new TexToken(TexTokenKind.ControlWord, latex[at..end]));
                    at = end;
                    continue;
                }

                tokens.Add(new TexToken(TexTokenKind.ControlSymbol, latex.Substring(at, 2)));
                at += 2;
                continue;
            }

            if (c == '%')
            {
                var end = at;
                while (end < latex.Length && latex[end] is not ('\n' or '\r')) end++;
                tokens.Add(new TexToken(TexTokenKind.Comment, latex[at..end]));
                at = end;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                var end = at;
                while (end < latex.Length && char.IsWhiteSpace(latex[end])) end++;
                tokens.Add(new TexToken(TexTokenKind.Space, latex[at..end]));
                at = end;
                continue;
            }

            tokens.Add(new TexToken(TexTokenKind.Character, latex.Substring(at, 1)));
            at++;
        }

        return tokens;
    }
}
