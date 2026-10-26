namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Where two writings of one document part ways: from <see cref="Start"/>, <see cref="Removed"/> characters of the first stand
/// where <see cref="Added"/> characters of the second do, and everything either side of that is the same in both. It is the one
/// stretch an edit made, whoever made it — what a step taken back is judged by, what a host showing a document twice writes
/// into its other half, and how a place in the first writing is found again in the second.
/// </summary>
public readonly record struct SourceDifference(int Start, int Removed, int Added)
{
    /// <summary>Whether the two writings are the same.</summary>
    public bool None => Removed == 0 && Added == 0;

    /// <summary>The stretch <paramref name="before"/> and <paramref name="after"/> differ by.</summary>
    public static SourceDifference Between(string before, string after)
    {
        var start = before.AsSpan().CommonPrefixLength(after);
        var room = Math.Min(before.Length, after.Length) - start;

        var same = 0;
        while (same < room && before[before.Length - 1 - same] == after[after.Length - 1 - same]) same++;

        return new(start, before.Length - start - same, after.Length - start - same);
    }

    /// <summary>
    /// Where the character at <paramref name="offset"/> in the first writing stands in the second: where it was, before the
    /// stretch; moved by what the stretch grew or shrank by, after it; and at the stretch's start, where it was written over.
    /// </summary>
    public int Moved(int offset) =>
        offset < Start ? offset : offset >= Start + Removed ? offset + Added - Removed : Start;
}
