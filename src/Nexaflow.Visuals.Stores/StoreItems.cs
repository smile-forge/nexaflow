using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Icons;
using Nexaflow.IO.Common.Stores;
using Nexaflow.Visuals.Common.Behaviors;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Visuals.Stores;

/// <summary>A file or folder in the folder open; <see cref="IsSelected"/> follows the list's selection.</summary>
public sealed partial class StoreRow(StoreEntry entry, string path) : ObservableObject, IClipboardText
{
    public StoreEntry Entry { get; } = entry;

    /// <summary>Where it is, as Copy path gives it: a path on a disk, the way down on a phone.</summary>
    public string Path { get; } = path;

    public string ClipboardText => Path;
    public string Name => Entry.Name;
    public bool IsFolder => Entry.IsFolder;
    public long Size => Entry.Size;
    public DateTime? Modified => Entry.Modified;
    public IconRef Icon => IconRef.Fluent(IsFolder ? "folder" : "document");
    public string SizeText => IsFolder ? "" : SizeFormatter.FormatBytes(Size);
    public string ModifiedText => Modified?.ToString("yyyy-MM-dd HH:mm") ?? "";

    [ObservableProperty] private bool _isSelected;
}

/// <summary>One step of the path bar: a root, then each folder down to the one open.</summary>
public sealed record StoreCrumb(string Label, IReadOnlyList<StoreEntry> Trail, string Path) : IClipboardText
{
    public string ClipboardText => Path;
}

/// <summary>The columns the list sorts by.</summary>
public enum StoreSortKey { Name, Size, Modified }

