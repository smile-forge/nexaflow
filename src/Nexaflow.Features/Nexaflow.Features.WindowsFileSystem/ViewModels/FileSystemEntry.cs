using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

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
    private FileCompanionKind? _companion;

    private long          _sizeBytes;
    private DateTime      _modified;

    public string   Name        { get; set; } = string.Empty;
    public string   FullPath    { get; init; } = string.Empty;
    public bool     IsDirectory { get; init; }
    public long     SizeBytes   { get => _sizeBytes; init => _sizeBytes = value; }
    public DateTime Modified    { get => _modified;  init => _modified = value; }

    /// <summary>A top-level row of "This PC" — a physical drive, or a location contributed by an
    /// <c>IThisPcItemProvider</c>. Not "a Windows volume": the whole feature keys the This PC
    /// presentation (type/size columns, the drive-only context menu) off this flag.</summary>
    public bool IsThisPcItem { get; init; }

    /// <summary>Set when this row came from an <c>IThisPcItemProvider</c> — the item's id. Null for a
    /// physical drive, so the two kinds of top-level row can still be told apart.</summary>
    public string? ProviderId { get; init; }

    /// <summary>
    /// The companion beside this file whose badge it shows — an edited photo's recipe — or null. Set as the folder
    /// finishes listing, which can be after the row is on screen, so it notifies.
    /// </summary>
    public FileCompanionKind? Companion
    {
        get => _companion;
        set { _companion = value; OnPropertyChanged(); }
    }

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
        : IsDirectory ? Str.Get("WindowsFileSystem.Entry.Folder") : Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();

    public string SizeLabel
    {
        get
        {
            if (IsThisPcItem)
            {
                if (_driveTotalBytes <= 0) return string.Empty;
                double pct = (double)_driveUsedBytes / _driveTotalBytes * 100.0;
                return Str.Format("WindowsFileSystem.Entry.DriveUsedFormat", FormatSize(_driveUsedBytes), pct);
            }
            return IsDirectory ? string.Empty : FormatSize(SizeBytes);
        }
    }

    public string ModifiedLabel => IsThisPcItem || Modified == DateTime.MinValue
        ? string.Empty
        : Modified.ToString("yyyy-MM-dd HH:mm");

    /// <summary>
    /// Takes on what <paramref name="fresh"/> — a new reading of the same path — says: its name (a rename that only changed
    /// case keeps the path), size, time and companion, telling the view about each that changed. False where it is no longer
    /// the same kind of thing, a folder where there was a file or a different row of This PC, and cannot stand for it.
    /// </summary>
    public bool TakeOn(FileSystemEntry fresh)
    {
        if (fresh.IsDirectory != IsDirectory || fresh.IsThisPcItem != IsThisPcItem || fresh.ProviderId != ProviderId) return false;

        if (fresh.Name != Name)
        {
            Name = fresh.Name;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(TypeLabel));
        }
        if (fresh.SizeBytes != _sizeBytes)
        {
            _sizeBytes = fresh.SizeBytes;
            OnPropertyChanged(nameof(SizeBytes));
            OnPropertyChanged(nameof(SizeLabel));
        }
        if (fresh.Modified != _modified)
        {
            _modified = fresh.Modified;
            OnPropertyChanged(nameof(Modified));
            OnPropertyChanged(nameof(ModifiedLabel));
        }
        if (fresh.Companion != Companion) Companion = fresh.Companion;

        return true;
    }

    private static string FormatSize(long bytes) => SizeFormatter.FormatBytes(bytes);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
