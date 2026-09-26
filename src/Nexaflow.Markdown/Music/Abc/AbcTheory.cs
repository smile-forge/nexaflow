using Nexaflow.Markdown.Ast;

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
/// What ABC's own spellings mean: what a length suffix multiplies the unit by, and the unit a meter implies.
/// <para>
/// Deliberately small and WPF-free. The theory under the spellings — what a key alters, how a pitch is
/// moved — is <see cref="Keys"/> and <see cref="Pitch"/>, which every notation shares; how wide a note
/// is, where its stem goes and whether it beams are the builder's, and depend on the page.
/// </para>
/// </summary>
public static class AbcTheory
{
    /// <summary>
    /// What a written length suffix multiplies the unit note length by. <c>2</c> doubles, <c>/2</c>
    /// halves, <c>3/2</c> is a dotted one, a bare <c>/</c> halves once per slash. No suffix is 1.
    /// </summary>
    public static Duration Factor(ContentNode? length)
    {
        if (length is null) return new Duration(1, 1);

        long numerator = 0, denominator = 0;
        var slashes = 0;

        foreach (var piece in length.Children)
        {
            if (piece.Kind != AbcKinds.Number) { slashes++; continue; }
            if (!long.TryParse(piece.Text, out var number)) continue;

            if (slashes == 0) numerator = number;
            else denominator = number;
        }

        if (slashes == 0) return Duration.Of(numerator > 0 ? numerator : 1, 1);
        if (denominator > 0) return Duration.Of(numerator > 0 ? numerator : 1, denominator);

        // Bare slashes halve once each: "A/" is a half, "A//" a quarter.
        return Duration.Of(numerator > 0 ? numerator : 1, 1L << slashes);
    }

    /// <summary>
    /// The unit note length a meter implies when no <c>L:</c> says otherwise: a sixteenth in anything
    /// under three quarters to the bar, an eighth above it. ABC's own rule, and the reason a tune in 2/4
    /// with no <c>L:</c> is written in sixteenths.
    /// </summary>
    public static Duration UnitFor(int beats, int unit) =>
        unit != 0 && (double)beats / unit < 0.75 ? new Duration(1, 4) : new Duration(1, 2);
}
