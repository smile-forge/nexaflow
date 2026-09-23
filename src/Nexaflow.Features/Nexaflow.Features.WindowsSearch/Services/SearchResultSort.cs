namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// The results grid's column sorting, in terms the view can apply: which property a clicked column sorts
/// by, and which way round the next click should go.
/// <para>
/// Pure because the column→property map is the part that breaks silently — rename a column id in the view
/// and clicking it simply stops sorting, with nothing to see. The arrow is baked into the header text, so
/// swapping it between columns means stripping it again; that round-trip is here too.
/// </para>
/// </summary>
public static class SearchResultSort
{
    public const string Ascending = "  ↑";
    public const string Descending = "  ↓";

    /// <summary>The result property a column id sorts by, or null when the column is not sortable.</summary>
    public static string? PropertyFor(string? header) => Strip(header) switch
    {
        "Name"     => "FileName",
        "Location" => "Directory",
        "Size"     => "SizeBytes",
        "Modified" => "Modified",
        _          => null,
    };

    /// <summary>A header's own text, with any sort arrow removed.</summary>
    public static string Strip(string? header) => header?.TrimEnd(' ', '↑', '↓') ?? string.Empty;

    /// <summary>The header text with the arrow for <paramref name="ascending"/> appended.</summary>
    public static string WithArrow(string? header, bool ascending) =>
        Strip(header) + (ascending ? Ascending : Descending);

    /// <summary>
    /// Which way the next click sorts: clicking a new column starts ascending, and clicking the column
    /// already sorted ascending flips it.
    /// </summary>
    public static bool NextAscending(bool isSameHeaderAsLast, bool lastWasAscending) =>
        !(isSameHeaderAsLast && lastWasAscending);
}
