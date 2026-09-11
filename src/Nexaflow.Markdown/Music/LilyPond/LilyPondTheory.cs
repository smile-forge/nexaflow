namespace Nexaflow.Markdown.Music.LilyPond;

/// <summary>
/// What LilyPond's own spellings mean: a note's name, a duration, a fraction.
/// <para>
/// Deliberately small and WPF-free, as ABC's is. It answers questions about notation, never about drawing.
/// </para>
/// </summary>
public static class LilyPondTheory
{
    /// <summary>
    /// The step and alteration a Dutch note name spells — <c>c</c>, <c>fis</c>, <c>bes</c>, and the contracted
    /// <c>as</c> and <c>es</c> for A flat and E flat — or null where the word spells no note at all. Being
    /// strict is what lets a word like <c>bass</c> or <c>default</c> be read as a word.
    /// </summary>
    public static (int Step, int Alter)? Name(string name)
    {
        if (name.Length == 0) return null;

        var step = "cdefgab".IndexOf(name[0]);
        if (step < 0) return null;

        int? alter = name[1..] switch
        {
            "" => 0,
            "is" => 1,
            "isis" => 2,
            "es" => -1,
            "eses" => -2,
            "s" when name[0] is 'a' or 'e' => -1,
            "ses" when name[0] is 'a' or 'e' => -2,
            _ => null,
        };

        return alter is { } by ? (step, by) : null;
    }

    /// <summary>
    /// What a duration spells — <c>4</c>, <c>8.</c>, <c>2*3/4</c> — as the value it is written as and what
    /// that is multiplied by. Null where the text is not a duration.
    /// </summary>
    public static (Duration Written, Duration Scale)? Length(string text)
    {
        var at = 0;
        var number = Digits(text, ref at);
        if (number <= 0) return null;

        var dots = 0;
        while (at < text.Length && text[at] == '.') { dots++; at++; }

        var scale = Duration.Of(1, 1);
        while (at < text.Length && text[at] == '*')
        {
            at++;
            var times = Digits(text, ref at);
            var over = 1L;
            if (at < text.Length && text[at] == '/') { at++; over = Digits(text, ref at); }
            if (times <= 0 || over <= 0) return null;
            scale *= Duration.Of(times, over);
        }

        return at == text.Length ? (Dotted(Duration.Of(4, number), dots), scale) : null;
    }

    /// <summary>A written value with its dots: each adds half of what the one before it added.</summary>
    public static Duration Dotted(Duration value, int dots)
    {
        var total = value;
        var add = value;

        for (var i = 0; i < dots; i++)
        {
            add *= Duration.Of(1, 2);
            total += add;
        }

        return total;
    }

    /// <summary>The durations written as commands rather than numbers.</summary>
    public static Duration? Named(string command) => command switch
    {
        @"\breve" => Duration.Of(8, 1),
        @"\longa" => Duration.Of(16, 1),
        @"\maxima" => Duration.Of(32, 1),
        _ => null,
    };

    /// <summary>A fraction — <c>3/4</c>, <c>3/2</c> — or null.</summary>
    public static (int Numerator, int Denominator)? Fraction(string text)
    {
        var at = 0;
        var top = Digits(text, ref at);
        if (top <= 0 || at >= text.Length || text[at] != '/') return null;

        at++;
        var bottom = Digits(text, ref at);
        return bottom > 0 && at == text.Length ? ((int)top, (int)bottom) : null;
    }

    private static long Digits(string text, ref int at)
    {
        long value = 0;
        while (at < text.Length && char.IsAsciiDigit(text[at]) && value < 100_000)
        {
            value = (value * 10) + (text[at] - '0');
            at++;
        }

        return value;
    }
}
