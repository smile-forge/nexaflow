using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Nexaflow.Core.ViewModels;

// ── File / folder entry shown in the right panel ──────────────────────────────

public class FileSystemEntry : INotifyPropertyChanged
{
    private DriveStatus   _driveStatus;
    private DriveIconType _driveIconType = DriveIconType.HDD;
    private long          _driveUsedBytes;
    private long          _driveTotalBytes;
    private string        _fileSystem     = string.Empty;
    private string        _driveKindLabel = string.Empty;

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

    /// <summary>Visual icon variant — only meaningful for IsDrive entries.</summary>
    public DriveIconType DriveIconType
    {
        get => _driveIconType;
        set { _driveIconType = value; OnPropertyChanged(); }
    }

    /// <summary>Bytes actually used on the drive (TotalSize − TotalFreeSpace).</summary>
    public long DriveUsedBytes
    {
        get => _driveUsedBytes;
        set { _driveUsedBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeLabel)); }
    }

    /// <summary>Total capacity of the drive in bytes.</summary>
    public long DriveTotalBytes
    {
        get => _driveTotalBytes;
        set { _driveTotalBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeLabel)); }
    }

    /// <summary>Filesystem format string, e.g. "NTFS", "FAT32", "exFAT".</summary>
    public string FileSystem
    {
        get => _fileSystem;
        set { _fileSystem = value; OnPropertyChanged(); }
    }

    /// <summary>Human-readable drive kind, e.g. "Local Disk", "Network Drive".</summary>
    public string DriveKindLabel
    {
        get => _driveKindLabel;
        set { _driveKindLabel = value; OnPropertyChanged(); OnPropertyChanged(nameof(TypeLabel)); }
    }

    // ── Computed display properties ────────────────────────────────────────────

    public string TypeLabel => IsDrive
        ? _driveKindLabel
        : IsDirectory ? "Folder" : Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();

    public string SizeLabel
    {
        get
        {
            if (IsDrive)
            {
                if (_driveTotalBytes <= 0) return string.Empty;
                double pct = (double)_driveUsedBytes / _driveTotalBytes * 100.0;
                return $"{FormatSize(_driveUsedBytes)} used ({pct:F0}%)";
            }
            return IsDirectory ? string.Empty : FormatSize(SizeBytes);
        }
    }

    public string ModifiedLabel => IsDrive || Modified == DateTime.MinValue
        ? string.Empty
        : Modified.ToString("yyyy-MM-dd HH:mm");

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024                            => $"{bytes} B",
        < 1024 * 1024                     => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024             => $"{bytes / (1024.0 * 1024):F1} MB",
        < 1024L * 1024 * 1024 * 1024      => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        _                                 => $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
