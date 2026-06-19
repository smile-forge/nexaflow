using Nexaflow.Visuals.Common.Formatting;

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

    public string SizeDisplay => SizeBytes is > 0 ? SizeFormatter.FormatBytes(SizeBytes.Value) : string.Empty;

    public string ModifiedDisplay => Modified?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
}
