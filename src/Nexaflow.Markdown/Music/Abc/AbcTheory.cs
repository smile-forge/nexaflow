namespace Nexaflow.Markdown.Music.Abc;

/// <summary>
/// What is in force where a note is written: the key, the meter, and the length one letter stands for.
/// <para>
/// None of it is written on the note. All of it is written above it, possibly many lines above it,
/// possibly changed halfway down — so it is worked out once by walking the tune in order and hung on each
/// line as a fact, rather than looked up again by everything that needs it.
/// </para>
/// </summary>
public readonly record struct AbcContext(int Fifths, int Beats, int BeatUnit, Duration Unit, string Voice)
{
    /// <summary>What a tune with no header at all is read as: C major, 4/4, eighth notes.</summary>
    public static readonly AbcContext Default = new(0, 4, 4, new Duration(1, 2), "");

    /// <summary>How long one bar is.</summary>
    public Duration Bar => Duration.Of(this.Beats * 4, this.BeatUnit);
}

/// <summary>
/// What ABC's own spellings mean: which key a <c>K:</c> field names, what a length suffix multiplies the
/// unit by, what an <c>M:</c> field counts.
/// <para>
/// Deliberately small and WPF-free. The theory under the spellings — what a key alters, how a pitch is
/// moved — is <see cref="Keys"/> and <see cref="Pitch"/>, which every notation shares; how wide a note
/// is, where its stem goes and whether it beams are the builder's, and depend on the page.
/// </para>
/// </summary>
public static class AbcTheory
{
    /// <summary>
    /// How far round the circle of fifths a key sits, from its tonic and mode — <c>K:Bb</c>, <c>K:Cm</c>,
    /// <c>K:C Lydian</c>, <c>K:Ador</c>. Null when the field names no key at all (<c>K:none</c>, or a
    /// field that only sets a clef).
    /// </summary>
    public static int? Fifths(string field)
    {
        var text = field.Trim();
        if (text.Length == 0) return null;

        // Anything after the key itself — clef=bass, transpose=-2 — is not part of it.
        var head = text.Split([' ', '\t'], 2);
        var key = head[0];
        var rest = head.Length > 1 ? head[1] : "";

        if (key.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (key.Length == 0 || !"ABCDEFG".Contains(char.ToUpperInvariant(key[0]))) return null;

        var step = Pitch.Letters.IndexOf(char.ToUpperInvariant(key[0]));
        var at = 1;
        var alter = 0;

        while (at < key.Length && key[at] is '#' or 'b')
        {
            alter += key[at] == '#' ? 1 : -1;
            at++;
        }

        return Keys.Fifths(step, alter, (key[at..] + " " + rest).Trim());
    }

    /// <summary>
    /// What a written length suffix multiplies the unit note length by. <c>2</c> doubles, <c>/2</c>
    /// halves, <c>3/2</c> is a dotted one, a bare <c>/</c> halves once per slash. No suffix is 1.
    /// </summary>
    public static Duration Factor(string? suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return new Duration(1, 1);

        var at = 0;
        var numerator = Digits(suffix, ref at);

        var slashes = 0;
        while (at < suffix.Length && suffix[at] == '/') { slashes++; at++; }

        if (slashes == 0) return Duration.Of(numerator > 0 ? numerator : 1, 1);

        var denominator = Digits(suffix, ref at);
        if (denominator > 0) return Duration.Of(numerator > 0 ? numerator : 1, denominator);

        // Bare slashes halve once each: "A/" is a half, "A//" a quarter.
        return Duration.Of(numerator > 0 ? numerator : 1, 1L << slashes);
    }

    /// <summary>The meter a <c>M:</c> field sets — beats over a beat unit. Null for a free meter.</summary>
    public static (int Beats, int Unit)? Meter(string field)
    {
        var text = field.Trim();
        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (text is "C") return (4, 4);
        if (text is "C|") return (2, 2);

        var slash = text.IndexOf('/');
        if (slash <= 0) return null;

        // "2+3/8" and "(2+3)/8" are one bar of five eighths as far as the length of a bar goes.
        var top = new string([.. text[..slash].Where(c => char.IsAsciiDigit(c) || c == '+')]);
        var beats = top.Split('+').Select(part => int.TryParse(part, out var n) ? n : 0).Sum();

        var at = slash + 1;
        var unit = (int)Digits(text, ref at);

        return beats > 0 && unit > 0 ? (beats, unit) : null;
    }

    /// <summary>The unit note length an <c>L:</c> field sets, in quarter notes.</summary>
    public static Duration? UnitLength(string field)
    {
        var text = field.Trim();
        var slash = text.IndexOf('/');
        if (slash <= 0) return null;

        var at = 0;
        var numerator = Digits(text, ref at);
        at = slash + 1;
        var denominator = Digits(text, ref at);

        return numerator > 0 && denominator > 0 ? Duration.Of(numerator * 4, denominator) : null;
    }

    /// <summary>
    /// The unit note length a meter implies when no <c>L:</c> says otherwise: a sixteenth in anything
    /// under three quarters to the bar, an eighth above it. ABC's own rule, and the reason a tune in 2/4
    /// with no <c>L:</c> is written in sixteenths.
    /// </summary>
    public static Duration UnitFor(int beats, int unit) =>
        unit != 0 && (double)beats / unit < 0.75 ? new Duration(1, 4) : new Duration(1, 2);

    private static long Digits(string text, ref int at)
    {
        long value = 0;
        var any = false;
        while (at < text.Length && char.IsAsciiDigit(text[at])) { value = (value * 10) + (text[at] - '0'); any = true; at++; }
        return any ? value : 0;
    }
}
