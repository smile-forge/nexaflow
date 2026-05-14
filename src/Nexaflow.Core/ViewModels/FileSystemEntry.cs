using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Nexaflow.Core.ViewModels;

// ── File / folder entry shown in the right panel ──────────────────────────────

public class FileSystemEntry : INotifyPropertyChanged
{
    private DriveStatus _driveStatus;

    public string   Name        { get; set; } = string.Empty;
    public string   FullPath    { get; init; } = string.Empty;
    public bool     IsDirectory { get; init; }
    public bool     IsDrive     { get; init; }
    public long     SizeBytes   { get; init; }
    public DateTime Modified    { get; init; }

    /// <summary>Drive readiness state — only meaningful for IsDrive entries.</summary>
    public DriveStatus DriveStatus
    {
        get => _driveStatus;
        set { _driveStatus = value; OnPropertyChanged(); }
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
