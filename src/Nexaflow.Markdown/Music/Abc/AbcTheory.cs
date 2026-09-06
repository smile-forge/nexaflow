namespace Nexaflow.Markdown.Music.Abc;

/// <summary>
/// A sounded pitch: which letter, how it is altered, and which octave.
/// </summary>
/// <param name="Step">0-6 for C D E F G A B.</param>
/// <param name="Alter">Semitones away from the natural, -2 to 2.</param>
/// <param name="Octave">Scientific octave numbering — middle C is octave 4.</param>
public readonly record struct AbcPitch(int Step, int Alter, int Octave)
{
    /// <summary>Where it sits on the staff, counting lines and spaces from C0. What decides its height.</summary>
    public int DiatonicIndex => (this.Octave * 7) + this.Step;

    /// <summary>Written as a fact for the tree to carry: <c>step/alter/octave</c>.</summary>
    public override string ToString() => $"{this.Step}/{this.Alter}/{this.Octave}";

    /// <summary>Reads back what <see cref="ToString"/> wrote.</summary>
    public static AbcPitch Parse(string text)
    {
        var parts = text.Split('/');
        return parts.Length == 3
               && int.TryParse(parts[0], out var step)
               && int.TryParse(parts[1], out var alter)
               && int.TryParse(parts[2], out var octave)
            ? new AbcPitch(step, alter, octave)
            : default;
    }
}

