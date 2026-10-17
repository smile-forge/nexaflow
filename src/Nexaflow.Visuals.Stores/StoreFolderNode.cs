using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Icons;
using Nexaflow.IO.Common.Stores;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Visuals.Stores;

/// <summary>
/// A root or folder in a store browser's tree. What is inside is read the first time it is opened; until then it shows
/// an expander, which goes once it turns out to hold no folders — on a phone, asking each folder whether it has any
/// would be a round trip per folder.
/// </summary>
public sealed partial class StoreFolderNode : ObservableObject, IClipboardText
{
    readonly StoreBrowser? _browser;
    Task? _loading;

    StoreFolderNode()
    {
        Trail = [];
        Path = "";
    }

    internal StoreFolderNode(StoreBrowser browser, IReadOnlyList<StoreEntry> trail, StoreRoot? root)
    {
        _browser = browser;
        Trail = trail;
        Root = root;
        Path = browser.PathOf(trail);
        Children.Add(new StoreFolderNode());
    }

    /// <summary>The way down to this folder from its root, the root first and this folder last.</summary>
    public IReadOnlyList<StoreEntry> Trail { get; }

    public StoreEntry Entry => Trail[^1];
    public string Name => Root?.Name ?? Entry.Name;

    /// <summary>Where it is, as Copy path gives it.</summary>
    public string Path { get; }

    /// <summary>The root this node is, when it is one.</summary>
    public StoreRoot? Root { get; internal set; }

    public bool IsRoot => Root is not null;
    public bool IsPlaceholder => _browser is null;
    public IconRef Icon => IsRoot ? _browser!.RootIcon : IconRef.Fluent(IsExpanded ? "folder_open" : "folder");
    public string ClipboardText => Path;

    public ObservableCollection<StoreFolderNode> Children { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Icon))] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) _ = LoadAsync();
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && _browser is not null) _browser.OnNodeSelected(this);
    }

    /// <summary>Reads the subfolders once; every caller waits on the same read.</summary>
    public Task LoadAsync() => _loading ??= ReadAsync();

    /// <summary>Reads the subfolders again where they have been read, keeping open what was open.</summary>
    public async Task ReloadAsync()
    {
        if (_loading is null) return;
        await _loading;
        _loading = ReadAsync();
        await _loading;
        foreach (var child in Children.Where(c => c.IsExpanded).ToList()) await child.ReloadAsync();
    }

    /// <summary>The subfolder with this id, once read.</summary>
    public StoreFolderNode? ChildWithId(string id) => Children.FirstOrDefault(c => !c.IsPlaceholder && c.Entry.Id == id);

    async Task ReadAsync()
    {
        if (_browser is null) return;
        IReadOnlyList<StoreEntry> folders;
        try { folders = await _browser.FoldersInAsync(Entry); }
        catch (Exception e) when (e is not OutOfMemoryException) { folders = []; }

        var kept = Children.Where(c => !c.IsPlaceholder).ToDictionary(c => c.Entry.Id, StringComparer.Ordinal);
        Children.Clear();
        foreach (var folder in folders)
            Children.Add(kept.TryGetValue(folder.Id, out var existing) ? existing : new StoreFolderNode(_browser, [.. Trail, folder], null));
    }
}
