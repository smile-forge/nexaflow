using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Features.UsbProbe.StorageTools;

/// <summary>
/// A volume or folder in the storage tool's folder tree. Its subfolders are read the first time it is opened, off the UI
/// thread; until then a folder that has any shows a placeholder child, so the tree draws its expander.
/// </summary>
public sealed partial class UsbFolderNode : ObservableObject, IClipboardText
{
    readonly Action<UsbFolderNode> _selected;
    Task? _loading;

    UsbFolderNode(Action<UsbFolderNode> selected)
    {
        _selected = selected;
        Name = FullPath = "";
        IsPlaceholder = true;
    }

    UsbFolderNode(string name, string fullPath, UsbVolumeRow? volume, bool hasFolders, Action<UsbFolderNode> selected)
    {
        _selected = selected;
        Name = name;
        FullPath = fullPath;
        Volume = volume;
        if (hasFolders) Children.Add(new UsbFolderNode(selected));
    }

    /// <summary>A volume's root: the top of one branch of the tree.</summary>
    public static UsbFolderNode ForVolume(UsbVolumeRow volume, Action<UsbFolderNode> selected) =>
        new(volume.Name, volume.Root, volume, hasFolders: true, selected);

    public string Name { get; }
    public string FullPath { get; }

    /// <summary>The volume this node is the root of; null for a folder.</summary>
    public UsbVolumeRow? Volume { get; private set; }

    public bool IsVolume => Volume is not null;
    public bool IsPlaceholder { get; }
    public bool IsLoaded => _loading is { IsCompleted: true };
    public IconRef Icon => IsVolume ? IconRef.Fluent("usb_stick") : IconRef.Fluent(IsExpanded ? "folder_open" : "folder");
    public string ClipboardText => FullPath;

    public ObservableCollection<UsbFolderNode> Children { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Icon))] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) _ = LoadAsync();
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !IsPlaceholder) _selected(this);
    }

    /// <summary>Reads the subfolders once; every caller waits on the same read.</summary>
    public Task LoadAsync() => _loading ??= ReadAsync();

    /// <summary>Reads the subfolders again where they have been read, keeping what was open open.</summary>
    public async Task ReloadAsync()
    {
        if (_loading is null) return;
        await _loading;
        _loading = ReadAsync();
        await _loading;
        foreach (var child in Children.Where(c => c.IsExpanded)) await child.ReloadAsync();
    }

    internal void Update(UsbVolumeRow volume) => Volume = volume;

    /// <summary>The child folder at <paramref name="path"/>, once read.</summary>
    public UsbFolderNode? ChildAt(string path) =>
        Children.FirstOrDefault(c => !c.IsPlaceholder && UsbStoragePaths.Same(c.FullPath, path));

    async Task ReadAsync()
    {
        List<(string Name, string Path, bool HasFolders)> read;
        try { read = await Task.Run(() => Subfolders(FullPath)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { read = []; }

        var kept = Children.Where(c => !c.IsPlaceholder).ToDictionary(c => c.FullPath, StringComparer.OrdinalIgnoreCase);
        Children.Clear();
        foreach (var (name, path, hasFolders) in read)
            Children.Add(kept.TryGetValue(path, out var existing) ? existing : new UsbFolderNode(name, path, null, hasFolders, _selected));
    }

    /// <summary>The folders a person would see in a folder: hidden system folders (System Volume Information) left out.</summary>
    static List<(string, string, bool)> Subfolders(string path) =>
        [.. new DirectoryInfo(path).EnumerateDirectories()
            .Where(UsbStoragePaths.IsShown)
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => (d.Name, d.FullName, HasFolders(d)))];

    static bool HasFolders(DirectoryInfo folder)
    {
        try { return folder.EnumerateDirectories().Any(UsbStoragePaths.IsShown); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}

/// <summary>Path rules the browser and the tool share.</summary>
internal static class UsbStoragePaths
{
    const FileAttributes HiddenSystem = FileAttributes.Hidden | FileAttributes.System;

    public static bool IsShown(FileSystemInfo info) => (info.Attributes & HiddenSystem) != HiddenSystem;

    public static string Trimmed(string path) => path.Length > 3 ? path.TrimEnd('\\') : path;

    public static bool Same(string a, string b) => string.Equals(Trimmed(a), Trimmed(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or somewhere inside it.</summary>
    public static bool Within(string path, string folder)
    {
        string p = Trimmed(path), f = Trimmed(folder).TrimEnd('\\');
        return p.Equals(f, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