/// <summary>
/// How long an event lasts, as a multiple of a quarter note.
/// <para>
/// A rational, kept as a pair rather than as a double, because a triplet in 6/8 is a third of a
/// three-eighths beat and the bar has to add up exactly. Doubles that nearly add up are how a bar comes
/// out one thirty-second short and nothing says why.
/// </para>
/// </summary>
public readonly record struct AbcLength(long Numerator, long Denominator)
{
    public static readonly AbcLength Zero = new(0, 1);
    public static readonly AbcLength Quarter = new(1, 1);
    public static readonly AbcLength Whole = new(4, 1);

    /// <summary>In quarter notes, for anything that only needs to compare or space.</summary>
    public double Quarters => this.Denominator == 0 ? 0 : (double)this.Numerator / this.Denominator;

    public static AbcLength Of(long numerator, long denominator)
    {
        if (denominator == 0) return Zero;
        if (denominator < 0) { numerator = -numerator; denominator = -denominator; }

        var divisor = Gcd(Math.Abs(numerator), denominator);
        return divisor == 0 ? Zero : new AbcLength(numerator / divisor, denominator / divisor);
    }

    public static AbcLength operator *(AbcLength a, AbcLength b) =>
        Of(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    public static AbcLength operator +(AbcLength a, AbcLength b) =>
        Of((a.Numerator * b.Denominator) + (b.Numerator * a.Denominator), a.Denominator * b.Denominator);

    public static bool operator <(AbcLength a, AbcLength b) =>
        a.Numerator * b.Denominator < b.Numerator * a.Denominator;

    public static bool operator >(AbcLength a, AbcLength b) =>
        a.Numerator * b.Denominator > b.Numerator * a.Denominator;

    /// <summary>Written as a fact for the tree to carry: <c>numerator/denominator</c>, in quarter notes.</summary>
    public override string ToString() => $"{this.Numerator}/{this.Denominator}";

    /// <summary>Reads back what <see cref="ToString"/> wrote.</summary>
    public static AbcLength Parse(string text)
    {
        var parts = text.Split('/');
        return parts.Length == 2 && long.TryParse(parts[0], out var n) && long.TryParse(parts[1], out var d)
            ? Of(n, d)
            : Zero;
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}

/// <summary>
/// What is in force where a note is written: the key, the meter, and the length one letter stands for.
/// <para>
/// None of it is written on the note. All of it is written above it, possibly many lines above it,
/// possibly changed halfway down — so it is worked out once by walking the tune in order and hung on each
/// line as a fact, rather than looked up again by everything that needs it.
/// </para>
/// </summary>
public readonly record struct AbcContext(int Fifths, int Beats, int BeatUnit, AbcLength Unit, string Voice)
{
    /// <summary>What a tune with no header at all is read as: C major, 4/4, eighth notes.</summary>
    public static readonly AbcContext Default = new(0, 4, 4, new AbcLength(1, 2), "");

    /// <summary>How long one bar is.</summary>
    public AbcLength Bar => AbcLength.Of(this.Beats * 4, this.BeatUnit);
}

/// <summary>
/// The bits of music theory ABC needs read: what a key signature alters, and what a length suffix means.
/// <para>
/// Deliberately small and WPF-free. It answers questions about notation, never about drawing — how wide a
/// note is, where its stem goes and whether it beams are the builder's, and depend on the page.
/// </para>
/// </summary>
public static class AbcTheory
{
    /// <summary>The note letters in staff order, from C.</summary>
    public const string StepLetters = "CDEFGAB";

    /// <summary>The order sharps are added to a key signature: F C G D A E B.</summary>
    private static readonly int[] SharpOrder = [3, 0, 4, 1, 5, 2, 6];

    /// <summary>The order flats are added: B E A D G C F.</summary>
    private static readonly int[] FlatOrder = [6, 2, 5, 1, 4, 0, 3];

    /// <summary>How the key signature alters this step: +1 for a sharp, -1 for a flat, 0 for neither.</summary>
    public static int KeyAlterFor(int step, int fifths)
    {
        if (fifths > 0)
            for (var i = 0; i < Math.Min(fifths, 7); i++)
                if (SharpOrder[i] == step) return 1;

        if (fifths < 0)
            for (var i = 0; i < Math.Min(-fifths, 7); i++)
                if (FlatOrder[i] == step) return -1;

        return 0;
    }

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

        var step = StepLetters.IndexOf(char.ToUpperInvariant(key[0]));
        var at = 1;
        var fifths = Natural[step];

        while (at < key.Length && key[at] is '#' or 'b')
        {
            fifths += key[at] == '#' ? 7 : -7;
            at++;
        }

        var mode = (key[at..] + " " + rest).Trim();
        return fifths + ModeShift(mode);
    }

    /// <summary>Where each natural key sits on the circle of fifths: C=0, G=1, D=2, A=3, E=4, B=5, F=-1.</summary>
    private static readonly int[] Natural = [0, 2, 4, -1, 1, 3, 5];

    /// <summary>
    /// How far a mode moves a key off its major. Named in full or by the first three letters, in any
    /// case, which is what ABC allows and what real tunebooks use.
    /// </summary>
    private static int ModeShift(string mode)
    {
        var name = new string([.. mode.Where(char.IsLetter)]).ToLowerInvariant();
        if (name.Length == 0) return 0;

        // "maj" and "min" are also written "m" on its own, which is the commonest of all.
        if (name is "m") return -3;

        var head = name.Length >= 3 ? name[..3] : name;
        return head switch
        {
            "maj" or "ion" => 0,
            "min" or "aeo" => -3,
            "dor" => -2,
            "phr" => -4,
            "lyd" => 1,
            "mix" => -1,
            "loc" => -5,
            _ => 0,
        };
    }

    /// <summary>
    /// What a written length suffix multiplies the unit note length by. <c>2</c> doubles, <c>/2</c>
    /// halves, <c>3/2</c> is a dotted one, a bare <c>/</c> halves once per slash. No suffix is 1.
    /// </summary>
    public static AbcLength Factor(string? suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return new AbcLength(1, 1);

        var at = 0;
        var numerator = Digits(suffix, ref at);

        var slashes = 0;
        while (at < suffix.Length && suffix[at] == '/') { slashes++; at++; }

        if (slashes == 0) return AbcLength.Of(numerator > 0 ? numerator : 1, 1);

        var denominator = Digits(suffix, ref at);
        if (denominator > 0) return AbcLength.Of(numerator > 0 ? numerator : 1, denominator);

        // Bare slashes halve once each: "A/" is a half, "A//" a quarter.
        return AbcLength.Of(numerator > 0 ? numerator : 1, 1L << slashes);
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
    public static AbcLength? UnitLength(string field)
    {
        var text = field.Trim();
        var slash = text.IndexOf('/');
        if (slash <= 0) return null;

        var at = 0;
        var numerator = Digits(text, ref at);
        at = slash + 1;
        var denominator = Digits(text, ref at);

        return numerator > 0 && denominator > 0 ? AbcLength.Of(numerator * 4, denominator) : null;
    }

    /// <summary>
    /// The unit note length a meter implies when no <c>L:</c> says otherwise: a sixteenth in anything
    /// under three quarters to the bar, an eighth above it. ABC's own rule, and the reason a tune in 2/4
    /// with no <c>L:</c> is written in sixteenths.
    /// </summary>
    public static AbcLength UnitFor(int beats, int unit) =>
        unit != 0 && (double)beats / unit < 0.75 ? new AbcLength(1, 4) : new AbcLength(1, 2);

    private static long Digits(string text, ref int at)
    {
        long value = 0;
        var any = false;
        while (at < text.Length && char.IsAsciiDigit(text[at])) { value = (value * 10) + (text[at] - '0'); any = true; at++; }
        return any ? value : 0;
    }
}
