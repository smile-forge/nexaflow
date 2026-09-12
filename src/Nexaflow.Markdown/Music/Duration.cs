namespace Nexaflow.Markdown.Music;

/// <summary>
/// How long something lasts, as a multiple of a quarter note.
/// <para>
/// A rational, kept as a pair rather than as a double, because a triplet in 6/8 is a third of a
/// three-eighths beat and the bar has to add up exactly. Doubles that nearly add up are how a bar comes
/// out one thirty-second short and nothing says why.
/// </para>
/// </summary>
public readonly record struct Duration(long Numerator, long Denominator)
{
    public static readonly Duration Zero = new(0, 1);
    public static readonly Duration Quarter = new(1, 1);
    public static readonly Duration Whole = new(4, 1);

    /// <summary>In quarter notes, for anything that only needs to compare or space.</summary>
    public double Quarters => this.Denominator == 0 ? 0 : (double)this.Numerator / this.Denominator;

    public static Duration Of(long numerator, long denominator)
    {
        if (denominator == 0) return Zero;
        if (denominator < 0) { numerator = -numerator; denominator = -denominator; }

        var divisor = Gcd(Math.Abs(numerator), denominator);
        return divisor == 0 ? Zero : new Duration(numerator / divisor, denominator / divisor);
    }

    public static Duration operator *(Duration a, Duration b) =>
        Of(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    public static Duration operator +(Duration a, Duration b) =>
        Of((a.Numerator * b.Denominator) + (b.Numerator * a.Denominator), a.Denominator * b.Denominator);

    public static Duration operator -(Duration a, Duration b) =>
        Of((a.Numerator * b.Denominator) - (b.Numerator * a.Denominator), a.Denominator * b.Denominator);

    public static bool operator <(Duration a, Duration b) =>
        a.Numerator * b.Denominator < b.Numerator * a.Denominator;

    public static bool operator >(Duration a, Duration b) =>
        a.Numerator * b.Denominator > b.Numerator * a.Denominator;

    public static bool operator <=(Duration a, Duration b) => !(a > b);

    public static bool operator >=(Duration a, Duration b) => !(a < b);

    /// <summary>Written as a fact for the tree to carry: <c>numerator/denominator</c>, in quarter notes.</summary>
    public override string ToString() => $"{this.Numerator}/{this.Denominator}";

    /// <summary>Reads back what <see cref="ToString"/> wrote.</summary>
    public static Duration Parse(string text)
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
