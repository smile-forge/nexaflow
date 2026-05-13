using System.IO;

namespace Nexaflow.Core.ViewModels;

// ── File / folder entry shown in the right panel ──────────────────────────────

public class FileSystemEntry
{
    public string   Name        { get; init; } = string.Empty;
    public string   FullPath    { get; init; } = string.Empty;
    public bool     IsDirectory { get; init; }
    public bool     IsDrive     { get; init; }
    public long     SizeBytes   { get; init; }
    public DateTime Modified    { get; init; }

    public string TypeLabel => IsDirectory ? "Folder" : Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();
    public string SizeLabel => IsDirectory ? string.Empty : FormatSize(SizeBytes);
    public string ModifiedLabel => Modified.ToString("yyyy-MM-dd HH:mm");

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024          => $"{bytes} B",
        < 1024 * 1024   => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _               => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
