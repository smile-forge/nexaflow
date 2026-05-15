namespace Nexaflow.Features.WindowsSearch;

/// <summary>One row returned from the Windows Search index.</summary>
public sealed class SearchResultEntry
{
    public required string    FilePath  { get; init; }
    public required string    FileName  { get; init; }
    /// <summary>Directory relative to the search root.</summary>
    public required string    Directory { get; init; }
    public          long?     SizeBytes { get; init; }
    public          DateTime? Modified  { get; init; }
    public          string    Kind      { get; init; } = string.Empty;

    public bool IsFolder => Kind.Contains("folder", StringComparison.OrdinalIgnoreCase);

    public string SizeDisplay => SizeBytes switch
    {
        null or 0             => string.Empty,
        < 1024                => $"{SizeBytes} B",
        < 1024 * 1024         => $"{SizeBytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):F1} MB",
        _                     => $"{SizeBytes / (1024.0 * 1024 * 1024):F1} GB"
    };

    public string ModifiedDisplay => Modified?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
}
