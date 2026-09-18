using System.Globalization;

namespace Nexaflow.Markdown.Plot;

/// <summary>
/// What a cell of a table reads as.
///
/// <para>
/// One place, because two questions turn on it and have to agree: whether a row names the columns — a
/// header being a row no cell of which is a number — and what a mark is drawn from. Two readings of
/// "is this a number" would eventually draw a header as a point.
/// </para>
/// </summary>
public static class PlotNumber
{
    /// <summary>The number <paramref name="text"/> says, or null where it says none.</summary>
    public static double? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var read)
               && !double.IsNaN(read) && !double.IsInfinity(read)
            ? read
            : null;
    }

    /// <summary>A number written out so it reads back as itself, which is how a fact carries one.</summary>
    public static string Written(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
