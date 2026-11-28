using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.WindowsFileSystem.Model;

/// <summary>
/// Where a file-system tab is: the folder open in it, the folder its tree is rooted at, and whether it shows This PC or a
/// place inside an archive. <see cref="FolderNavigation"/> moves it; the listing, creating, the actions and the assistant read it.
/// </summary>
public sealed partial class FolderLocation : ObservableObject
{
    /// <summary>The folder open in the tab; empty on This PC.</summary>
    [ObservableProperty] private string _currentPath = string.Empty;

    /// <summary>The folder the tree is rooted at; empty when the tab was reached from This PC.</summary>
    public string RootPath { get; internal set; } = string.Empty;

    /// <summary>The tab shows This PC rather than a folder.</summary>
    public bool IsThisPc { get; internal set; }

    /// <summary>The open folder is an archive or inside one — settled when it was browsed to. Such a place is virtual:
    /// real-folder actions ("Zip It", "New") and watching do not apply there.</summary>
    public bool InContainer { get; internal set; }

    /// <summary>Whether "New" is offered: an open folder on disk, under a mount or on a location — not an archive or a
    /// folder inside one, which changes through the archive members.</summary>
    public bool CanCreateHere => !IsThisPc && !string.IsNullOrEmpty(CurrentPath) && !InContainer;
}
