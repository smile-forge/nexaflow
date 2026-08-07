using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.WindowsFileSystem.ViewModels;

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
    public long     SizeBytes   { get; init; }
    public DateTime Modified    { get; init; }

    /// <summary>A top-level row of "This PC" — a physical drive, or a location contributed by an
    /// <c>IThisPcItemProvider</c>. Not "a Windows volume": the whole feature keys the This PC
    /// presentation (type/size columns, the drive-only context menu) off this flag.</summary>
    public bool IsThisPcItem { get; init; }

    /// <summary>Set when this row came from an <c>IThisPcItemProvider</c> — the item's id. Null for a
    /// physical drive, so the two kinds of top-level row can still be told apart.</summary>
    public string? ProviderId { get; init; }

    /// <summary>Readiness state — only meaningful for <see cref="IsThisPcItem"/> entries.</summary>
    public DriveStatus DriveStatus
    {
        get => _driveStatus;
        set { _driveStatus = value; OnPropertyChanged(); }
    }

    /// <summary>Visual icon variant — only meaningful for <see cref="IsThisPcItem"/> entries.</summary>
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

    public string TypeLabel => IsThisPcItem
        ? _driveKindLabel
        : IsDirectory ? "Folder" : Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();

    public string SizeLabel
    {
        get
        {
            if (IsThisPcItem)
            {
                if (_driveTotalBytes <= 0) return string.Empty;
                double pct = (double)_driveUsedBytes / _driveTotalBytes * 100.0;
                return $"{FormatSize(_driveUsedBytes)} used ({pct:F0}%)";
            }
            return IsDirectory ? string.Empty : FormatSize(SizeBytes);
        }
    }

    public string ModifiedLabel => IsThisPcItem || Modified == DateTime.MinValue
        ? string.Empty
        : Modified.ToString("yyyy-MM-dd HH:mm");

    private static string FormatSize(long bytes) => SizeFormatter.FormatBytes(bytes);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
