using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common.Cloud;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Shell;

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
    private CloudFileState _cloudState;
    private long          _statusRevision;

    public string   Name        { get; set; } = string.Empty;
    public string   FullPath    { get; init; } = string.Empty;
    public bool     IsDirectory { get; init; }
    public long     SizeBytes   { get => _sizeBytes; init => _sizeBytes = value; }
    public DateTime Modified    { get => _modified;  init => _modified = value; }

    /// <summary>A top-level row of "This PC" — a physical drive, or a location contributed by an
    /// <c>IThisPcItemProvider</c>. Not "a Windows volume": the whole feature keys the This PC
    /// presentation (type/size columns, the drive-only context menu) off this flag.</summary>
    public bool IsThisPcItem { get; init; }

    /// <summary>A row inside an archive or another container the folder view opens: not on disk, so its icon is read
    /// from its name alone.</summary>
    public bool InContainer { get; init; }

    /// <summary>Where the row stands with its cloud sync provider, as the folder's listing said; None outside a synced folder.</summary>
    public CloudFileState CloudState { get => _cloudState; init => _cloudState = value; }

    /// <summary>Moved on when the shell says the file changed in a way its listing may not show, so its provider is asked again.</summary>
    public long StatusRevision => _statusRevision;

    /// <summary>Has the row's sync status asked of its provider again, the next time it is drawn.</summary>
    public void RecheckStatus()
    {
        _statusRevision++;
        OnPropertyChanged(nameof(StatusRevision));
    }

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

    /// <summary>
    /// The shell's name for what this is ("Text Document", "File folder"), as Explorer's Type column shows it. The enumerator
    /// sets it off the UI thread; a row built anywhere else reads it from the shell the first time it is wanted.
    /// </summary>
    public string TypeName
    {
        get => _typeName ??= ShellFileTypes.NameFor(IsDirectory ? string.Empty : Path.GetExtension(Name), IsDirectory);
        init => _typeName = value;
    }
    private string? _typeName;

    public string TypeLabel => IsThisPcItem ? _driveKindLabel : TypeName;

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
    /// case keeps the path), size, time, companion and cloud sync state, telling the view about each that changed. False where it is no longer
    /// the same kind of thing, a folder where there was a file or a different row of This PC, and cannot stand for it.
    /// </summary>
    public bool TakeOn(FileSystemEntry fresh)
    {
        if (fresh.IsDirectory != IsDirectory || fresh.IsThisPcItem != IsThisPcItem || fresh.ProviderId != ProviderId) return false;

        if (fresh.Name != Name)
        {
            Name      = fresh.Name;
            _typeName = fresh._typeName;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(TypeName));
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
        if (fresh._cloudState != _cloudState)
        {
            _cloudState = fresh._cloudState;
            OnPropertyChanged(nameof(CloudState));
        }

        return true;
    }

    private static string FormatSize(long bytes) => SizeFormatter.FormatBytes(bytes);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
