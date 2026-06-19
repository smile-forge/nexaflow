namespace Nexaflow.Visuals.Common.Formatting;

/// <summary>
/// Formats byte counts as compact human-readable sizes (e.g. <c>1.4 MB</c>). The shared home for the
/// 1024-scaling loop that several features each used to re-implement.
/// </summary>
public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>
    /// Formats <paramref name="bytes"/>; a non-positive value renders as <c>0 B</c>.
    /// <paramref name="decimals"/> caps the fraction digits used at KB and above (trailing zeros dropped).
    /// </summary>
    public static string FormatBytes(long bytes, int decimals = 1)
        => Format(bytes <= 0 ? 0d : bytes, decimals);

    /// <summary>Unsigned overload; defaults to two fraction digits to match the system dashboard's look.</summary>
    public static string FormatBytes(ulong bytes, int decimals = 2)
        => Format(bytes, decimals);

    private static string Format(double value, int decimals)
    {
        var u = 0;
        while (value >= 1024 && u < Units.Length - 1) { value /= 1024; u++; }
        var fmt = u == 0 ? "0" : "0." + new string('#', System.Math.Max(1, decimals));
        return $"{value.ToString(fmt)} {Units[u]}";
    }
}
